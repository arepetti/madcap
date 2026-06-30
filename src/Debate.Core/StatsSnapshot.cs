namespace Debate.Core;

/// <summary>A profile note with its observation count, for display.</summary>
public sealed record ProfileEntryView(string Text, int Count);

/// <summary>Per-actor context-window usage at a point in time.</summary>
public sealed record ActorContextInfo(
    string DisplayName,
    DebateRole Role,
    string Model,
    float Temperature,
    int Tokens,
    bool Built);

/// <summary>
/// An immutable snapshot of session metrics and state for a stats view. Carries
/// everything a host needs to render; it has no behaviour and no console ties.
/// </summary>
public sealed record StatsSnapshot(
    string TokenMethod,
    int EffectiveContextSize,
    bool BuildProfile,
    SessionStats Stats,
    IReadOnlyList<string> PriorRephrased,
    IReadOnlyList<ProfileEntryView> ActiveProfile,
    IReadOnlyList<ProfileEntryView> PendingProfile,
    IReadOnlyList<ActorContextInfo> Actors);

/// <summary>Actor -> model/temperature/persona-file mapping for the personas view.</summary>
public sealed record PersonaRoleInfo(
    string DisplayName,
    DebateRole Role,
    string Model,
    float Temperature,
    string? PersonaFile);

/// <summary>One message in an actor's conversation buffer, for the <c>!context</c> view.</summary>
public sealed record ActorMessageView(string Role, string Text);

/// <summary>
/// A snapshot of exactly what an actor receives: its rendered system prompt and
/// its current conversation buffer (excluding the system message). Used by the
/// <c>!context</c> command.
/// </summary>
public sealed record ActorContextView(
    DebateRole Role,
    string PersonaToken,
    string DisplayName,
    string SystemPrompt,
    IReadOnlyList<ActorMessageView> Messages);
