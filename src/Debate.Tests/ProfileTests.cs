using Debate.Core;

namespace Debate.Tests;

public class ProfileTests
{
    [Fact]
    public void Identical_notes_are_fully_similar()
    {
        Assert.Equal(1.0, Profile.Similarity("tends to overgeneralise badly", "tends to overgeneralise badly"), 3);
    }

    [Fact]
    public void Unrelated_notes_have_low_similarity()
    {
        Assert.True(Profile.Similarity("ignores database indexing costs", "assumes unlimited network bandwidth") < Profile.SimilarityThreshold);
    }

    [Fact]
    public void Similar_notes_clear_the_threshold()
    {
        var a = "might tend to overgeneralise microservices benefits";
        var b = "tends to overgeneralise the benefits of microservices";
        Assert.True(Profile.Similarity(a, b) >= Profile.SimilarityThreshold);
    }

    [Fact]
    public void Stopwords_only_yields_zero()
    {
        Assert.Equal(0.0, Profile.Similarity("the a an of to", "is are was were"));
    }

    [Theory]
    [InlineData("tends to be too verbose and wordy", "verbose")]
    [InlineData("uses too many bullet lists", "bullet")]
    [InlineData("the tone is too casual", "tone")]
    public void Stylistic_notes_are_detected(string note, string expectedKeyword)
    {
        Assert.True(Profile.IsStylistic(note, out var matched));
        Assert.Equal(expectedKeyword, matched);
    }

    [Fact]
    public void Substantive_note_is_not_stylistic()
    {
        Assert.False(Profile.IsStylistic("ignores transaction consistency under failure", out var matched));
        Assert.Null(matched);
    }
}
