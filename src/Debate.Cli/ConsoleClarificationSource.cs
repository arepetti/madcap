using Debate.Core;
using Spectre.Console;

namespace Debate.Cli;

/// <summary>
/// Reads clarification replies from the console. The Judge's CLARIFY: message is
/// already shown by the observer, so this only prompts for input. An empty line
/// is a skip (the pipeline aborts the question); EOF returns null (cancelled).
/// </summary>
public sealed class ConsoleClarificationSource : IClarificationSource
{
    private readonly ConsoleOutputGate _gate;

    public ConsoleClarificationSource(ConsoleOutputGate gate) => _gate = gate;

    public Task<string?> RequestClarificationAsync(string judgeMessage, CancellationToken cancellationToken)
    {
        string? line;
        using (_gate.Suspend())
        {
            AnsiConsole.Markup("    your reply [grey]>>>[/] ");
            line = Console.ReadLine();
        }

        return Task.FromResult(line);
    }
}
