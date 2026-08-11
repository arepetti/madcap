using System.ComponentModel;
using Debate.Core;
using Debate.Models.FoundryLocal;
using Debate.Models.OpenAICompatible;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Debate.Cli;

/// <summary>
/// Default CLI command: build the host (DI + config + provider selection), start
/// it (which bootstraps the local model backend if selected), run the setup
/// wizard, then enter the debate REPL. Command-line options override config.
/// </summary>
public sealed class RunCommand : AsyncCommand<RunCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-p|--provider <PROVIDER>")]
        [Description("Model provider to use: FoundryLocal (local) or Remote (OpenAI-compatible). Overrides config.")]
        public string? Provider { get; init; }

        [CommandOption("--profile <NAME>")]
        [Description("Foundry Local model profile to use (e.g. 'small' or 'normal'). Overrides config.")]
        public string? Profile { get; init; }

        [CommandOption("--execution-provider <EP>")]
        [Description("Force a Foundry Local execution provider: auto (default), cpu, cuda, or webgpu. 'cpu' avoids GPU out-of-memory. Overrides config.")]
        public string? ExecutionProvider { get; init; }

        [CommandOption("--execution-mode <MODE>")]
        [Description("Model residency for the active profile: 'parallel' (all loaded, fast), 'sequential' (one at a time, lowest memory), or 'semisequential' (Judge stays resident, others cycle). Overrides config.")]
        public string? ExecutionMode { get; init; }

        [CommandOption("--no-window")]
        [Description("Create per-model host processes with no console window (default creates them with a window).")]
        public bool NoWindow { get; init; }

        [CommandOption("--persona <NAME>")]
        [Description("Persona preset to use. Overrides config and skips the persona prompt.")]
        public string? Persona { get; init; }

        [CommandOption("--persona-dir <PATH>")]
        [Description("Directory containing persona .txt files. Overrides config.")]
        public string? PersonaDirectory { get; init; }

        [CommandOption("--no-wizard")]
        [Description("Skip the interactive setup wizard and use configured defaults.")]
        public bool NoWizard { get; init; }

        [CommandOption("--rounds <N>")]
        [Description("Maximum debate rounds per question (the loop still ends early when the Critic is done). Overrides config.")]
        public int? Rounds { get; init; }

        [CommandOption("--prefetch")]
        [Description("Start the model backend (download/register execution providers and models), then exit. Used for setup warm-up.")]
        public bool Prefetch { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });

        ApplyCommandLineOverrides(builder, settings);
        RegisterServices(builder, settings);

        var host = builder.Build();
        try
        {
            if (!await TryStartHostAsync(host, cancellationToken).ConfigureAwait(false))
            {
                return 1;
            }

            return settings.Prefetch
                ? await RunPrefetchAsync(host, cancellationToken).ConfigureAwait(false)
                : await RunReplAsync(host, settings, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await DisposeHostAsync(host).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Disposes the host asynchronously when it supports it. <see cref="IHost"/> only
    /// declares <see cref="IDisposable"/>, but the default implementation is also
    /// <see cref="IAsyncDisposable"/> — and the synchronous path blocks the calling thread
    /// while the Foundry Local provider tears down its child model processes.
    /// </summary>
    private static async ValueTask DisposeHostAsync(IHost host)
    {
        if (host is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            host.Dispose();
        }
    }

    /// <summary>
    /// Layers command-line option overrides on top of appsettings as in-memory config. Each
    /// maps to the same key the providers read, so a flag and a config value are
    /// interchangeable.
    /// </summary>
    private static void ApplyCommandLineOverrides(HostApplicationBuilder builder, Settings settings)
    {
        var overrides = new Dictionary<string, string?>();
        if (settings.Provider is not null)
        {
            overrides["Debate:Provider"] = settings.Provider;
        }

        if (settings.Profile is not null)
        {
            overrides["Debate:FoundryLocal:Profile"] = settings.Profile;
        }

        if (settings.ExecutionProvider is not null)
        {
            overrides["Debate:FoundryLocal:ExecutionProvider"] = settings.ExecutionProvider;
        }

        if (settings.ExecutionMode is not null)
        {
            // ExecutionMode is per profile, so the override targets the active profile.
            var profileName =
                settings.Profile ?? builder.Configuration["Debate:FoundryLocal:Profile"] ?? "small";
            overrides[$"Debate:FoundryLocal:Profiles:{profileName}:ExecutionMode"] = settings.ExecutionMode;
        }

        if (settings.NoWindow)
        {
            overrides["Debate:FoundryLocal:SeparateWindows"] = "false";
        }

        if (settings.Rounds is int rounds)
        {
            overrides["Debate:Defaults:MaxRounds"] = rounds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (overrides.Count > 0)
        {
            builder.Configuration.AddInMemoryCollection(overrides);
        }
    }

    /// <summary>
    /// Registers the host-side services (token counter, personas, console observer and
    /// clarification source, defaults) and the selected model provider — local Foundry by
    /// default, or the remote OpenAI-compatible backend when <c>Debate:Provider</c> is
    /// "Remote".
    /// </summary>
    private static void RegisterServices(HostApplicationBuilder builder, Settings settings)
    {
        var provider = builder.Configuration["Debate:Provider"] ?? "FoundryLocal";
        var personaDirectory =
            settings.PersonaDirectory
            ?? builder.Configuration["Debate:PersonaDirectory"]
            ?? Path.Combine(AppContext.BaseDirectory, "personas");

        builder.Services.AddSingleton<ITokenCounter, TiktokenCounter>();
        builder.Services.AddSingleton(new PersonaLibrary(personaDirectory));
        builder.Services.AddSingleton<ConsoleOutputGate>();
        builder.Services.AddSingleton<IDebateObserver, SpectreDebateObserver>();
        builder.Services.AddSingleton<IClarificationSource, ConsoleClarificationSource>();
        builder.Services.Configure<DebateDefaultsOptions>(
            builder.Configuration.GetSection(DebateDefaultsOptions.SectionName));

        if (string.Equals(provider, "Remote", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddOpenAICompatibleProvider(builder.Configuration);
        }
        else
        {
            builder.Services.AddFoundryLocalProvider(builder.Configuration);
        }
    }

    private static async Task<bool> TryStartHostAsync(IHost host, CancellationToken cancellationToken)
    {
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception e)
        {
            AnsiConsole.MarkupLine($"[red][[error]] failed to start the model backend: {Markup.Escape(e.Message)}[/]");
            return false;
        }
    }

    /// <summary>
    /// Prefetch mode: the started backend has (for Foundry Local) registered execution
    /// providers and cached models. Force every model in the active profile to load once,
    /// regardless of execution mode, then stop and exit so the first interactive run is warm.
    /// Backends without a warm-up step simply have nothing to do here.
    /// </summary>
    private static async Task<int> RunPrefetchAsync(IHost host, CancellationToken cancellationToken)
    {
        try
        {
            if (host.Services.GetRequiredService<IModelProvider>() is IPrefetchable prefetchable)
            {
                await prefetchable.PrefetchAsync(cancellationToken).ConfigureAwait(false);
            }

            AnsiConsole.MarkupLine("[green]Prefetch complete: execution providers and models are cached.[/]");
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 1;
        }
        catch (Exception e)
        {
            AnsiConsole.MarkupLine($"[red][[error]] prefetch failed: {Markup.Escape(e.Message)}[/]");
            return 1;
        }
        finally
        {
            await host.StopAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves the session services, runs the setup wizard (or configured defaults), then
    /// drives the debate REPL until the user exits. Always stops the host on the way out.
    /// </summary>
    private static async Task<int> RunReplAsync(IHost host, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var modelProvider = host.Services.GetRequiredService<IModelProvider>();
            var personas = host.Services.GetRequiredService<PersonaLibrary>();
            var tokenizer = host.Services.GetRequiredService<ITokenCounter>();
            var observer = host.Services.GetRequiredService<IDebateObserver>();
            var clarifications = host.Services.GetRequiredService<IClarificationSource>();
            var defaults = host.Services.GetRequiredService<IOptions<DebateDefaultsOptions>>().Value;
            var gate = host.Services.GetRequiredService<ConsoleOutputGate>();

            var wizard = new SetupWizard(personas, defaults, gate);
            SessionConfig sessionConfig;
            try
            {
                sessionConfig = settings.NoWizard
                    ? wizard.FromDefaults(settings.Persona)
                    : wizard.Run(settings.Persona);
            }
            catch (OperationCanceledException)
            {
                return 0;
            }

            var engine = new DebateEngine(
                sessionConfig, modelProvider, personas, tokenizer, observer, clarifications);

            var loop = new ChatLoop(engine, modelProvider, gate);
            await loop.RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }
        finally
        {
            await host.StopAsync().ConfigureAwait(false);
        }
    }
}
