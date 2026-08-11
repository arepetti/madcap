using Debate.Core;
using Debate.Tests.Support;

namespace Debate.Tests;

/// <summary>
/// The restater is told to re-express faithfully AND to flag claims that came with no
/// support. Those are different jobs, so they travel in different fields: the flag must
/// still reach the Critic, but it must not contaminate the restatement itself.
/// </summary>
public class RestatementContractTests
{
    private static string Judge(string p, string restateReply)
    {
        if (Phase.IsRephrase(p)) return "{\"action\":\"rephrase\",\"text\":\"REPHRASED\"}";
        if (Phase.IsRestate(p)) return restateReply;
        if (Phase.IsVerdict(p)) return "{\"answer\":\"V\",\"confidence\":\"low\",\"justification\":\"j\",\"uncertainty\":\"\"}";
        return "{}";
    }

    [Fact]
    public async Task Flagged_claims_reach_the_critic()
    {
        const string restate =
            "{\"restatement\":\"NEUTRALFACTS\",\"unsupported\":[\"throughput triples\",\"costs fall\"]}";
        using var s = new Scenario(
            _ => "{\"answer\":\"A\"}",
            _ => "{\"done\":true}",
            p => Judge(p, restate),
            buildProfile: false);

        await s.RunAsync("question");

        var criticPrompt = s.Provider.Critic.Calls.Single().LastUserMessage;
        Assert.Contains("NEUTRALFACTS", criticPrompt);
        Assert.Contains("throughput triples", criticPrompt);
        Assert.Contains("costs fall", criticPrompt);
    }

    [Fact]
    public async Task An_empty_flag_list_leaves_the_restatement_alone()
    {
        const string restate = "{\"restatement\":\"NEUTRALFACTS\",\"unsupported\":[]}";
        using var s = new Scenario(
            _ => "{\"answer\":\"A\"}",
            _ => "{\"done\":true}",
            p => Judge(p, restate),
            buildProfile: false);

        await s.RunAsync("question");

        Assert.Equal("NEUTRALFACTS", Assert.Single(s.Observer.Restatement));
    }

    [Fact]
    public async Task A_restater_that_omits_the_field_still_works()
    {
        // Small models drop optional fields; the old single-field shape must keep working.
        const string restate = "{\"restatement\":\"NEUTRALFACTS\"}";
        using var s = new Scenario(
            _ => "{\"answer\":\"A\"}",
            _ => "{\"done\":true}",
            p => Judge(p, restate),
            buildProfile: false);

        await s.RunAsync("question");

        Assert.Equal("NEUTRALFACTS", Assert.Single(s.Observer.Restatement));
    }

    [Fact]
    public void The_critic_scratchpad_is_parsed_and_never_used_as_the_objection()
    {
        const string raw =
            "{\"scratch\":\"weighing the assumptions\",\"done\":false,\"objection\":\"THEOBJECTION\"}";
        Assert.True(JsonProtocol.TryParse<CriticReply>(raw, out var reply));
        Assert.Equal("THEOBJECTION", reply!.Objection);
        Assert.False(reply.Done);
    }

    [Fact]
    public async Task A_critic_scratchpad_is_not_shown_to_anyone()
    {
        using var s = new Scenario(
            _ => "{\"answer\":\"A\"}",
            _ => "{\"scratch\":\"SECRETNOTES\",\"done\":false,\"objection\":\"THEOBJECTION\"}",
            p => Judge(p, "{\"restatement\":\"NEUTRALFACTS\"}"),
            maxRounds: 1,
            buildProfile: false);

        await s.RunAsync("question");

        Assert.Contains("THEOBJECTION", s.Observer.Critic);
        Assert.DoesNotContain(s.Observer.Critic, c => c.Contains("SECRETNOTES"));
        Assert.DoesNotContain("SECRETNOTES", s.ActorBuffer(PersonaTokens.Answerer));
        Assert.DoesNotContain("SECRETNOTES", s.ActorBuffer(PersonaTokens.JudgeArbiter));
    }
}
