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
    public void Reask_disables_thinking_on_a_model_that_understands_the_switch()
    {
        var p = DebatePrompts.BuildReask("{\"answer\":\"...\"}", "qwen3-4b");
        Assert.Contains(DebatePrompts.NoThinkDirective, p);
        Assert.Contains("{\"answer\":\"...\"}", p);
    }

    [Fact]
    public void Reask_omits_the_switch_on_other_model_families()
    {
        var p = DebatePrompts.BuildReask("{\"answer\":\"...\"}", "gpt-4o");
        Assert.DoesNotContain(DebatePrompts.NoThinkDirective, p);
        Assert.Contains("{\"answer\":\"...\"}", p);
    }

    [Theory]
    [InlineData("qwen3-4b", true)]
    [InlineData("qwen3-8b", true)]
    [InlineData("Qwen2.5-7B-Instruct", true)]
    [InlineData("gpt-4o", false)]
    [InlineData("phi-4-mini", false)]
    [InlineData("ministral-3-3b-instruct-2512", false)]
    [InlineData(null, false)]
    public void The_switch_is_recognised_per_model_family(string? model, bool expected) =>
        Assert.Equal(expected, DebatePrompts.SupportsNoThink(model));

    [Fact]
    public void WithNoThink_appends_the_directive_only_where_it_is_understood()
    {
        Assert.EndsWith(DebatePrompts.NoThinkDirective, DebatePrompts.WithNoThink("hello", "qwen3-4b"));
        Assert.Equal("hello", DebatePrompts.WithNoThink("hello", "gpt-4o"));
    }

    [Fact]
    public void ApplyNoThink_substitutes_or_removes_the_persona_placeholder()
    {
        const string persona = "be terse.\n{no_think}";

        Assert.Equal("be terse.\n/no_think", DebatePrompts.ApplyNoThink(persona, "qwen3-4b"));
        Assert.Equal("be terse.", DebatePrompts.ApplyNoThink(persona, "phi-4"));
    }

    [Fact]
    public void No_persona_file_hardcodes_the_directive()
    {
        // The switch is Qwen3-specific, so it must come from ApplyNoThink and not from
        // text shared by every backend.
        var personaDirectory = Path.Combine(AppContext.BaseDirectory, "personas");
        var files = Directory.GetFiles(personaDirectory, "*.txt");
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain(
                DebatePrompts.NoThinkDirective,
                text.Replace(DebatePrompts.NoThinkPlaceholder, string.Empty));
        }
    }
}
