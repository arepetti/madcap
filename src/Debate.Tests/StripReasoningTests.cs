using Debate.Core;

namespace Debate.Tests;

public class StripReasoningTests
{
    [Fact]
    public void Removes_paired_block_keeps_surrounding_text()
    {
        Assert.Equal("AB", JsonProtocol.StripReasoning("A<think>noise</think>B"));
    }

    [Fact]
    public void Unterminated_open_tag_keeps_following_text()
    {
        Assert.Equal("\n\nkept", JsonProtocol.StripReasoning("<think>\n\nkept"));
    }

    [Fact]
    public void Stray_closing_tag_drops_preceding_text()
    {
        Assert.Equal("kept", JsonProtocol.StripReasoning("dropped</think>kept"));
    }

    [Fact]
    public void Empty_input_is_returned_unchanged()
    {
        Assert.Equal("", JsonProtocol.StripReasoning(""));
    }

    [Fact]
    public void Text_without_tags_is_unchanged()
    {
        Assert.Equal("plain", JsonProtocol.StripReasoning("plain"));
    }
}
