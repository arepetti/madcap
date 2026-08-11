using System.ClientModel;
using System.Text;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Debate.Models.FoundryLocal;

/// <summary>
/// The per-model child process runtime. Loads exactly one model alias into this
/// process (its own <see cref="FoundryLocalManager"/> singleton), then serves chat
/// completions over a line-delimited JSON protocol on stdin/stdout. Terminating the
/// process fully releases the model's RAM/VRAM, which is how the parent "unloads" it.
///
/// stdout carries only protocol lines; all human-readable logging goes to stderr so
/// the parent can relay it without corrupting the channel.
/// </summary>
public static class FoundryModelHost
{
    /// <summary>The first CLI argument that selects this mode in the shared executable.</summary>
    public const string ModeArgument = "__serve-model";

    /// <summary>
    /// Entry point for <c>debate __serve-model --alias ... [options]</c>. Returns a
    /// process exit code.
    /// </summary>
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        ModelHostArgs parsed;
        try
        {
            parsed = ModelHostArgs.Parse(args);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[model-host] invalid arguments: {e.Message}");
            return 2;
        }

        void Log(string message) => Console.Error.WriteLine($"[model-host:{parsed.Alias}] {message}");

        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

        var loaded = await TryLoadModelAsync(parsed, Log, stdout, cancellationToken).ConfigureAwait(false);
        if (loaded is null)
        {
            return 1;
        }

        var (chatClient, modelId) = loaded.Value;
        await WriteLineAsync(stdout, new HostResponse { Ready = true, Model = modelId }).ConfigureAwait(false);
        Log("Listening for requests on stdin.");

        await ServeRequestsAsync(chatClient, Log, stdout, cancellationToken).ConfigureAwait(false);

        Log("Shutting down.");
        await StopManagerAsync().ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Runs the full load sequence (manager init, EP registration, variant selection/load)
    /// and builds a chat client for the model. Returns null on failure, having already
    /// reported it on stderr and — for non-cancellation failures — as a protocol error line
    /// so the parent's readiness wait fails fast instead of hanging.
    /// </summary>
    private static async Task<(IChatClient ChatClient, string ModelId)?> TryLoadModelAsync(
        ModelHostArgs parsed, Action<string> log, StreamWriter stdout, CancellationToken cancellationToken)
    {
        try
        {
            var loader = new FoundryModelLoader(log);
            var manager = await loader.EnsureManagerAsync(
                parsed.AppName, parsed.ModelCacheDir, parsed.WebServiceUrl, cancellationToken).ConfigureAwait(false);

            var forcedEp = FoundryModelLoader.MapExecutionProvider(parsed.ExecutionProvider);
            var registeredEps = await loader.RegisterExecutionProvidersAsync(
                manager, parsed.RegisterExecutionProviders, forcedEp, cancellationToken).ConfigureAwait(false);
            var effectiveEps = loader.ResolveEffectiveEps(forcedEp, registeredEps);

            log($"Preparing model '{parsed.Alias}'...");
            var model = await loader.GetModelAsync(manager, parsed.Alias, cancellationToken).ConfigureAwait(false);
            var target = await loader.LoadCompatibleVariantAsync(model, parsed.Alias, effectiveEps, cancellationToken)
                .ConfigureAwait(false);
            log($"Model '{parsed.Alias}' ready ({target.Id}).");

            var chatClient = await BuildChatClientAsync(manager, target.Id, parsed.RequestTimeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
            return (chatClient, target.Id);
        }
        // A timeout during download or EP registration also arrives as an
        // OperationCanceledException; only a real shutdown should skip the error line the
        // parent's readiness wait depends on.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception e)
        {
            // Report startup failure on both channels: stderr for humans, and a protocol
            // error line so the parent's readiness wait fails fast instead of hanging.
            log($"failed to start: {e.Message}");
            await WriteLineAsync(stdout, new HostResponse { Error = e.Message }).ConfigureAwait(false);
            return null;
        }
    }

    /// <summary>
    /// Starts the Foundry Local web service and builds an OpenAI-client chat client for the
    /// loaded model. Foundry Local serves an OpenAI-compatible HTTP endpoint on loopback, so
    /// calls go through the OpenAI client's pipeline — including its 100s default per-request
    /// timeout. Local inference can exceed that, so raise NetworkTimeout accordingly.
    /// </summary>
    private static async Task<IChatClient> BuildChatClientAsync(
        FoundryLocalManager manager, string modelId, int requestTimeoutSeconds, CancellationToken cancellationToken)
    {
        await manager.StartWebServiceAsync(cancellationToken).ConfigureAwait(false);
        var baseUrl = manager.Urls?.FirstOrDefault()
            ?? throw new InvalidOperationException("Foundry Local web service did not report a URL.");
        var endpoint = new Uri(baseUrl.TrimEnd('/') + "/v1");

        var clientOptions = new OpenAIClientOptions { Endpoint = endpoint };
        clientOptions.NetworkTimeout = requestTimeoutSeconds > 0
            ? TimeSpan.FromSeconds(requestTimeoutSeconds)
            : Timeout.InfiniteTimeSpan;

        var openAiClient = new OpenAIClient(new ApiKeyCredential("not-needed"), clientOptions);
        return openAiClient.GetChatClient(modelId).AsIChatClient();
    }

    /// <summary>
    /// Reads line-delimited requests from stdin and serves each one until stdin closes, a
    /// shutdown request arrives, the request is cancelled, or cancellation is requested.
    /// Malformed lines are logged and skipped.
    /// </summary>
    private static async Task ServeRequestsAsync(
        IChatClient chatClient, Action<string> log, StreamWriter stdout, CancellationToken cancellationToken)
    {
        using var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await stdin.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break; // stdin closed: parent went away.
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var request = ModelHostProtocol.DeserializeRequest(line);
            if (request is null)
            {
                log($"ignoring malformed request line: {Truncate(line)}");
                continue;
            }

            if (request.Shutdown)
            {
                break;
            }

            if (!await TryServeRequestAsync(chatClient, request, log, stdout, cancellationToken).ConfigureAwait(false))
            {
                break;
            }
        }
    }

    /// <summary>
    /// Completes one chat request and writes its reply. Returns false only when the request
    /// was cancelled (so the serve loop stops); other failures are reported as a protocol
    /// error line and return true so the loop keeps serving.
    /// </summary>
    private static async Task<bool> TryServeRequestAsync(
        IChatClient chatClient, HostRequest request, Action<string> log, StreamWriter stdout, CancellationToken cancellationToken)
    {
        try
        {
            var messages = request.Messages
                .Select(m => new ChatMessage(ToChatRole(m.Role), m.Text))
                .ToList();
            var options = new ChatOptions
            {
                Temperature = request.Temperature,
                MaxOutputTokens = request.MaxOutputTokens,
            };

            var response = await chatClient.GetResponseAsync(messages, options, cancellationToken)
                .ConfigureAwait(false);
            var text = (response.Text ?? string.Empty).Trim();

            await WriteLineAsync(stdout, new HostResponse { Id = request.Id, Text = text }).ConfigureAwait(false);
            return true;
        }
        // Only a genuine shutdown request stops the loop. The OpenAI client surfaces its
        // NetworkTimeout (see BuildChatClientAsync) as a TaskCanceledException, which is an
        // OperationCanceledException too: reporting that as an error keeps the model loaded
        // instead of silently exiting and forcing a multi-minute reload.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception e)
        {
            log($"request {request.Id} failed: {e.Message}");
            await WriteLineAsync(stdout, new HostResponse { Id = request.Id, Error = e.Message }).ConfigureAwait(false);
            return true;
        }
    }

    private static async Task StopManagerAsync()
    {
        if (!FoundryLocalManager.IsInitialized)
        {
            return;
        }

        try
        {
            await FoundryLocalManager.Instance.StopWebServiceAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: the process is exiting anyway.
        }

        FoundryLocalManager.Instance.Dispose();
    }

    private static async Task WriteLineAsync(StreamWriter writer, HostResponse response)
    {
        await writer.WriteLineAsync(ModelHostProtocol.SerializeResponse(response)).ConfigureAwait(false);
    }

    private static ChatRole ToChatRole(string role) => role?.ToLowerInvariant() switch
    {
        "system" => ChatRole.System,
        "assistant" => ChatRole.Assistant,
        "tool" => ChatRole.Tool,
        _ => ChatRole.User,
    };

    private static string Truncate(string line) =>
        line.Length <= 200 ? line : string.Concat(line.AsSpan(0, 200), "...");
}

/// <summary>
/// Parsed <c>--key value</c> arguments for <see cref="FoundryModelHost"/>.
/// </summary>
public sealed class ModelHostArgs
{
    public string Alias { get; private set; } = string.Empty;
    public string AppName { get; private set; } = "debate";
    public string? ModelCacheDir { get; private set; }
    public string WebServiceUrl { get; private set; } = "http://127.0.0.1:0";
    public string? ExecutionProvider { get; private set; }
    public bool RegisterExecutionProviders { get; private set; } = true;
    public int ContextSize { get; private set; } = 8192;
    public int RequestTimeoutSeconds { get; private set; } = 600;

    public static ModelHostArgs Parse(string[] args)
    {
        var result = new ModelHostArgs();
        for (int i = 0; i < args.Length; i++)
        {
            var key = args[i];
            string? Next()
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException($"missing value for '{key}'");
                }

                return args[++i];
            }

            switch (key)
            {
                case "--alias":
                    result.Alias = Next() ?? string.Empty;
                    break;
                case "--app-name":
                    result.AppName = Next() ?? result.AppName;
                    break;
                case "--cache-dir":
                    result.ModelCacheDir = Next();
                    break;
                case "--web-service-url":
                    result.WebServiceUrl = Next() ?? result.WebServiceUrl;
                    break;
                case "--execution-provider":
                    result.ExecutionProvider = Next();
                    break;
                case "--register-eps":
                    result.RegisterExecutionProviders = ParseBool(Next());
                    break;
                case "--context-size":
                    result.ContextSize = ParseInt(Next(), result.ContextSize);
                    break;
                case "--request-timeout-seconds":
                    result.RequestTimeoutSeconds = ParseInt(Next(), result.RequestTimeoutSeconds);
                    break;
                default:
                    // Ignore unknown flags so the protocol can evolve compatibly.
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(result.Alias))
        {
            throw new ArgumentException("'--alias' is required");
        }

        return result;
    }

    private static bool ParseBool(string? value) =>
        bool.TryParse(value, out var b) ? b : !string.Equals(value, "0", StringComparison.Ordinal);

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out var n) ? n : fallback;
}
