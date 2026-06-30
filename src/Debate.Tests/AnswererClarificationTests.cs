using Debate.Core;
using Debate.Tests.Support;

namespace Debate.Tests;

public class AnswererClarificationTests
{
    private static string Judge(string p)
    {
        if (Phase.IsRephrase(p)) return "{\"action\":\"rephrase\",\"text\":\"REPHRASED\"}";
        if (Phase.IsClarifyForAnswerer(p)) return "{\"action\":\"rephrase\",\"text\":\"CADENCEWEEKLY\"}";
        if (Phase.IsRestate(p)) return "{\"restatement\":\"NEUTRAL\"}";
        if (Phase.IsVerdict(p)) return "{\"answer\":\"V\",\"confidence\":\"low\",\"justification\":\"j\",\"uncertainty\":\"\"}";
        if (Phase.IsProfile(p)) return "{\"tendency\":null}";
        return "{}";
    }

    private static Func<string, string> AnswererThatAsksOnce()
    {
        return p =>
        {
            if (Phase.IsClarifiedAnswer(p)) return "{\"answer\":\"CLARIFIEDANSWER\"}";
            if (Phase.IsAnswer(p)) return "{\"clarification\":\"WHATCADENCE\"}";
            return "{\"answer\":\"X\"}";
        };
    }

    [Fact]
    public async Task Answerer_clarification_is_routed_to_user_and_rephrased_back()
    {
        using var s = new Scenario(
            AnswererThatAsksOnce(), _ => "{\"done\":true}", Judge,
            clarificationReplies: "weekly raw reply");

        await s.RunAsync("USERQUESTION");

        // The Answerer's question was put to the user.
        Assert.Contains("WHATCADENCE", s.Observer.Clarify);
        Assert.Contains("WHATCADENCE", s.Clarifications.AskedQuestions);
        Assert.Equal(1, s.Engine.GetStatsSnapshot().Stats.Clarifications);

        // The user's reply was rephrased and fed back to the Answerer.
        var clarifiedCall = s.Provider.Answerer.Calls.Single(c => Phase.IsClarifiedAnswer(c.LastUserMessage));
        Assert.Contains("CADENCEWEEKLY", clarifiedCall.LastUserMessage);

        // The clarification question was never shown as an answer; the real answer was.
        Assert.Equal("CLARIFIEDANSWER", Assert.Single(s.Observer.Answerer));
    }

    [Fact]
    public async Task Clarification_round_does_not_invoke_the_critic()
    {
        using var s = new Scenario(
            AnswererThatAsksOnce(), _ => "{\"done\":true}", Judge,
            clarificationReplies: "weekly");

        await s.RunAsync("USERQUESTION");

        // Only the single debate critique call — the clarification turn skipped the Critic.
        Assert.Single(s.Provider.Critic.Calls);
    }

    [Fact]
    public async Task Skipped_clarification_aborts_the_question()
    {
        using var s = new Scenario(
            AnswererThatAsksOnce(), _ => "{\"done\":true}", Judge,
            clarificationReplies: ""); // empty == skip

        await s.RunAsync("USERQUESTION");

        Assert.Empty(s.Observer.Verdict);
        Assert.Contains(s.Observer.Warnings, w => w.Contains("skipped"));
        Assert.Empty(s.Engine.GetStatsSnapshot().PriorRephrased);
        Assert.Empty(s.Provider.Critic.Calls);
    }
}
