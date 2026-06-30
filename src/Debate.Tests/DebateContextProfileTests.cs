using Debate.Core;
using Debate.Tests.Support;

namespace Debate.Tests;

public class DebateContextProfileTests
{
    private static DebateContext NewContext()
    {
        using var personas = new TempPersonas();
        return new DebateContext(TestFactory.Config(), TestFactory.InertProvider(), personas.Library);
    }

    [Fact]
    public void New_note_is_a_hidden_candidate_until_threshold()
    {
        var ctx = NewContext();

        var update = ctx.RecordProfileNote("tends to ignore failure modes in distributed systems");

        Assert.Equal(ProfileUpdateKind.NewCandidate, update.Kind);
        Assert.Empty(ctx.ActiveProfile());
        Assert.Single(ctx.PendingProfile());
    }

    [Fact]
    public void Similar_note_increments_and_surfaces_at_threshold()
    {
        var ctx = NewContext();
        ctx.RecordProfileNote("tends to ignore failure modes in distributed systems");

        var update = ctx.RecordProfileNote("tends to ignore distributed-system failure modes");

        Assert.Equal(ProfileUpdateKind.BecameActive, update.Kind);
        Assert.Equal(Profile.MinCountToSurface, update.Count);
        Assert.Single(ctx.ActiveProfile());
    }

    [Fact]
    public void Dissimilar_notes_stay_separate_candidates()
    {
        var ctx = NewContext();
        ctx.RecordProfileNote("tends to ignore failure modes in distributed systems");
        ctx.RecordProfileNote("assumes unlimited compute and budget everywhere");

        Assert.Equal(2, ctx.PendingProfile().Count);
        Assert.Empty(ctx.ActiveProfile());
    }

    [Fact]
    public async Task ClearSession_wipes_profile_and_history()
    {
        var ctx = NewContext();
        ctx.RecordProfileNote("note one");
        ctx.PriorRephrased.Add("Q1");
        ctx.PriorVerdicts.Add("V1");
        ctx.Stats.Questions = 5;

        await ctx.ClearSessionAsync();

        Assert.Empty(ctx.ProfileEntries);
        Assert.Empty(ctx.PriorRephrased);
        Assert.Empty(ctx.PriorVerdicts);
        Assert.Equal(0, ctx.Stats.Questions);
    }
}
