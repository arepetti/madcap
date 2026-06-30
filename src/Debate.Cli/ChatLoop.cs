using Debate.Cli.Commands;
using Debate.Core;
using Spectre.Console;

namespace Debate.Cli;

/// <summary>
/// Interactive read-eval-print loop: read a line, dispatch "!" commands,
/// otherwise hand the line to the <see cref="DebateEngine"/>. Knows nothing
/// about phases or actors — that all lives in Debate.Core.
/// </summary>
public sealed class ChatLoop
{
    private readonly DebateEngine _engine;
    private readonly CommandRegistry _registry;
    private readonly ConsoleOutputGate _gate;

    public ChatLoop(DebateEngine engine, IModelProvider provider, ConsoleOutputGate gate)
    {
        _engine = engine;
        _gate = gate;
        _registry = new CommandRegistry();
        _registry.Register(new HelpCommand(_registry));
        _registry.Register(new NewSessionCommand(engine));
        _registry.Register(new PersonasCommand(engine));
        _registry.Register(new ContextCommand(engine));
        _registry.Register(new StatsCommand(engine, provider));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine($"Multi-agent debate ready. Persona: '[bold]{Markup.Escape(_engine.Config.PersonaName)}[/]'.");
        AnsiConsole.MarkupLine("Type [green]!help[/] for commands. Empty input exits.");
        AnsiConsole.WriteLine();

        while (!cancellationToken.IsCancellationRequested)
        {
            // Suspend background output (e.g. model-loading logs) while we show the prompt
            // and wait for input, so queued lines flush only after the user has typed.
            string? line;
            using (_gate.Suspend())
            {
                AnsiConsole.Markup("[bold]>>>[/] ");
                line = Console.ReadLine();
            }

            if (line is null || string.IsNullOrWhiteSpace(line))
            {
                break;
            }

            var result = await _registry.DispatchAsync(line.Trim(), cancellationToken).ConfigureAwait(false);
            if (result is not null)
            {
                if (result.Value.Exit)
                {
                    break;
                }

                continue;
            }

            try
            {
                await _engine.RunQuestionAsync(line, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                AnsiConsole.MarkupLine($"[red][[error]] during debate: {Markup.Escape(e.Message)}[/]");
            }
        }
    }
}
