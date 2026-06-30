namespace Debate.Core;

/// <summary>The token buckets tracked per session. Mirrors the original stats view.</summary>
public enum TokenBucket
{
    Rephrase,
    Answerer,
    Critic,
    Verdict,
    Profile,
}

/// <summary>
/// Per-session counters, wall times, and token buckets. Mutated by the pipeline,
/// read for the stats view, cleared by a new session.
///
/// Token counts cover the request payloads and replies actually transmitted on
/// each call; the system-prompt and history overhead processed every turn is
/// intentionally not counted here (the per-actor context-fill view covers that).
/// </summary>
public sealed class SessionStats
{
    public int Questions { get; set; }
    public int Clarifications { get; set; }
    public int DebateRounds { get; set; }

    public double WallTimeTotal { get; set; }
    public double LastWallTimeTotal { get; set; }
    public double WallTimePostRephrase { get; set; }
    public double LastWallTimePostRephrase { get; set; }

    public long TokensRephrase { get; private set; }
    public long TokensAnswerer { get; private set; }
    public long TokensCritic { get; private set; }
    public long TokensVerdict { get; private set; }
    public long TokensProfile { get; private set; }

    public int VerdictConfidenceLow { get; private set; }
    public int VerdictConfidenceMedium { get; private set; }
    public int VerdictConfidenceHigh { get; private set; }

    public long TokensTotal =>
        TokensRephrase + TokensAnswerer + TokensCritic + TokensVerdict + TokensProfile;

    public void Add(TokenBucket bucket, int count)
    {
        switch (bucket)
        {
            case TokenBucket.Rephrase: TokensRephrase += count; break;
            case TokenBucket.Answerer: TokensAnswerer += count; break;
            case TokenBucket.Critic: TokensCritic += count; break;
            case TokenBucket.Verdict: TokensVerdict += count; break;
            case TokenBucket.Profile: TokensProfile += count; break;
        }
    }

    public void RecordConfidence(ConfidenceLabel? label)
    {
        switch (label)
        {
            case ConfidenceLabel.Low: VerdictConfidenceLow++; break;
            case ConfidenceLabel.Medium: VerdictConfidenceMedium++; break;
            case ConfidenceLabel.High: VerdictConfidenceHigh++; break;
        }
    }

    public void Reset()
    {
        Questions = 0;
        Clarifications = 0;
        DebateRounds = 0;
        WallTimeTotal = 0.0;
        LastWallTimeTotal = 0.0;
        WallTimePostRephrase = 0.0;
        LastWallTimePostRephrase = 0.0;
        TokensRephrase = 0;
        TokensAnswerer = 0;
        TokensCritic = 0;
        TokensVerdict = 0;
        TokensProfile = 0;
        VerdictConfidenceLow = 0;
        VerdictConfidenceMedium = 0;
        VerdictConfidenceHigh = 0;
    }
}
