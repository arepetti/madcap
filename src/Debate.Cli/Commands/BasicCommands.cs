using Debate.Core;
using Spectre.Console;

namespace Debate.Cli.Commands;

/// <summary>!help — list commands.</summary>
public sealed class HelpCommand : ReplCommand
{
    private readonly CommandRegistry _registry;

    public HelpCommand(CommandRegistry registry) => _registry = registry;

    public override string Name => "help";
    public override string Help => "show this help";

    public override Task<CommandResult> ExecuteAsync(string args, CancellationToken cancellationToken)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Commands:[/]");
        foreach (var cmd in _registry.All)
        {
            AnsiConsole.MarkupLine($"  [green]!{cmd.Name,-11}[/] {Markup.Escape(cmd.Help)}");
        }

        AnsiConsole.MarkupLine("  [green]<empty>    [/] exit");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Anything else is a question for the debate.");
        AnsiConsole.WriteLine();
        return Task.FromResult(new CommandResult());
    }
}

/// <summary>!new — clear the session.</summary>
public sealed class NewSessionCommand : ReplCommand
{
    private readonly DebateEngine _engine;

    public NewSessionCommand(DebateEngine engine) => _engine = engine;

    public override string Name => "new";
    public override string Help => "start a new session (clear Answerer memory, history, profile)";

    public override async Task<CommandResult> ExecuteAsync(string args, CancellationToken cancellationToken)
    {
        await _engine.ClearSessionAsync().ConfigureAwait(false);
        AnsiConsole.MarkupLine("[grey][[context cleared]][/]");
        AnsiConsole.WriteLine();
        return new CommandResult();
    }
}

/// <summary>!context — show exactly what each actor receives (system prompt, buffer, prompts).</summary>
public sealed class ContextCommand : ReplCommand
{
    private readonly DebateEngine _engine;

    public ContextCommand(DebateEngine engine) => _engine = engine;

    public override string Name => "context";
    public override string Help => "show what each actor receives (system prompt, conversation buffer, per-phase prompts)";

    public override Task<CommandResult> ExecuteAsync(string args, CancellationToken cancellationToken)
    {
        foreach (var actor in _engine.GetActorContexts())
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold underline]{Markup.Escape(actor.DisplayName)}[/]");

            AnsiConsole.MarkupLine("[bold]System prompt[/] (rendered):");
            WriteBlock(actor.SystemPrompt);

            AnsiConsole.MarkupLine($"[bold]Conversation buffer[/] ({actor.Messages.Count} message(s) beyond the system prompt):");
            if (actor.Messages.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]  (empty — this actor has not been used yet this round)[/]");
            }
            else
            {
                foreach (var message in actor.Messages)
                {
                    AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(message.Role.ToLowerInvariant())}:[/]");
                    WriteBlock(message.Text);
                }
            }

            AnsiConsole.MarkupLine("[bold]Per-phase prompt templates[/] sent to this actor:");
            foreach (var (label, template) in TemplatesFor(actor.PersonaToken))
            {
                AnsiConsole.MarkupLine($"  [green]{Markup.Escape(label)}[/]:");
                WriteBlock(template);
            }
        }

        AnsiConsole.WriteLine();
        return Task.FromResult(new CommandResult());
    }

    private static IEnumerable<(string Label, string Template)> TemplatesFor(string personaToken) => personaToken switch
    {
        PersonaTokens.JudgeRephraser =>
        [
            ("rephrase", DebatePrompts.RephraseTemplate),
            ("clarify follow-up", DebatePrompts.ClarifyFollowUpTemplate),
            ("rephrase clarification for Answerer", DebatePrompts.ClarifyForAnswererTemplate),
        ],
        PersonaTokens.JudgeRestater =>
        [
            ("restate", DebatePrompts.RestateTemplate),
        ],
        PersonaTokens.JudgeArbiter =>
        [
            ("verdict", DebatePrompts.VerdictTemplate),
        ],
        PersonaTokens.JudgeProfiler =>
        [
            ("profile", DebatePrompts.ProfileTemplate),
        ],
        PersonaTokens.Answerer =>
        [
            ("answer (or ask for missing info)", DebatePrompts.AnswerTemplate),
            ("answer after clarification", DebatePrompts.ClarifiedAnswerTemplate),
            ("respond to objection", DebatePrompts.RespondToObjectionTemplate),
        ],
        PersonaTokens.Critic =>
        [
            ("critique", DebatePrompts.CritiqueTemplate),
        ],
        _ => [],
    };

    private static void WriteBlock(string text)
    {
        foreach (var line in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            AnsiConsole.MarkupLine($"[grey]    {Markup.Escape(line)}[/]");
        }
    }
}

/// <summary>!personas — show roles, models, temperatures, persona files.</summary>
public sealed class PersonasCommand : ReplCommand
{
    private readonly DebateEngine _engine;

    public PersonasCommand(DebateEngine engine) => _engine = engine;

    public override string Name => "personas";
    public override string Help => "show roles, models, temperatures, and persona files";

    public override Task<CommandResult> ExecuteAsync(string args, CancellationToken cancellationToken)
    {
        var cfg = _engine.Config;
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"Loaded persona preset: [bold]{Markup.Escape(cfg.PersonaName)}[/]");
        AnsiConsole.MarkupLine($"Persona directory:     {Markup.Escape(_engine.PersonaDirectory)}");
        AnsiConsole.MarkupLine($"Profile building:      {(cfg.BuildProfile ? "on" : "off")}");

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("actor");
        table.AddColumn("model");
        table.AddColumn(new TableColumn("temp").RightAligned());
        table.AddColumn("persona file");

        foreach (var info in _engine.GetPersonaInfo())
        {
            table.AddRow(
                Markup.Escape(info.DisplayName),
                Markup.Escape(info.Model),
                $"{info.Temperature:0.00}",
                Markup.Escape(info.PersonaFile ?? "(missing)"));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        return Task.FromResult(new CommandResult());
    }
}
