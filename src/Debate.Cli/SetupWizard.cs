using Debate.Core;
using Spectre.Console;

namespace Debate.Cli;

/// <summary>
/// Interactive setup, asking for persona, per-role temperatures, and whether to
/// build an Answerer profile. Defaults come from configuration. Ports
/// <c>setup.py</c> to Spectre.Console prompts. Returns a <see cref="SessionConfig"/>.
/// </summary>
public sealed class SetupWizard
{
    private readonly PersonaLibrary _personas;
    private readonly DebateDefaultsOptions _defaults;
    private readonly ConsoleOutputGate _gate;

    public SetupWizard(PersonaLibrary personas, DebateDefaultsOptions defaults, ConsoleOutputGate gate)
    {
        _personas = personas;
        _defaults = defaults;
        _gate = gate;
    }

    /// <summary>Build a session config from defaults without prompting (headless / --no-wizard).</summary>
    public SessionConfig FromDefaults(string? personaOverride = null) => new(
        personaOverride ?? _defaults.Persona,
        _defaults.AnswererTemp,
        _defaults.CriticTemp,
        _defaults.JudgeTemp,
        _defaults.BuildProfile,
        _defaults.MaxRounds);

    public SessionConfig Run(string? personaOverride = null)
    {
        // Hold background output (model loading) until setup is done, so warm-up logs
        // don't interrupt the interactive prompts; they flush right after.
        using var _ = _gate.Suspend();

        AnsiConsole.MarkupLine("[bold]=== MADJURY Setup ===[/]");
        AnsiConsole.WriteLine();

        var persona = personaOverride ?? PromptPersona();

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Set per-role temperatures (press Enter for default):");
        var answerer = PromptTemperature("Answerer", _defaults.AnswererTemp);
        var critic = PromptTemperature("Critic", _defaults.CriticTemp);
        var judge = PromptTemperature("Judge", _defaults.JudgeTemp);
        AnsiConsole.MarkupLine($"Using: Answerer={answerer}, Critic={critic}, Judge={judge}");
        AnsiConsole.WriteLine();

        var buildProfile = AnsiConsole.Confirm(
            "Should the Judge build an Answerer profile across rounds?",
            _defaults.BuildProfile);
        AnsiConsole.MarkupLine($"Profile building: {(buildProfile ? "on" : "off")}");
        AnsiConsole.WriteLine();

        var maxRounds = AnsiConsole.Prompt(
            new TextPrompt<int>("Maximum debate rounds:")
                .DefaultValue(_defaults.MaxRounds)
                .ShowDefaultValue()
                .Validate(v => v is >= 1 and <= 10
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]out of range (1-10)[/]")));
        AnsiConsole.MarkupLine($"Max rounds: {maxRounds}");
        AnsiConsole.WriteLine();

        return new SessionConfig(persona, answerer, critic, judge, buildProfile, maxRounds);
    }

    private string PromptPersona()
    {
        var available = _personas.ListPersonaNames();
        if (available.Count > 0)
        {
            AnsiConsole.MarkupLine($"Available personas: {Markup.Escape(string.Join(", ", available))}");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow][[warning]] No persona files found. Using 'default' (will fail unless created).[/]");
        }

        var name = AnsiConsole.Prompt(
            new TextPrompt<string>("Persona preset:")
                .DefaultValue(_defaults.Persona)
                .ShowDefaultValue());

        var missing = new List<string>();
        foreach (var token in PersonaTokens.All)
        {
            if (_personas.ResolvePersonaPath(name, token) is null)
            {
                missing.Add(token);
            }
        }

        if (missing.Count > 0)
        {
            AnsiConsole.MarkupLine(
                $"[yellow][[warning]] persona '{Markup.Escape(name)}' is missing files for: " +
                $"{Markup.Escape(string.Join(", ", missing))} — and 'default.<token>.txt' fallbacks are not present either.[/]");
        }

        return name;
    }

    private static float PromptTemperature(string role, float defaultValue)
    {
        return AnsiConsole.Prompt(
            new TextPrompt<float>($"  {role} temperature:")
                .DefaultValue(defaultValue)
                .ShowDefaultValue()
                .Validate(v => v is >= 0.0f and <= 2.0f
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]out of range (0.0-2.0)[/]")));
    }
}
