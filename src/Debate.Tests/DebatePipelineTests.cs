using Debate.Core;
using Debate.Tests.Support;

namespace Debate.Tests;

/// <summary>
/// End-to-end pipeline behaviour with scripted models. These also serve as executable
/// checks of the design.md invariants (channel constraint, per-context isolation,
/// configurable rounds, Answerer-initiated clarification).
/// </summary>
public class DebatePipelineTests
{
    // A judge router for the canonical one-objection debate, with distinctive markers
    // so tests can prove who saw what.
    private static string Judge(string p)
    {
        if (Phase.IsRephrase(p)) return "{\"action\":\"rephrase\",\"text\":\"REPHRASED\"}";
        if (Phase.IsRestate(p))
            return p.Contains("RAWREBUTTAL")
                ? "{\"restatement\":\"NEUTRALFACTS2\"}"
                : "{\"restatement\":\"NEUTRALFACTS\"}";
        if (Phase.IsVerdict(p))
            return "{\"answer\":\"VERDICT\",\"confidence\":\"medium\",\"justification\":\"because\",\"uncertainty\":\"\"}";
        if (Phase.IsProfile(p)) return "{\"tendency\":\"might tend to TENDENCY here\"}";
        return "{}";
    }

    private static Func<string, string> CriticObjectingThenDone()
    {
        int calls = 0;
        return _ =>
        {
            calls++;
            return calls == 1
                ? "{\"done\":false,\"objection\":\"OBJECTION1\"}"
                : "{\"done\":true}";
        };
    }

    private static string Answerer(string p)
    {
        if (Phase.IsRespondToObjection(p)) return "{\"answer\":\"RAWREBUTTAL\"}";
        return "{\"answer\":\"RAWANSWER\"}";
    }

    [Fact]
    public async Task Happy_path_surfaces_each_phase_to_the_observer()
    {
        using var s = new Scenario(Answerer, CriticObjectingThenDone(), Judge);

        await s.RunAsync("USERQUESTION");

        Assert.Equal("REPHRASED", Assert.Single(s.Observer.Rephrased));
        Assert.Contains("RAWANSWER", s.Observer.Answerer);
        Assert.Contains("OBJECTION1", s.Observer.Critic);
        Assert.Contains(s.Observer.Restatement, r => r.Contains("NEUTRALFACTS"));
        var verdict = Assert.Single(s.Observer.Verdict);
        Assert.Equal(ConfidenceLabel.Medium, verdict.Confidence);
        Assert.Contains("VERDICT", verdict.Text);
    }

    [Fact]
    public async Task Records_rephrased_question_and_verdict_for_followups()
    {
        using var s = new Scenario(Answerer, CriticObjectingThenDone(), Judge);

        await s.RunAsync("USERQUESTION");

        var snap = s.Engine.GetStatsSnapshot();
        Assert.Equal("REPHRASED", Assert.Single(snap.PriorRephrased));
        Assert.Equal(1, snap.Stats.Questions);
    }

    [Fact]
    public async Task Channel_constraint_only_the_restater_sees_raw_answerer_text()
    {
        using var s = new Scenario(Answerer, CriticObjectingThenDone(), Judge);

        await s.RunAsync("USERQUESTION");

        // The restater is the one place raw Answerer text appears.
        Assert.Contains("RAWANSWER", s.ActorBuffer(PersonaTokens.JudgeRestater));
        Assert.Contains("RAWREBUTTAL", s.ActorBuffer(PersonaTokens.JudgeRestater));

        // The Critic only ever sees the neutral restatement, never the raw answer.
        var critic = s.ActorBuffer(PersonaTokens.Critic);
        Assert.Contains("NEUTRALFACTS", critic);
        Assert.DoesNotContain("RAWANSWER", critic);

        // The arbiter rules on rephrased form only.
        var arbiter = s.ActorBuffer(PersonaTokens.JudgeArbiter);
        Assert.Contains("NEUTRALFACTS", arbiter);
        Assert.Contains("OBJECTION1", arbiter);
        Assert.DoesNotContain("RAWANSWER", arbiter);
        Assert.DoesNotContain("RAWREBUTTAL", arbiter);
    }

    [Fact]
    public async Task Rephraser_never_sees_the_debate()
    {
        using var s = new Scenario(Answerer, CriticObjectingThenDone(), Judge);

        await s.RunAsync("USERQUESTION");

        var rephraser = s.ActorBuffer(PersonaTokens.JudgeRephraser);
        Assert.Contains("REPHRASED", rephraser);
        Assert.DoesNotContain("RAWANSWER", rephraser);
        Assert.DoesNotContain("NEUTRALFACTS", rephraser);
        Assert.DoesNotContain("OBJECTION1", rephraser);
    }

    [Fact]
    public async Task Profiler_sees_only_the_critic_objections()
    {
        using var s = new Scenario(Answerer, CriticObjectingThenDone(), Judge);

        await s.RunAsync("USERQUESTION");

        var profiler = s.ActorBuffer(PersonaTokens.JudgeProfiler);
        Assert.Contains("OBJECTION1", profiler);
        Assert.DoesNotContain("RAWANSWER", profiler);
        Assert.DoesNotContain("NEUTRALFACTS", profiler);
    }

    [Fact]
    public async Task Answerer_hears_critiques_verbatim_but_not_restatements()
    {
        using var s = new Scenario(Answerer, CriticObjectingThenDone(), Judge);

        await s.RunAsync("USERQUESTION");

        var answerer = s.ActorBuffer(PersonaTokens.Answerer);
        Assert.Contains("REPHRASED", answerer);   // saw the rephrased question, not the raw user text
        Assert.Contains("OBJECTION1", answerer);  // heard the critique directly
        Assert.DoesNotContain("USERQUESTION", answerer);
        Assert.DoesNotContain("NEUTRALFACTS", answerer);
    }

    [Fact]
    public async Task Max_rounds_caps_the_debate()
    {
        // Critic always objects; only the round cap can stop the loop.
        using var s = new Scenario(Answerer, _ => "{\"done\":false,\"objection\":\"AGAIN\"}", Judge, maxRounds: 1);

        await s.RunAsync("USERQUESTION");

        Assert.Single(s.Provider.Critic.Calls);
        Assert.Equal(1, s.Engine.GetStatsSnapshot().Stats.DebateRounds);
        Assert.Single(s.Observer.Verdict);
    }

    [Fact]
    public async Task Critic_can_end_the_debate_immediately()
    {
        using var s = new Scenario(Answerer, _ => "{\"done\":true}", Judge);

        await s.RunAsync("USERQUESTION");

        Assert.Single(s.Provider.Critic.Calls);
        Assert.Empty(s.Observer.Critic);          // no objection shown
        Assert.Single(s.Observer.Verdict);
        Assert.Empty(s.Observer.ProfileUpdates);  // no objections => profiler skipped
    }

    [Fact]
    public async Task Output_cap_is_passed_through_to_chat_options()
    {
        using var s = new Scenario(Answerer, _ => "{\"done\":true}", Judge);
        s.Provider.OutputCaps[DebateRole.Judge] = 2048;

        await s.RunAsync("USERQUESTION");

        Assert.All(s.Provider.Judge.Calls, c => Assert.Equal(2048, c.Options?.MaxOutputTokens));
        Assert.All(s.Provider.Answerer.Calls, c => Assert.Null(c.Options?.MaxOutputTokens));
    }
}
