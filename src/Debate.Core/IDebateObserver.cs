namespace Debate.Core;

/// <summary>
/// Sink for everything the debate wants to surface to a user. Keeps the
/// algorithm free of any console/printing concern so the same core can drive a
/// CLI, a GUI, or a web frontend. A host implements this to render output.
///
/// All members are synchronous and must not throw; a no-op implementation is a
/// valid "headless" host.
/// </summary>
public interface IDebateObserver
{
    /// <summary>The Judge's neutral rephrasing of the user's question (rephrase phase).</summary>
    void OnRephrased(string question);

    /// <summary>
    /// A clarifying question the Judge wants to ask the user, surfaced before the
    /// clarification source prompts for the answer.
    /// </summary>
    void OnClarify(string question);

    /// <summary>An Answerer turn.</summary>
    void OnAnswerer(string text);

    /// <summary>The Judge's neutral restatement of an Answerer turn (what the Critic sees).</summary>
    void OnRestatement(string text);

    /// <summary>A Critic objection.</summary>
    void OnCritic(string text);

    /// <summary>The Judge's final verdict and its parsed confidence (if any).</summary>
    void OnVerdict(string text, ConfidenceLabel? confidence);

    /// <summary>A non-fatal warning (e.g. a skipped clarification or a rejected profile note).</summary>
    void OnWarning(string text);

    /// <summary>An informational message.</summary>
    void OnInfo(string text);

    /// <summary>
    /// A transient "in progress" status emitted immediately before a potentially
    /// slow model call (e.g. "Asking the Judge for a verdict..."), so a user can
    /// see which step is running while waiting. Hosts may render it dimly or as a
    /// spinner; a no-op is fine.
    /// </summary>
    void OnStatus(string text);

    /// <summary>A change to the Answerer profile.</summary>
    void OnProfileUpdate(ProfileUpdate update);
}
