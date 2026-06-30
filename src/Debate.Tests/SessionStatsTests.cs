using Debate.Core;

namespace Debate.Tests;

public class SessionStatsTests
{
    [Fact]
    public void Add_accumulates_into_buckets_and_total()
    {
        var stats = new SessionStats();
        stats.Add(TokenBucket.Rephrase, 10);
        stats.Add(TokenBucket.Answerer, 20);
        stats.Add(TokenBucket.Answerer, 5);
        stats.Add(TokenBucket.Verdict, 7);

        Assert.Equal(10, stats.TokensRephrase);
        Assert.Equal(25, stats.TokensAnswerer);
        Assert.Equal(7, stats.TokensVerdict);
        Assert.Equal(42, stats.TokensTotal);
    }

    [Fact]
    public void RecordConfidence_counts_per_label_and_ignores_null()
    {
        var stats = new SessionStats();
        stats.RecordConfidence(ConfidenceLabel.High);
        stats.RecordConfidence(ConfidenceLabel.High);
        stats.RecordConfidence(ConfidenceLabel.Low);
        stats.RecordConfidence(null);

        Assert.Equal(2, stats.VerdictConfidenceHigh);
        Assert.Equal(1, stats.VerdictConfidenceLow);
        Assert.Equal(0, stats.VerdictConfidenceMedium);
    }

    [Fact]
    public void Reset_clears_everything()
    {
        var stats = new SessionStats { Questions = 3, DebateRounds = 9 };
        stats.Add(TokenBucket.Critic, 100);
        stats.RecordConfidence(ConfidenceLabel.Medium);

        stats.Reset();

        Assert.Equal(0, stats.Questions);
        Assert.Equal(0, stats.DebateRounds);
        Assert.Equal(0, stats.TokensTotal);
        Assert.Equal(0, stats.VerdictConfidenceMedium);
    }
}
