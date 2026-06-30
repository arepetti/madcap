using Debate.Core;
using Spectre.Console;

namespace Debate.Cli;

/// <summary>
/// Renders debate output to the terminal with Spectre.Console. This is the only
/// place that knows about colors and the console; the algorithm in Debate.Core
/// just calls <see cref="IDebateObserver"/>.
///
/// Every write goes through a <see cref="ConsoleOutputGate"/> so background output
/// (e.g. model-loading logs) is queued while the foreground is reading user input and
/// flushed afterwards, instead of landing in the middle of a prompt.
/// </summary>
public sealed class SpectreDebateObserver : IDebateObserver
{
    private readonly ConsoleOutputGate _gate;

    public SpectreDebateObserver(ConsoleOutputGate gate) => _gate = gate;

    public void OnRephrased(string question) => _gate.Write(() =>
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold magenta]REPHRASED QUESTION:[/]");
        WriteBody(question, "magenta");
        AnsiConsole.WriteLine();
    });

    public void OnClarify(string question) => _gate.Write(() =>
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold magenta]CLARIFYING QUESTION:[/]");
        WriteBody(question, "magenta");
    });

    public void OnAnswerer(string text) => Section("--- Answerer ---", text, "dodgerblue1");

    public void OnRestatement(string text) => Section("--- Judge restates ---", text, "magenta");

    public void OnCritic(string text) => Section("--- Critic ---", text, "blue");

    public void OnVerdict(string text, ConfidenceLabel? confidence) => _gate.Write(() =>
    {
        AnsiConsole.MarkupLine("[bold white]--- Final Answer ---[/]");
        WriteBody(text, "white");
        if (confidence is not null)
        {
            AnsiConsole.MarkupLine($"[grey](parsed confidence: {confidence.ToString()!.ToLowerInvariant()})[/]");
        }

        AnsiConsole.WriteLine();
    });

    public void OnWarning(string text) => _gate.Write(() =>
        AnsiConsole.MarkupLine($"[yellow][[warning]] {Markup.Escape(text)}[/]"));

    public void OnInfo(string text) => _gate.Write(() => AnsiConsole.WriteLine(text));

    public void OnStatus(string text) => _gate.Write(() =>
        AnsiConsole.MarkupLine($"[grey italic][[{DateTime.Now:HH:mm:ss}]] {Markup.Escape(text)}[/]"));

    public void OnProfileUpdate(ProfileUpdate update) => _gate.Write(() =>
    {
        var message = update.Kind switch
        {
            ProfileUpdateKind.BecameActive =>
                $"[profile +1, now active] {update.Text}",
            ProfileUpdateKind.Incremented =>
                $"[profile +1, count={update.Count}] {update.Text}",
            ProfileUpdateKind.NewCandidate =>
                $"[profile new candidate, count=1, hidden until count>={Debate.Core.Profile.MinCountToSurface}] {update.Text}",
            _ => update.Text,
        };

        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(message)}[/]");
    });

    private void Section(string header, string body, string color, bool bold = true) => _gate.Write(() =>
    {
        var headerMarkup = bold ? $"[bold {color}]{header}[/]" : $"[{color}]{header}[/]";
        AnsiConsole.MarkupLine(headerMarkup);
        WriteBody(body, color);
        AnsiConsole.WriteLine();
    });

    private static void WriteBody(string body, string color) =>
        AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(body)}[/]");
}
