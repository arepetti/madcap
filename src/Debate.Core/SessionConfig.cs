namespace Debate.Core;

/// <summary>
/// Per-session settings gathered once at startup (by a setup wizard, config, or
/// any host). Immutable for the life of a session.
/// </summary>
public sealed class SessionConfig
{
    /// <summary>Fallback round count when none is configured.</summary>
    public const int DefaultMaxRounds = 3;

    public SessionConfig(
        string personaName,
        float answererTemperature,
        float criticTemperature,
        float judgeTemperature,
        bool buildProfile,
        int maxRounds = DefaultMaxRounds)
    {
        PersonaName = personaName;
        AnswererTemperature = answererTemperature;
        CriticTemperature = criticTemperature;
        JudgeTemperature = judgeTemperature;
        BuildProfile = buildProfile;
        MaxRounds = maxRounds > 0 ? maxRounds : DefaultMaxRounds;
    }

    /// <summary>Persona preset name (resolves persona files like "&lt;name&gt;.&lt;role&gt;.txt").</summary>
    public string PersonaName { get; }

    public float AnswererTemperature { get; }
    public float CriticTemperature { get; }
    public float JudgeTemperature { get; }

    /// <summary>Whether the Judge extracts a cross-round Answerer profile note after each question.</summary>
    public bool BuildProfile { get; }

    /// <summary>
    /// Maximum number of debate rounds (Answerer turn → Judge restatement → Critic
    /// objection → Answerer rebuttal). The loop still ends early when the Critic is done.
    /// </summary>
    public int MaxRounds { get; }

    public float TemperatureFor(DebateRole role) => role switch
    {
        DebateRole.Answerer => AnswererTemperature,
        DebateRole.Critic => CriticTemperature,
        DebateRole.Judge => JudgeTemperature,
        _ => 0.3f,
    };
}
