using Debate.Tests.Support;

namespace Debate.Tests;

public class PipelineFailureTests
{
    private static string MinimalJudge(string p)
    {
        if (Phase.IsRephrase(p)) return "{\"action\":\"rephrase\",\"text\":\"REPHRASED\"}";
        if (Phase.IsRestate(p)) return "{\"restatement\":\"NEUTRAL\"}";
        if (Phase.IsVerdict(p)) return "{\"answer\":\"V\",\"confidence\":\"low\",\"justification\":\"j\",\"uncertainty\":\"\"}";
        return "{}";
    }

    [Fact]
    public async Task Unparseable_reply_triggers_one_reask_then_succeeds()
    {
        int calls = 0;
        Func<string, string> answerer = p =>
        {
            calls++;
            return calls == 1 ? "totally not json" : "{\"answer\":\"RECOVERED\"}";
        };

        using var s = new Scenario(answerer, _ => "{\"done\":true}", MinimalJudge);

        await s.RunAsync("USERQUESTION");

        Assert.Contains(s.Observer.Warnings, w => w.Contains("not valid JSON") && w.Contains("re-asking"));
        Assert.Contains("RECOVERED", s.Observer.Answerer);
        // The second answerer call was the re-ask.
        Assert.Contains(s.Provider.Answerer.Calls, c => Phase.IsReask(c.LastUserMessage));
    }

    [Fact]
    public async Task Empty_answer_after_reask_aborts_without_a_verdict()
    {
        // The Answerer always returns content-free JSON: empty answer, not a clarification.
        using var s = new Scenario(_ => "{}", _ => "{\"done\":true}", MinimalJudge);

        await s.RunAsync("USERQUESTION");

        Assert.Empty(s.Observer.Verdict);
        Assert.Contains(s.Observer.Warnings, w => w.Contains("no usable answer"));
        Assert.Empty(s.Provider.Critic.Calls);
        // The Answerer was asked twice (initial + one re-ask) before aborting.
        Assert.Equal(2, s.Provider.Answerer.Calls.Count);
    }
}
