using System.Diagnostics;
using System.Globalization;
using System.Text;
using Debate.Core;
using Spectre.Console;

namespace Debate.Cli.Commands;

/// <summary>!stats — show session/per-actor stats; "export [path]" appends to CSV.</summary>
public sealed class StatsCommand : ReplCommand
{
    private readonly DebateEngine _engine;
    private readonly IModelProvider _provider;

    public StatsCommand(DebateEngine engine, IModelProvider provider)
    {
        _engine = engine;
        _provider = provider;
    }

    public override string Name => "stats";
    public override string Help => "show session/per-actor stats; 'export [path]' appends to CSV";

    // Single source of truth for the CSV header and per-row value order.
    private static readonly string[] ExportColumns =
    {
        "role", "questions", "clarifications", "debate_rounds",
        "wall_time_total", "last_wall_time_total",
        "wall_time_post_rephrase", "last_wall_time_post_rephrase",
        "tokens_total", "tokens_rephrase", "tokens_answerer", "tokens_critic",
        "tokens_verdict", "tokens_profile",
        "verdict_confidence_low", "verdict_confidence_medium", "verdict_confidence_high",
    };

    public override Task<CommandResult> ExecuteAsync(string args, CancellationToken cancellationToken)
    {
        args = (args ?? string.Empty).Trim();
        if (args.StartsWith("export", StringComparison.Ordinal))
        {
            var rest = args["export".Length..].Trim();
            return Task.FromResult(Export(rest));
        }

        Show();
        return Task.FromResult(new CommandResult());
    }

    private void Show()
    {
        var snap = _engine.GetStatsSnapshot();

        ShowSummary(snap);
        ShowSessionStats(snap.Stats);
        ShowTokens(snap.Stats);
        ShowConfidence(snap.Stats);
        ShowActorTable(snap);
        ShowProcesses();
        ShowRephrasedQuestions(snap);
        ShowProfileEntries(snap);

        AnsiConsole.WriteLine();
    }

    private static void ShowSummary(StatsSnapshot snap)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"Token estimation: {Markup.Escape(snap.TokenMethod)}");
        AnsiConsole.MarkupLine($"Effective context size: {snap.EffectiveContextSize}");
        AnsiConsole.MarkupLine($"Profile building: {(snap.BuildProfile ? "on" : "off")}");
        AnsiConsole.MarkupLine($"Prior rephrased questions: {snap.PriorRephrased.Count}");
        AnsiConsole.MarkupLine($"Profile: {snap.ActiveProfile.Count} active, {snap.PendingProfile.Count} pending");
    }

    private static void ShowSessionStats(SessionStats s)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Session stats:[/]");
        AnsiConsole.MarkupLine($"  Questions:               {s.Questions}");
        AnsiConsole.MarkupLine($"  Clarifications:          {s.Clarifications}");
        AnsiConsole.MarkupLine($"  Debate rounds:           {s.DebateRounds}");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  Wall time (seconds):");
        AnsiConsole.MarkupLine(
            $"    question  -> answer:   total {s.WallTimeTotal,7:F1}   last {s.LastWallTimeTotal,7:F1}");
        AnsiConsole.MarkupLine(
            $"    rephrased -> answer:   total {s.WallTimePostRephrase,7:F1}   last {s.LastWallTimePostRephrase,7:F1}");
    }

    private static void ShowTokens(SessionStats s)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  Tokens:");
        WriteTokenRow("total:", s.TokensTotal);
        WriteTokenRow("rephrase question:", s.TokensRephrase);
        WriteTokenRow("answerer turns:", s.TokensAnswerer);
        WriteTokenRow("critic (restate+critic):", s.TokensCritic);
        WriteTokenRow("judge verdict:", s.TokensVerdict);
        WriteTokenRow("profile (phase 3 + render):", s.TokensProfile);
    }

    private static void ShowConfidence(SessionStats s)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  Verdict confidence (count of verdicts at each label):");
        AnsiConsole.MarkupLine(
            $"    low {s.VerdictConfidenceLow,5}   medium {s.VerdictConfidenceMedium,5}   high {s.VerdictConfidenceHigh,5}");
    }

    private static void ShowActorTable(StatsSnapshot snap)
    {
        AnsiConsole.WriteLine();

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("agent");
        table.AddColumn("model");
        table.AddColumn(new TableColumn("tokens").RightAligned());
        table.AddColumn(new TableColumn("budget").RightAligned());
        table.AddColumn(new TableColumn("fill").RightAligned());

        foreach (var actor in snap.Actors)
        {
            if (!actor.Built)
            {
                table.AddRow(
                    Markup.Escape(actor.DisplayName),
                    Markup.Escape(actor.Model),
                    "—",
                    snap.EffectiveContextSize.ToString(CultureInfo.InvariantCulture),
                    "(unbuilt)");
                continue;
            }

            double pct = 100.0 * actor.Tokens / snap.EffectiveContextSize;
            var fill = pct > 80 ? $"[red]{pct:F1}% !![/]" : $"{pct:F1}%";
            table.AddRow(
                Markup.Escape(actor.DisplayName),
                Markup.Escape(actor.Model),
                actor.Tokens.ToString(CultureInfo.InvariantCulture),
                snap.EffectiveContextSize.ToString(CultureInfo.InvariantCulture),
                fill);
        }

        AnsiConsole.Write(table);
    }

    private static void ShowRephrasedQuestions(StatsSnapshot snap)
    {
        if (snap.PriorRephrased.Count == 0)
        {
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Rephrased questions so far:[/]");
        for (int i = 0; i < snap.PriorRephrased.Count; i++)
        {
            var q = snap.PriorRephrased[i];
            var preview = q.Length <= 80 ? q : q[..77] + "...";
            AnsiConsole.MarkupLine($"  {i + 1}. {Markup.Escape(preview)}");
        }
    }

    private static void ShowProfileEntries(StatsSnapshot snap)
    {
        if (snap.ActiveProfile.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold]Active profile (visible to Critic, count >= {Debate.Core.Profile.MinCountToSurface}):[/]");
            foreach (var entry in snap.ActiveProfile)
            {
                AnsiConsole.MarkupLine($"  [[{entry.Count}x]] {Markup.Escape(entry.Text)}");
            }
        }

        if (snap.PendingProfile.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Pending profile (hidden from Critic, observed only once):[/]");
            foreach (var entry in snap.PendingProfile)
            {
                AnsiConsole.MarkupLine($"  [[{entry.Count}x]] {Markup.Escape(entry.Text)}");
            }
        }
    }

    private void ShowProcesses()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Processes:[/]");

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn(new TableColumn("PID").RightAligned());
        table.AddColumn("role");
        table.AddColumn("purpose");
        table.AddColumn("state");

        using var self = Process.GetCurrentProcess();
        table.AddRow(
            self.Id.ToString(CultureInfo.InvariantCulture),
            "parent",
            $"{Markup.Escape(self.ProcessName)} (debate CLI / orchestrator)",
            "[green]running[/]");

        AddChildProcessRows(table);
        AnsiConsole.Write(table);
    }

    private void AddChildProcessRows(Table table)
    {
        if (_provider is not IBackendDiagnostics diagnostics)
        {
            table.AddRow("—", "child", "[grey]this provider runs no local model processes[/]", "—");
            return;
        }

        var children = diagnostics.TryDescribeProcesses();
        if (children is null)
        {
            table.AddRow("—", "child", "[grey]busy loading a model; process list unavailable[/]", "—");
            return;
        }

        if (children.Count == 0)
        {
            table.AddRow("—", "child", "[grey]no model host processes resident right now[/]", "—");
            return;
        }

        foreach (var child in children)
        {
            var roles = child.Roles.Count > 0
                ? string.Join(", ", child.Roles)
                : "(unused)";
            table.AddRow(
                child.Pid >= 0 ? child.Pid.ToString(CultureInfo.InvariantCulture) : "—",
                Markup.Escape(roles),
                $"model host: {Markup.Escape(child.Label)}",
                child.Running ? "[green]running[/]" : "[red]exited[/]");
        }
    }

    private static void WriteTokenRow(string label, long value) =>
        AnsiConsole.MarkupLine($"    {label,-29}{value,8:N0}");

    private CommandResult Export(string pathArg)
    {
        var path = ResolveExportPath(pathArg);
        if (path is null)
        {
            return new CommandResult();
        }

        var role = AnsiConsole.Prompt(
            new TextPrompt<string>("    role label:")
                .DefaultValue(_engine.Config.PersonaName)
                .ShowDefaultValue());

        var row = BuildExportRow(role, _engine.GetStatsSnapshot().Stats);

        try
        {
            bool needsHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
            using var writer = new StreamWriter(path, append: true, Encoding.UTF8);
            if (needsHeader)
            {
                writer.WriteLine(ToCsvLine(ExportColumns));
            }

            writer.WriteLine(ToCsvLine(row));
        }
        catch (IOException e)
        {
            AnsiConsole.MarkupLine($"[red][[error]] could not write {Markup.Escape(path)}: {Markup.Escape(e.Message)}[/]");
            return new CommandResult();
        }

        AnsiConsole.MarkupLine($"[grey][[stats appended to {Markup.Escape(path)} as role='{Markup.Escape(role)}']][/]");
        return new CommandResult();
    }

    /// <summary>
    /// Resolves the CSV target path: prompts when none was supplied, expands environment
    /// variables and a leading "~". Returns null when the user cancels by giving no path.
    /// </summary>
    private static string? ResolveExportPath(string pathArg)
    {
        var path = pathArg;
        if (string.IsNullOrWhiteSpace(path))
        {
            AnsiConsole.Markup("    csv path [grey]>>>[/] ");
            path = (Console.ReadLine() ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                AnsiConsole.MarkupLine("[yellow][[warning]] export cancelled (no path)[/]");
                return null;
            }
        }

        path = Environment.ExpandEnvironmentVariables(path);
        if (path.StartsWith("~", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = home + path[1..];
        }

        return path;
    }

    // Builds one CSV row aligned with <see cref="ExportColumns"/>.
    private static string[] BuildExportRow(string role, SessionStats s)
    {
        var inv = CultureInfo.InvariantCulture;
        return new[]
        {
            role,
            s.Questions.ToString(inv),
            s.Clarifications.ToString(inv),
            s.DebateRounds.ToString(inv),
            s.WallTimeTotal.ToString("F1", inv),
            s.LastWallTimeTotal.ToString("F1", inv),
            s.WallTimePostRephrase.ToString("F1", inv),
            s.LastWallTimePostRephrase.ToString("F1", inv),
            s.TokensTotal.ToString(inv),
            s.TokensRephrase.ToString(inv),
            s.TokensAnswerer.ToString(inv),
            s.TokensCritic.ToString(inv),
            s.TokensVerdict.ToString(inv),
            s.TokensProfile.ToString(inv),
            s.VerdictConfidenceLow.ToString(inv),
            s.VerdictConfidenceMedium.ToString(inv),
            s.VerdictConfidenceHigh.ToString(inv),
        };
    }

    private static string ToCsvLine(IReadOnlyList<string> fields)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            var field = fields[i];
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            {
                sb.Append('"').Append(field.Replace("\"", "\"\"")).Append('"');
            }
            else
            {
                sb.Append(field);
            }
        }

        return sb.ToString();
    }
}
