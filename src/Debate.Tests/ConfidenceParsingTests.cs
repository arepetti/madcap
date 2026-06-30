using Debate.Core;

namespace Debate.Tests;

public class ConfidenceParsingTests
{
    [Theory]
    [InlineData("Confidence: high", ConfidenceLabel.High)]
    [InlineData("I have medium confidence in this", ConfidenceLabel.Medium)]
    [InlineData("confidence level is LOW overall", ConfidenceLabel.Low)]
    [InlineData("The answer is high quality and well sourced.", ConfidenceLabel.High)]
    public void Parses_label_from_text(string text, ConfidenceLabel expected)
    {
        Assert.Equal(expected, DebatePipeline.ParseConfidence(text));
    }

    [Fact]
    public void Returns_null_when_no_label_present()
    {
        Assert.Null(DebatePipeline.ParseConfidence("no relevant words here"));
    }

    [Fact]
    public void Prefers_label_after_the_confidence_anchor()
    {
        // "low" appears as a distractor before the anchor; the label after it wins.
        Assert.Equal(ConfidenceLabel.High, DebatePipeline.ParseConfidence("low risk, but confidence: high"));
    }
}
