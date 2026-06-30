namespace Debate.Core;

/// <summary>
/// The persona-file tokens the app loads. A persona file is named
/// "&lt;preset&gt;.&lt;token&gt;.txt". The Answerer and Critic map 1:1 to their role,
/// but the Judge is split into four single-job contexts (rephraser, restater,
/// arbiter, profiler) so each has its own tight system prompt and its own
/// conversation buffer — see <see cref="DebateContext"/> and design.md.
/// </summary>
public static class PersonaTokens
{
    public const string Answerer = "answerer";
    public const string Critic = "critic";
    public const string JudgeRephraser = "judge-rephraser";
    public const string JudgeRestater = "judge-restater";
    public const string JudgeArbiter = "judge-arbiter";
    public const string JudgeProfiler = "judge-profiler";

    /// <summary>Every persona token a preset must provide (used for setup validation).</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Answerer,
        Critic,
        JudgeRephraser,
        JudgeRestater,
        JudgeArbiter,
        JudgeProfiler,
    ];
}
