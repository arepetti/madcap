using Debate.Core;
using Debate.Tests.Support;

namespace Debate.Tests;

public class PersonaLibraryTests
{
    [Fact]
    public void RenderPriorExchanges_pairs_questions_with_verdicts()
    {
        var text = PersonaLibrary.RenderPriorExchanges(
            new[] { "Q1", "Q2" }, new[] { "V1", "V2" });

        Assert.Contains("1. Q: Q1", text);
        Assert.Contains("Verdict: V1", text);
        Assert.Contains("2. Q: Q2", text);
    }

    [Fact]
    public void RenderPriorExchanges_handles_missing_verdict()
    {
        var text = PersonaLibrary.RenderPriorExchanges(new[] { "Q1" }, Array.Empty<string>());
        Assert.Contains("1. Q: Q1", text);
        Assert.DoesNotContain("Verdict:", text);
    }

    [Fact]
    public void RenderPriorExchanges_empty_says_none()
    {
        Assert.Equal("(none yet)", PersonaLibrary.RenderPriorExchanges(Array.Empty<string>(), Array.Empty<string>()));
    }

    [Fact]
    public void Falls_back_to_default_preset_when_named_missing()
    {
        using var personas = new TempPersonas(preset: "default");

        // 'technical' files don't exist; resolution should fall back to default.*.
        var path = personas.Library.ResolvePersonaPath("technical", PersonaTokens.Answerer);

        Assert.NotNull(path);
        Assert.Contains("default.answerer.txt", path);
    }

    [Fact]
    public void Returns_null_when_no_file_exists()
    {
        using var personas = new TempPersonas();
        Assert.Null(personas.Library.ResolvePersonaPath("default", "nonexistent-token"));
    }

    [Fact]
    public void Load_throws_with_helpful_message_when_missing()
    {
        using var personas = new TempPersonas();
        var ex = Assert.Throws<FileNotFoundException>(() => personas.Library.Load("default", "missing"));
        Assert.Contains("missing", ex.Message);
    }
}
