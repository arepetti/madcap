using System.Diagnostics;
using System.Reflection;
using Debate.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Debate.Models.FoundryLocal;

/// <summary>
/// A Foundry Local backend where each role's model runs in its own child process of
/// the same executable (re-invoked with <see cref="FoundryModelHost.ModeArgument"/>),
/// communicating over a line-delimited JSON protocol on stdin/stdout.
///
/// The active profile's <see cref="ModelExecutionMode"/> decides residency:
/// <list type="bullet">
/// <item><see cref="ModelExecutionMode.Parallel"/>: every model process is started up
/// front and kept resident (fast).</item>
/// <item><see cref="ModelExecutionMode.Sequential"/>: at most one model process is
/// resident; before serving a role, the others are terminated, fully releasing their
/// RAM/VRAM (low peak memory).</item>
/// <item><see cref="ModelExecutionMode.SemiSequential"/>: the Judge stays resident while
/// the Answerer and Critic cycle one at a time (peak = Judge + one other), avoiding
/// constant Judge reloads.</item>
/// </list>
/// The debate algorithm only ever sees <see cref="IModelProvider"/>.
/// </summary>
public sealed class ProcessModelProvider :
    IModelProvider, IBackendDiagnostics, IPrefetchable, IHostedService, IDisposable, IAsyncDisposable
{
    /// <summary>
    /// How long <see cref="TryDescribeProcesses"/> waits for the lifecycle lock before
    /// reporting "busy". Short by design: it serves a display, not the debate.
    /// </summary>
    private static readonly TimeSpan DescribeLockTimeout = TimeSpan.FromMilliseconds(250);


    /// <summary>
    /// How long shutdown waits for the lifecycle lock before tearing the child processes
    /// down anyway. In Sequential/SemiSequential mode that lock is held for the whole
    /// duration of a model call, so waiting for it unconditionally would hang shutdown
    /// behind an in-flight inference.
    /// </summary>
    private static readonly TimeSpan ShutdownLockTimeout = TimeSpan.FromSeconds(5);

    private readonly FoundryLocalOptions _options;
    private readonly IDebateObserver _observer;
    private readonly ILogger<ProcessModelProvider> _logger;

    // Guards all process lifecycle mutations (start/stop). In Sequential mode it is
    // also held across a request so role switches and inference are serialized.
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly Dictionary<string, ManagedModelProcess> _processes = new(StringComparer.Ordinal);

    private bool _started;

    // Background model warm-up kicked off by StartAsync so the REPL is interactive
    // immediately instead of blocking on model loads. Cancelled/awaited on shutdown.
    private readonly CancellationTokenSource _warmupCts = new();
    private Task? _warmupTask;

    // This instance is registered under three service types (itself, IModelProvider,
    // IHostedService), so the DI container can call Dispose() more than once. Guard so
    // the second call does not touch the already-disposed lifecycle lock.
    private bool _disposed;

    public ProcessModelProvider(
        IOptions<FoundryLocalOptions> options,
        IDebateObserver observer,
        ILogger<ProcessModelProvider>? logger = null)
    {
        _options = options.Value;
        _observer = observer;
        _logger = logger ?? NullLogger<ProcessModelProvider>.Instance;
    }

    public int EffectiveContextSize => _options.ContextSize;

    public string ModelName(DebateRole role) => AliasFor(role);

    public int? MaxOutputTokens(DebateRole role)
    {
        var limit = ActiveProfile().MaxOutputTokens?.For(role);
        return limit is > 0 ? limit : null;
    }

    public IChatClient GetClient(DebateRole role)
    {
        if (!_started)
        {
            throw new InvalidOperationException(
                "Foundry Local provider has not been initialized. Ensure the host has started.");
        }

        return new ProcessChatClient(this, role);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var profile = ActiveProfile();
        _observer.OnInfo(
            $"Using model profile '{_options.Profile}' " +
            $"(Answerer: {profile.Answerer}, Critic: {profile.Critic}, Judge: {profile.Judge}; " +
            $"execution: {profile.ExecutionMode}, provider: {ResolveExecutionProviderArg()}).");

        // Mark ready immediately so the REPL is interactive right away: the user can type
        // the first question while models load. Any request that arrives before its model
        // is up will start/await it on demand (EnsureStarted is idempotent under the lock,
        // so it shares whatever the warm-up already started).
        _started = true;

        var toPreload = PreloadAliases();
        if (toPreload.Count == 0)
        {
            _observer.OnInfo("Foundry Local is ready (models load on demand).");
            return Task.CompletedTask;
        }

        // Warm up in the background. The Judge is loaded first because it is needed for the
        // very first action (the rephrase); the Answerer (and Critic) follow so they are
        // ready, or still loading, by the time the debate reaches them.
        _observer.OnInfo(
            $"Foundry Local is ready. Preloading {string.Join(", ", toPreload)} in the background " +
            "(Judge first) — you can type your question now.");
        _warmupTask = Task.Run(() => WarmUpAsync(toPreload, _warmupCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Models to load eagerly in the background at startup, in priority order. The Judge
    /// always comes first (the first turn needs it). In <see cref="ModelExecutionMode.Parallel"/>
    /// the Answerer and Critic follow so the whole lineup ends up resident. In Sequential /
    /// SemiSequential only the Judge is preloaded: those modes will not keep the Answerer
    /// resident through the Judge's rephrase anyway, so it is loaded on demand instead.
    /// </summary>
    private IReadOnlyList<string> PreloadAliases()
    {
        var ordered = new List<string> { AliasFor(DebateRole.Judge) };
        if (ActiveProfile().ExecutionMode == ModelExecutionMode.Parallel)
        {
            ordered.Add(AliasFor(DebateRole.Answerer));
            ordered.Add(AliasFor(DebateRole.Critic));
        }

        return ordered.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Starts each preload model once, in order, releasing the lifecycle lock between
    /// models so a real request (e.g. the Judge rephrase) can interleave rather than wait
    /// for the whole lineup. Failures are logged and do not abort the rest or the host:
    /// the on-demand path will retry when the model is actually needed.
    /// </summary>
    private async Task WarmUpAsync(IReadOnlyList<string> aliases, CancellationToken cancellationToken)
    {
        foreach (var alias in aliases)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await EnsureStartedAsync(alias, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _lifecycleLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                _observer.OnWarning($"background preload of '{alias}' failed: {e.Message} (will retry on demand)");
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await CancelWarmupAsync().ConfigureAwait(false);
        await ShutdownAllAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task CancelWarmupAsync()
    {
        _warmupCts.Cancel();
        if (_warmupTask is not null)
        {
            try
            {
                await _warmupTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when warm-up is interrupted by shutdown.
            }
            catch
            {
                // WarmUpAsync already logs its own failures; nothing to do on shutdown.
            }
        }
    }

    /// <summary>
    /// Synchronous disposal is retained for the DI container, which may call it on a
    /// container that was not disposed asynchronously. Prefer <see cref="DisposeAsync"/>:
    /// this path blocks the calling thread on the shutdown sequence.
    /// </summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await CancelWarmupAsync().ConfigureAwait(false);
            await ShutdownAllAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Dispose();
            _warmupCts.Dispose();
        }
    }

    /// <summary>
    /// Starts each distinct model once (downloading and loading it), then stops it.
    /// Used by the <c>--prefetch</c> warm-up so first interactive use is fast.
    /// </summary>
    public async Task PrefetchAsync(CancellationToken cancellationToken)
    {
        // Prefetch drives its own full load/stop loop; stop the background warm-up first
        // so the two do not contend for the lifecycle lock or duplicate work.
        await CancelWarmupAsync().ConfigureAwait(false);

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var alias in DistinctAliases())
            {
                await EnsureStartedAsync(alias, cancellationToken).ConfigureAwait(false);
                await StopProcessAsync(alias).ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// Completes a chat request for a role, honoring the active execution mode. In
    /// Sequential mode this terminates other model processes and serializes the call;
    /// in Parallel mode it routes to the (already resident) process.
    /// </summary>
    internal async Task<string> CompleteAsync(
        DebateRole role, List<HostMessage> messages, float? temperature, int? maxOutputTokens, CancellationToken cancellationToken)
    {
        var alias = AliasFor(role);

        // Parallel keeps everything resident, so only a brief lock to (lazily) ensure the
        // process exists is needed; requests then run concurrently per process.
        if (ActiveProfile().ExecutionMode == ModelExecutionMode.Parallel)
        {
            ManagedModelProcess started;
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                started = await EnsureStartedAsync(alias, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _lifecycleLock.Release();
            }

            return await started.SendAsync(messages, temperature, maxOutputTokens, cancellationToken).ConfigureAwait(false);
        }

        // Sequential / SemiSequential: hold the lifecycle lock for the whole call so the
        // resident set is enforced and no other role may evict this model mid-request.
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var process = await EnsureResidentAsync(alias, cancellationToken).ConfigureAwait(false);
            return await process.SendAsync(messages, temperature, maxOutputTokens, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    // ----- lifecycle helpers (callers must hold _lifecycleLock) -----

    private async Task<ManagedModelProcess> EnsureStartedAsync(string alias, CancellationToken cancellationToken)
    {
        if (_processes.TryGetValue(alias, out var existing) && existing.IsAlive)
        {
            return existing;
        }

        if (existing is not null)
        {
            await existing.DisposeAsync().ConfigureAwait(false);
            _processes.Remove(alias);
        }

        _observer.OnStatus($"Starting model host for '{alias}'...");
        var process = ManagedModelProcess.Start(alias, BuildHostArguments(alias), _options.SeparateWindows, _observer, _logger);
        _processes[alias] = process;
        await process.WaitForReadyAsync(cancellationToken).ConfigureAwait(false);
        return process;
    }

    /// <summary>
    /// Ensures the requested model (plus any pinned models, e.g. the Judge in
    /// SemiSequential mode) are resident and every other model process is terminated,
    /// then returns the requested model's process.
    /// </summary>
    private async Task<ManagedModelProcess> EnsureResidentAsync(string alias, CancellationToken cancellationToken)
    {
        var keep = new HashSet<string>(StringComparer.Ordinal) { alias };
        keep.UnionWith(PinnedAliases());

        foreach (var other in _processes.Keys.Where(k => !keep.Contains(k)).ToArray())
        {
            await StopProcessAsync(other).ConfigureAwait(false);
        }

        // Keep pinned models loaded (start them if a prior run never needed them yet).
        foreach (var pinned in PinnedAliases())
        {
            if (!string.Equals(pinned, alias, StringComparison.Ordinal))
            {
                await EnsureStartedAsync(pinned, cancellationToken).ConfigureAwait(false);
            }
        }

        return await EnsureStartedAsync(alias, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Aliases that must stay resident regardless of the active role under the current
    /// execution mode. SemiSequential pins the Judge (used between every step); other
    /// modes pin nothing (Parallel keeps all resident anyway; Sequential keeps only the
    /// active model).
    /// </summary>
    private IEnumerable<string> PinnedAliases() => ActiveProfile().ExecutionMode switch
    {
        ModelExecutionMode.SemiSequential => [AliasFor(DebateRole.Judge)],
        _ => [],
    };

    private async Task StopProcessAsync(string alias)
    {
        if (_processes.TryGetValue(alias, out var process))
        {
            _processes.Remove(alias);
            _observer.OnStatus($"Unloading model '{alias}'...");
            await process.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Terminates every model host process. The lifecycle lock is only *attempted*: in
    /// Sequential/SemiSequential mode it is held for the whole duration of a model call,
    /// and shutdown must not be hostage to an inference that may run for minutes. On
    /// timeout the children are torn down anyway — killing them is precisely what an
    /// abandoned request needs, and each child also exits on its own when stdin closes.
    /// </summary>
    private async Task ShutdownAllAsync(CancellationToken cancellationToken)
    {
        var acquired = await TryAcquireLifecycleLockAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var process in _processes.Values)
            {
                await process.DisposeAsync().ConfigureAwait(false);
            }

            _processes.Clear();
        }
        finally
        {
            if (acquired)
            {
                _lifecycleLock.Release();
            }
        }
    }

    private async Task<bool> TryAcquireLifecycleLockAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await _lifecycleLock.WaitAsync(ShutdownLockTimeout, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            _logger.LogDebug(
                "Shutting down model hosts without the lifecycle lock: still held after {Timeout}.",
                ShutdownLockTimeout);
        }
        catch (OperationCanceledException)
        {
            // Shutdown was itself cancelled (host shutdown timeout): tear down regardless.
        }

        return false;
    }

    // ----- argument / configuration plumbing -----

    private IReadOnlyList<string> BuildHostArguments(string alias)
    {
        var args = new List<string>
        {
            FoundryModelHost.ModeArgument,
            "--alias", alias,
            "--app-name", _options.AppName,
            "--web-service-url", _options.WebServiceUrl,
            "--execution-provider", ResolveExecutionProviderArg(),
            "--register-eps", _options.RegisterExecutionProviders ? "true" : "false",
            "--context-size", _options.ContextSize.ToString(),
            "--request-timeout-seconds", _options.RequestTimeoutSeconds.ToString(),
        };

        if (!string.IsNullOrWhiteSpace(_options.ModelCacheDir))
        {
            args.Add("--cache-dir");
            args.Add(_options.ModelCacheDir);
        }

        return args;
    }

    /// <summary>
    /// Resolves the execution-provider string passed to child hosts. A concrete
    /// top-level <see cref="FoundryLocalOptions.ExecutionProvider"/> (set by
    /// <c>--execution-provider</c>) wins; otherwise the active profile's value; otherwise
    /// <c>auto</c>.
    /// </summary>
    private string ResolveExecutionProviderArg()
    {
        if (FoundryModelLoader.MapExecutionProvider(_options.ExecutionProvider) is not null)
        {
            return _options.ExecutionProvider;
        }

        var profileEp = ActiveProfile().ExecutionProvider;
        if (FoundryModelLoader.MapExecutionProvider(profileEp) is not null)
        {
            return profileEp!;
        }

        return "auto";
    }

    private ModelProfile ActiveProfile()
    {
        if (_options.Profiles is null || _options.Profiles.Count == 0)
        {
            throw new InvalidOperationException(
                $"No Foundry Local model profiles are configured. " +
                $"Define '{FoundryLocalOptions.SectionName}:Profiles' in appsettings.json.");
        }

        var name = _options.Profile;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                $"No active Foundry Local model profile selected. " +
                $"Set '{FoundryLocalOptions.SectionName}:Profile' (or pass --profile). " +
                $"Available: {string.Join(", ", _options.Profiles.Keys)}.");
        }

        if (!_options.Profiles.TryGetValue(name, out var map))
        {
            throw new InvalidOperationException(
                $"Foundry Local model profile '{name}' is not defined. " +
                $"Available: {string.Join(", ", _options.Profiles.Keys)}.");
        }

        return map;
    }

    private string AliasFor(DebateRole role)
    {
        var models = ActiveProfile();
        var alias = role switch
        {
            DebateRole.Answerer => models.Answerer,
            DebateRole.Critic => models.Critic,
            DebateRole.Judge => models.Judge,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new InvalidOperationException(
                $"No Foundry Local model configured for the {role} role in profile '{_options.Profile}'. " +
                $"Set '{FoundryLocalOptions.SectionName}:Profiles:{_options.Profile}:{role}' in appsettings.json.");
        }

        return alias;
    }

    private IEnumerable<string> DistinctAliases() => Enum
        .GetValues<DebateRole>()
        .Select(AliasFor)
        .Distinct(StringComparer.Ordinal);

    private IReadOnlyList<DebateRole> RolesFor(string alias) => Enum
        .GetValues<DebateRole>()
        .Where(r => string.Equals(AliasFor(r), alias, StringComparison.Ordinal))
        .ToList();

    /// <summary>
    /// A snapshot of the model host child processes that are currently resident (which
    /// depends on the execution mode and what has run so far). Returns null rather than
    /// waiting out the lifecycle lock, which a model load or a Sequential-mode inference
    /// can hold for minutes — the REPL invites the user to type while models load, so
    /// this must not be able to freeze the prompt.
    /// </summary>
    public IReadOnlyList<BackendProcessInfo>? TryDescribeProcesses()
    {
        if (!_lifecycleLock.Wait(DescribeLockTimeout))
        {
            return null;
        }

        try
        {
            return _processes
                .Select(kvp => new BackendProcessInfo(
                    kvp.Value.ProcessId,
                    kvp.Key,
                    RolesFor(kvp.Key),
                    kvp.Value.IsAlive))
                .OrderBy(p => p.Pid)
                .ToList();
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// A thin <see cref="IChatClient"/> over a role: forwards each request to the
    /// provider, which routes it to the role's (possibly just-started) model process.
    /// Only the non-streaming path is implemented; the debate never streams.
    /// </summary>
    private sealed class ProcessChatClient(ProcessModelProvider provider, DebateRole role) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var payload = messages
                .Select(m => new HostMessage { Role = m.Role.Value, Text = m.Text })
                .ToList();

            var text = await provider
                .CompleteAsync(role, payload, options?.Temperature, options?.MaxOutputTokens, cancellationToken)
                .ConfigureAwait(false);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The Foundry Local process backend does not support streaming responses.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// Owns a single model host child process and its stdio: writes JSON requests to its
/// stdin, reads correlated JSON replies from its stdout, and relays its stderr (model
/// load progress and diagnostics) to the observer. Disposal shuts the child down
/// gracefully, then kills it if it does not exit promptly.
/// </summary>
internal sealed class ManagedModelProcess : IAsyncDisposable
{
    private static readonly TimeSpan GracefulShutdownTimeout = TimeSpan.FromSeconds(3);

    private readonly Process _process;
    private readonly string _alias;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private int _nextId;
    private bool _disposed;

    private ManagedModelProcess(Process process, string alias)
    {
        _process = process;
        _alias = alias;
    }

    public bool IsAlive
    {
        get
        {
            try
            {
                return !_process.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>The child process's OS process id, or -1 if it is unavailable.</summary>
    public int ProcessId
    {
        get
        {
            try
            {
                return _process.Id;
            }
            catch
            {
                return -1;
            }
        }
    }

    public static ManagedModelProcess Start(
        string alias,
        IReadOnlyList<string> hostArguments,
        bool separateWindows,
        IDebateObserver observer,
        ILogger logger)
    {
        var process = new Process { StartInfo = CreateStartInfo(hostArguments, separateWindows), EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start model host process for '{alias}'.");
        }

        var managed = new ManagedModelProcess(process, alias);
        managed.BeginRelayingStandardError(observer, logger);
        return managed;
    }

    private static ProcessStartInfo CreateStartInfo(IReadOnlyList<string> hostArguments, bool separateWindows)
    {
        var (fileName, prefix) = ResolveHostCommand();

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // The window flag only suppresses console-window creation; stdio is always
            // redirected for the protocol regardless.
            CreateNoWindow = !separateWindows,
            WorkingDirectory = AppContext.BaseDirectory,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        foreach (var arg in prefix)
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (var arg in hostArguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        return startInfo;
    }

    /// <summary>
    /// Waits for the child's one-time readiness line. Throws if the child reports a
    /// startup error or exits first.
    /// </summary>
    public async Task WaitForReadyAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await _process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                throw new InvalidOperationException(
                    $"Model host for '{_alias}' exited during startup (exit code {SafeExitCode()}).");
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var response = ModelHostProtocol.DeserializeResponse(line);
            if (response is null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(response.Error))
            {
                throw new InvalidOperationException(
                    $"Model host for '{_alias}' failed to start: {response.Error}");
            }

            if (response.Ready)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Sends one chat request and returns the reply text. Serialized per process so a
    /// single stdout stream is never interleaved.
    /// </summary>
    public async Task<string> SendAsync(
        List<HostMessage> messages, float? temperature, int? maxOutputTokens, CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsAlive)
            {
                throw new InvalidOperationException($"Model host for '{_alias}' is not running.");
            }

            var id = ++_nextId;
            var request = new HostRequest
            {
                Id = id,
                Temperature = temperature,
                MaxOutputTokens = maxOutputTokens,
                Messages = messages,
            };
            await _process.StandardInput.WriteLineAsync(ModelHostProtocol.SerializeRequest(request).AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

            return await ReadReplyAsync(id, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _requestLock.Release();
        }
    }

    /// <summary>
    /// Reads stdout until the reply correlated with <paramref name="id"/> arrives, skipping
    /// stray lines (e.g. a late readiness echo). Throws if the stream closes first or the
    /// reply carries an error.
    /// </summary>
    private async Task<string> ReadReplyAsync(int id, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await _process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                throw new InvalidOperationException(
                    $"Model host for '{_alias}' closed its output (exit code {SafeExitCode()}).");
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var response = ModelHostProtocol.DeserializeResponse(line);
            if (response is null || response.Id != id)
            {
                continue; // stray line (e.g. a late readiness echo); ignore.
            }

            if (!string.IsNullOrEmpty(response.Error))
            {
                throw new InvalidOperationException(response.Error);
            }

            return response.Text ?? string.Empty;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (IsAlive)
            {
                await ShutDownGracefullyAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            TryKill();
        }
        finally
        {
            _requestLock.Dispose();
            _process.Dispose();
        }
    }

    /// <summary>
    /// Asks the child to exit cleanly, then waits up to <see cref="GracefulShutdownTimeout"/>
    /// before forcing it with portable termination (Windows: TerminateProcess; Unix: SIGKILL).
    /// </summary>
    private async Task ShutDownGracefullyAsync()
    {
        // One budget for the whole attempt. The write matters as much as the wait: a child
        // wedged mid-inference stops reading stdin, so an unbounded WriteLineAsync blocks
        // forever once the pipe buffer fills — no exception is ever thrown to escape it.
        using var cts = new CancellationTokenSource(GracefulShutdownTimeout);
        try
        {
            var shutdown = ModelHostProtocol.SerializeRequest(new HostRequest { Shutdown = true });
            await _process.StandardInput.WriteLineAsync(shutdown.AsMemory(), cts.Token).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cts.Token).ConfigureAwait(false);
            _process.StandardInput.Close();
        }
        catch
        {
            // Pipe may already be gone, or the child stopped reading; fall through to wait/kill.
        }

        try
        {
            await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill();
        }
    }

    private void TryKill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Already gone.
        }
    }

    private int SafeExitCode()
    {
        try
        {
            return _process.HasExited ? _process.ExitCode : -1;
        }
        catch
        {
            return -1;
        }
    }

    private void BeginRelayingStandardError(IDebateObserver observer, ILogger logger)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var line = await _process.StandardError.ReadLineAsync().ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        observer.OnStatus(line);
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "Stopped relaying stderr for model host '{Alias}'.", _alias);
            }
        });
    }

    /// <summary>
    /// Resolves how to launch another instance of this executable in model-host mode.
    /// Normally this is the current process's exe; under <c>dotnet run</c> the current
    /// process is the <c>dotnet</c> muxer, so we launch it with the entry assembly dll.
    /// </summary>
    private static (string FileName, IReadOnlyList<string> Prefix) ResolveHostCommand()
    {
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the current executable path.");

        var name = Path.GetFileNameWithoutExtension(exe);
        if (string.Equals(name, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entry = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrEmpty(entry))
            {
                return (exe, new[] { entry });
            }
        }

        return (exe, Array.Empty<string>());
    }
}
