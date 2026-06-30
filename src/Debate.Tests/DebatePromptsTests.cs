using Debate.Core;

namespace Debate.Tests;

public class DebatePromptsTests
{
    [Fact]
    public void BuildRephrase_substitutes_question()
    {
        var p = DebatePrompts.BuildRephrase("WHY IS THE SKY BLUE");
        Assert.Contains("WHY IS THE SKY BLUE", p);
        Assert.DoesNotContain("{question}", p);
    }

    [Fact]
    public void BuildAnswer_offers_both_answer_and_clarification_shapes()
    {
        var p = DebatePrompts.BuildAnswer("Q");
        Assert.Contains("\"answer\"", p);
        Assert.Contains("\"clarification\"", p);
        Assert.Contains("Q", p);
    }

    [Fact]
    public void BuildClarifiedAnswer_substitutes_info()
    {
        var p = DebatePrompts.BuildClarifiedAnswer("the cadence is weekly");
        Assert.Contains("the cadence is weekly", p);
        Assert.DoesNotContain("{info}", p);
    }

    [Fact]
    public void BuildClarifyForAnswerer_includes_question_and_reply()
    {
        var p = DebatePrompts.BuildClarifyForAnswerer("what scale?", "about 20 users");
        Assert.Contains("what scale?", p);
        Assert.Contains("about 20 users", p);
        Assert.DoesNotContain("{question}", p);
        Assert.DoesNotContain("{reply}", p);
    }

    [Fact]
    public void BuildProfile_numbers_the_objections()
    {
        var p = DebatePrompts.BuildProfile(new[] { "first", "second" });
        Assert.Contains("1. first", p);
        Assert.Contains("2. second", p);
    }

    [Fact]
    public void BuildProfile_with_no_objections_says_none()
    {
        var p = DebatePrompts.BuildProfile(Array.Empty<string>());
        Assert.Contains("(none)", p);
    }

    [Fact]
    public void Reask_template_disables_thinking()
    {
        var p = DebatePrompts.BuildReask("{\"answer\":\"...\"}");
        Assert.Contains(DebatePrompts.NoThinkDirective, p);
        Assert.Contains("{\"answer\":\"...\"}", p);
    }

    [Fact]
    public void WithNoThink_appends_directive()
    {
        var p = DebatePrompts.WithNoThink("hello");
        Assert.StartsWith("hello", p);
        Assert.EndsWith(DebatePrompts.NoThinkDirective, p);
    }
}
