using Spectre.Console;

namespace Debate.Cli.Commands;

/// <summary>Outcome of a REPL command. <see cref="Exit"/> ends the loop.</summary>
public readonly record struct CommandResult(bool Exit = false);

/// <summary>A "!" command available at the debate prompt.</summary>
public abstract class ReplCommand
{
    public abstract string Name { get; }
    public abstract string Help { get; }
    public abstract Task<CommandResult> ExecuteAsync(string args, CancellationToken cancellationToken);
}

/// <summary>Holds the commands and dispatches a "!name args" line to one.</summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, ReplCommand> _commands = new(StringComparer.Ordinal);

    public void Register(ReplCommand command) => _commands[command.Name] = command;

    public IReadOnlyCollection<ReplCommand> All => _commands.Values;

    /// <summary>
    /// Dispatch a full input line. Returns null if it is not a command (no
    /// leading "!"), otherwise the command's result.
    /// </summary>
    public async Task<CommandResult?> DispatchAsync(string line, CancellationToken cancellationToken)
    {
        if (!line.StartsWith('!'))
        {
            return null;
        }

        var body = line[1..].Trim();
        string name;
        string args;
        int space = body.IndexOf(' ');
        if (space >= 0)
        {
            name = body[..space];
            args = body[(space + 1)..].Trim();
        }
        else
        {
            name = body;
            args = string.Empty;
        }

        if (!_commands.TryGetValue(name, out var command))
        {
            AnsiConsole.MarkupLine($"[yellow][[warning]] unknown command: !{Markup.Escape(name)} (type !help)[/]");
            return new CommandResult();
        }

        return await command.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
    }
}
