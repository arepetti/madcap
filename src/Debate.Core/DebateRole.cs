namespace Debate.Core;

/// <summary>
/// The three LLM-backed participants in a debate. Provider implementations map
/// each role to a concrete model; the algorithm only ever refers to the role.
/// </summary>
public enum DebateRole
{
    Answerer,
    Critic,
    Judge,
}

public static class DebateRoleExtensions
{
    /// <summary>
    /// Lowercase token used for persona file lookup (e.g. "default.answerer.txt")
    /// and configuration keys. Matches the original Python role names.
    /// </summary>
    public static string ToToken(this DebateRole role) => role switch
    {
        DebateRole.Answerer => "answerer",
        DebateRole.Critic => "critic",
        DebateRole.Judge => "judge",
        _ => role.ToString().ToLowerInvariant(),
    };
}
