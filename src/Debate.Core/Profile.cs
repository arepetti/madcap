using System.Text.RegularExpressions;

namespace Debate.Core;

/// <summary>A candidate Answerer-tendency note with an observation count.</summary>
public sealed class ProfileEntry
{
    public ProfileEntry(string text)
    {
        Text = text;
        Count = 1;
    }

    public string Text { get; }
    public int Count { get; set; }
}

/// <summary>
/// Thresholds and helpers for the cross-round Answerer profile. Ports
/// <c>profile.py</c>: Jaccard similarity over content words, a stylistic-note
/// filter, and the surfacing/capacity policy.
/// </summary>
public static partial class Profile
{
    /// <summary>Below this Jaccard score, two notes are treated as different.</summary>
    public const double SimilarityThreshold = 0.5;

    /// <summary>A note must be observed at least this many times before the Critic sees it.</summary>
    public const int MinCountToSurface = 2;

    /// <summary>Hard cap on candidate notes; oldest single-occurrence entry is evicted first.</summary>
    public const int MaxEntries = 10;

    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "to", "of", "in", "on", "at", "for", "with", "and", "or",
        "but", "is", "are", "was", "were", "be", "been", "being", "have", "has",
        "had", "do", "does", "did", "will", "would", "could", "should", "may",
        "might", "can", "tends", "tend", "he", "his", "him", "it", "its", "they",
        "them", "their", "this", "that", "these", "those", "as", "if", "than",
        "then", "so", "such", "very", "more", "most", "less", "least", "by",
        "from", "into", "about", "over", "under", "again",
    };

    private static readonly HashSet<string> StylisticKeywords = new(StringComparer.Ordinal)
    {
        "length", "lengthy", "long", "longer", "short", "shorter", "brief",
        "verbose", "concise", "terse", "wordy", "rambling",
        "format", "formatting", "formatted", "structure", "structured",
        "bullet", "bullets", "list", "lists", "heading", "headings",
        "header", "headers", "section", "sections", "paragraph", "paragraphs",
        "indent", "indentation", "whitespace", "newline", "newlines",
        "markdown", "prose",
        "tone", "register", "style", "stylistic", "voice", "phrasing",
        "wording", "vocabulary", "diction", "language",
        "casual", "formal", "informal", "playful", "serious",
        "friendly", "polite", "blunt",
        "emoji", "emojis", "emoticon", "emoticons",
        "punctuation", "capitalization", "uppercase", "lowercase",
        "bold", "italic", "italics",
        "readable", "readability", "presentation", "layout",
        "organize", "organized", "organization",
    };

    [GeneratedRegex("[a-z][a-z\\-]+")]
    private static partial Regex WordRegex();

    private static HashSet<string> Tokenize(string text)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in WordRegex().Matches(text.ToLowerInvariant()))
        {
            var w = m.Value;
            if (w.Length > 2 && !Stopwords.Contains(w))
            {
                result.Add(w);
            }
        }
        return result;
    }

    /// <summary>Jaccard similarity over content words of two notes (0.0-1.0).</summary>
    public static double Similarity(string a, string b)
    {
        var ta = Tokenize(a);
        var tb = Tokenize(b);
        if (ta.Count == 0 || tb.Count == 0)
        {
            return 0.0;
        }

        int intersection = ta.Count(tb.Contains);
        int union = ta.Count + tb.Count - intersection;
        return (double)intersection / union;
    }

    /// <summary>
    /// True if the note is about stylistic matters (length, tone, formatting,
    /// ...), which the protocol deliberately ignores. Returns the matched
    /// keyword for diagnostics.
    /// </summary>
    public static bool IsStylistic(string note, out string? matched)
    {
        foreach (Match m in WordRegex().Matches(note.ToLowerInvariant()))
        {
            if (StylisticKeywords.Contains(m.Value))
            {
                matched = m.Value;
                return true;
            }
        }

        matched = null;
        return false;
    }
}
