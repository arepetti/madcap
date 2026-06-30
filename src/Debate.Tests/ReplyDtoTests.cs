using Debate.Core;

namespace Debate.Tests;

public class ReplyDtoTests
{
    [Theory]
    [InlineData("rephrase", true, false)]
    [InlineData("REPHRASE", true, false)]
    [InlineData(" clarify ", false, true)]
    [InlineData("nonsense", false, false)]
    public void JudgeRephraseReply_action_flags(string action, bool isRephrase, bool isClarify)
    {
        var r = new JudgeRephraseReply { Action = action };
        Assert.Equal(isRephrase, r.IsRephrase);
        Assert.Equal(isClarify, r.IsClarify);
    }

    [Fact]
    public void AnswererReply_is_clarification_only_when_answer_absent()
    {
        Assert.True(new AnswererReply { Clarification = "what scale?" }.IsClarification);
        Assert.False(new AnswererReply { Answer = "use postgres", Clarification = "what scale?" }.IsClarification);
        Assert.False(new AnswererReply { Answer = "use postgres" }.IsClarification);
        Assert.False(new AnswererReply().IsClarification);
    }
}
