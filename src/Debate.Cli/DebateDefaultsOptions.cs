namespace Debate.Cli;

/// <summary>
/// Default session settings, bound from <c>Debate:Defaults</c>. The setup wizard
/// seeds its prompts from these; a headless run uses them directly.
/// </summary>
public sealed class DebateDefaultsOptions
{
    public const string SectionName = "Debate:Defaults";

    public string Persona { get; set; } = "default";
    public float AnswererTemp { get; set; } = 0.3f;
    public float CriticTemp { get; set; } = 0.9f;
    public float JudgeTemp { get; set; } = 0.3f;
    public bool BuildProfile { get; set; } = true;

    /// <summary>Maximum debate rounds per question (the loop still ends early when the Critic is done).</summary>
    public int MaxRounds { get; set; } = Debate.Core.SessionConfig.DefaultMaxRounds;
}
