using Debate.Core;
using Debate.Tests.Support;

namespace Debate.Tests;

/// <summary>
/// The rephraser's session memory is rendered into its system prompt on every question,
/// so it competes with the debate for the context window. These cover the bounds that
/// keep it from crowding out the current question.
/// </summary>
public class SessionMemoryTests
{
    private static string Judge(string p)
    {
        if (Phase.IsRephrase(p)) return "{\"action\":\"rephrase\",\"text\":\"REPHRASED\"}";
        if (Phase.IsRestate(p)) return "{\"restatement\":\"NEUTRAL\"}";
        if (Phase.IsVerdict(p)) return "{\"answer\":\"V\",\"confidence\":\"low\",\"justification\":\"j\",\"uncertainty\":\"\"}";
        return "{}";
    }

    [Fact]
    public async Task Only_the_most_recent_exchanges_are_kept()
    {
        using var s = new Scenario(_ => "{\"answer\":\"A\"}", _ => "{\"done\":true}", Judge, buildProfile: false);

        for (int i = 0; i < DebateContext.MaxPriorExchanges + 3; i++)
        {
            await s.RunAsync($"question {i}");
        }

        var snap = s.Engine.GetStatsSnapshot();
        Assert.Equal(DebateContext.MaxPriorExchanges, snap.PriorRephrased.Count);
    }

    [Fact]
    public async Task A_stored_verdict_is_abbreviated()
    {
        var longJustification = new string('x', 2000);
        string JudgeWithLongVerdict(string p) => Phase.IsVerdict(p)
            ? $"{{\"answer\":\"V\",\"confidence\":\"low\",\"justification\":\"{longJustification}\",\"uncertainty\":\"\"}}"
            : Judge(p);

        using var s = new Scenario(
            _ => "{\"answer\":\"A\"}", _ => "{\"done\":true}", JudgeWithLongVerdict, buildProfile: false);

        await s.RunAsync("question");
        await s.RunAsync("follow-up");

        // The rephraser sees the stored (abbreviated) verdict, not the full 2000 chars.
        var rephraserPrompt = s.Engine.GetActorContexts()
            .Single(a => a.PersonaToken == PersonaTokens.JudgeRephraser)
            .SystemPrompt;
        Assert.Contains("EXCHANGES:", rephraserPrompt);
        Assert.DoesNotContain(longJustification, rephraserPrompt);
    }

    [Fact]
    public async Task An_unparseable_verdict_reaches_the_user_without_its_reasoning()
    {
        // Both the verdict call and its re-ask return prose wrapped in a thinking loop.
        static string BrokenJudge(string p)
        {
            if (Phase.IsRephrase(p)) return "{\"action\":\"rephrase\",\"text\":\"REPHRASED\"}";
            if (Phase.IsRestate(p)) return "{\"restatement\":\"NEUTRAL\"}";
            if (Phase.IsProfile(p)) return "{\"tendency\":null}";
            return "<think>SECRETLOOP SECRETLOOP</think>The recommendation stands.";
        }

        using var s = new Scenario(_ => "{\"answer\":\"A\"}", _ => "{\"done\":true}", BrokenJudge);

        await s.RunAsync("question");

        var verdict = Assert.Single(s.Observer.Verdict).Text;
        Assert.Contains("The recommendation stands.", verdict);
        Assert.DoesNotContain("SECRETLOOP", verdict);
    }
}
