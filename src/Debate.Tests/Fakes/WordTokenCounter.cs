using Debate.Core;

namespace Debate.Tests.Fakes;

/// <summary>A trivial whitespace token counter — deterministic and dependency-free.</summary>
public sealed class WordTokenCounter : ITokenCounter
{
    public int Count(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    public string Method => "word-count (test)";
}
