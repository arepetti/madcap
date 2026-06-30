namespace Debate.Core;

/// <summary>What happened to the Answerer profile when a note was recorded.</summary>
public enum ProfileUpdateKind
{
    /// <summary>An existing note's observation count went up but it is still hidden.</summary>
    Incremented,

    /// <summary>An existing note crossed the surface threshold and is now visible to the Critic.</summary>
    BecameActive,

    /// <summary>A brand-new candidate note was inserted (hidden until observed again).</summary>
    NewCandidate,
}

/// <summary>
/// Result of merging a profile note into the session profile. Emitted to the
/// observer so a host can surface it; carries no behaviour.
/// </summary>
public sealed record ProfileUpdate(ProfileUpdateKind Kind, string Text, int Count);
