using Debate.Core;

namespace Debate.Tests;

public class JsonProtocolTests
{
    [Fact]
    public void Parses_clean_object()
    {
        Assert.True(JsonProtocol.TryParse<AnswererReply>("{\"answer\":\"hello\"}", out var reply));
        Assert.Equal("hello", reply!.Answer);
    }

    [Fact]
    public void Strips_markdown_code_fence()
    {
        const string raw = "```json\n{\"answer\":\"fenced\"}\n```";
        Assert.True(JsonProtocol.TryParse<AnswererReply>(raw, out var reply));
        Assert.Equal("fenced", reply!.Answer);
    }

    [Fact]
    public void Strips_fence_opened_on_the_same_line_as_the_object()
    {
        // Regression: fence stripping used to cut everything up to the first newline,
        // which threw away the whole object when the model put it on the fence line.
        const string raw = "```json {\"answer\":\"same line\"}\n```";
        Assert.True(JsonProtocol.TryParse<AnswererReply>(raw, out var reply));
        Assert.Equal("same line", reply!.Answer);
    }

    [Fact]
    public void Strips_a_single_line_fence()
    {
        const string raw = "```{\"answer\":\"one line\"}```";
        Assert.True(JsonProtocol.TryParse<AnswererReply>(raw, out var reply));
        Assert.Equal("one line", reply!.Answer);
    }

    [Fact]
    public void Ignores_prose_around_the_object()
    {
        const string raw = "Sure! Here is my reply: {\"answer\":\"x\"} — hope that helps.";
        Assert.True(JsonProtocol.TryParse<AnswererReply>(raw, out var reply));
        Assert.Equal("x", reply!.Answer);
    }

    [Fact]
    public void Removes_paired_think_block()
    {
        const string raw = "<think>let me reason about this</think>{\"answer\":\"done\"}";
        Assert.True(JsonProtocol.TryParse<AnswererReply>(raw, out var reply));
        Assert.Equal("done", reply!.Answer);
    }

    [Fact]
    public void Recovers_json_after_unterminated_think_tag()
    {
        // Regression: a Qwen3 reply that opens <think> then emits the answer without
        // ever closing the tag must NOT have its JSON discarded.
        const string raw = "<think>\n\n{\"action\":\"rephrase\",\"text\":\"neutral question?\"}";
        Assert.True(JsonProtocol.TryParse<JudgeRephraseReply>(raw, out var reply));
        Assert.True(reply!.IsRephrase);
        Assert.Equal("neutral question?", reply.Text);
    }

    [Fact]
    public void Drops_reasoning_before_a_stray_closing_think_tag()
    {
        const string raw = "reasoning that ran on and on</think>{\"answer\":\"final\"}";
        Assert.True(JsonProtocol.TryParse<AnswererReply>(raw, out var reply));
        Assert.Equal("final", reply!.Answer);
    }

    [Fact]
    public void Runaway_reasoning_with_no_json_fails_cleanly()
    {
        const string raw = "<think>I keep thinking and thinking but never answer.";
        Assert.False(JsonProtocol.TryParse<AnswererReply>(raw, out var reply, out var error));
        Assert.Null(reply);
        Assert.Contains("JSON object", error);
    }

    [Fact]
    public void Empty_reply_fails_with_reason()
    {
        Assert.False(JsonProtocol.TryParse<AnswererReply>("   ", out _, out var error));
        Assert.Contains("empty", error);
    }

    [Fact]
    public void No_braces_fails_with_reason()
    {
        Assert.False(JsonProtocol.TryParse<AnswererReply>("just some prose", out _, out var error));
        Assert.Contains("JSON object", error);
    }

    [Fact]
    public void Rescues_raw_newlines_inside_string_values()
    {
        // Literal (unescaped) newlines inside a JSON string are invalid; the parser
        // collapses CR/LF to spaces as a fallback.
        const string raw = "{\"answer\":\"line one\nline two\"}";
        Assert.True(JsonProtocol.TryParse<AnswererReply>(raw, out var reply));
        Assert.Contains("line one", reply!.Answer);
        Assert.Contains("line two", reply.Answer);
    }

    [Fact]
    public void Flexible_string_flattens_a_nested_object_answer()
    {
        // The exact failure seen in practice: the Answerer returns a structured object
        // instead of a string. We salvage its leaf values rather than reject the reply.
        const string raw =
            "{\"answer\":{\"architecture\":\"Microservices\",\"languages\":[\"Python\",\"C#\"]}}";
        Assert.True(JsonProtocol.TryParse<AnswererReply>(raw, out var reply));
        Assert.Contains("Microservices", reply!.Answer);
        Assert.Contains("Python", reply.Answer);
        Assert.Contains("C#", reply.Answer);
    }

    [Theory]
    [InlineData("{\"done\":true}", true)]
    [InlineData("{\"done\":\"true\"}", true)]
    [InlineData("{\"done\":\"yes\"}", true)]
    [InlineData("{\"done\":\"1\"}", true)]
    [InlineData("{\"done\":1}", true)]
    [InlineData("{\"done\":false}", false)]
    [InlineData("{\"done\":\"no\"}", false)]
    [InlineData("{\"done\":0}", false)]
    public void Flexible_bool_reads_many_encodings(string raw, bool expected)
    {
        Assert.True(JsonProtocol.TryParse<CriticReply>(raw, out var reply));
        Assert.Equal(expected, reply!.Done);
    }

    [Fact]
    public void Flexible_string_salvages_an_action_wrapped_in_an_array()
    {
        const string raw = "{\"action\":[\"rephrase\"],\"text\":\"neutral question?\"}";
        Assert.True(JsonProtocol.TryParse<JudgeRephraseReply>(raw, out var reply));
        Assert.True(reply!.IsRephrase);
    }

    [Fact]
    public void Confidence_number_is_read_as_text()
    {
        const string raw = "{\"answer\":\"a\",\"confidence\":3}";
        Assert.True(JsonProtocol.TryParse<JudgeVerdictReply>(raw, out var reply));
        Assert.Equal("3", reply!.Confidence);
    }
}
