using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Debate.Core;

/// <summary>
/// The Judge's reply to a Phase 1 rephrase request: either a neutral rephrasing of
/// the question or a single clarifying question for the user.
/// </summary>
public sealed class JudgeRephraseReply
{
    /// <summary><c>rephrase</c> or <c>clarify</c>.</summary>
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Action { get; set; }

    /// <summary>The neutral rephrasing (when rephrasing) or the question to ask (when clarifying).</summary>
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string Text { get; set; } = string.Empty;

    public bool IsClarify => string.Equals(Action?.Trim(), "clarify", StringComparison.OrdinalIgnoreCase);
    public bool IsRephrase => string.Equals(Action?.Trim(), "rephrase", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// An Answerer reply: either an answer (the initial answer or a response to a Critic
/// objection) or — only at the initial-answer stage — a request for missing information
/// (<see cref="Clarification"/>) to be put to the user via the Judge rephraser.
/// </summary>
public sealed class AnswererReply
{
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Answer { get; set; }

    /// <summary>A single focused question for the user when essential information is missing.</summary>
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Clarification { get; set; }

    /// <summary>True when the Answerer wants to ask for information instead of answering.</summary>
    public bool IsClarification =>
        string.IsNullOrWhiteSpace(Answer) && !string.IsNullOrWhiteSpace(Clarification);
}

/// <summary>The Judge's neutral, fact-only restatement of an Answerer turn (what the Critic sees).</summary>
public sealed class JudgeRestatementReply
{
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string Restatement { get; set; } = string.Empty;

    /// <summary>
    /// Claims the restater judged to be asserted without support. Kept out of
    /// <see cref="Restatement"/> so that "re-express faithfully" and "note a gap" stay
    /// separate instructions — asking for both in one prose field asks the model to add
    /// its own judgement to text it was just told not to editorialise.
    /// </summary>
    public List<string>? Unsupported { get; set; }
}

/// <summary>A Critic turn: either an objection, or a signal that it has no further objections.</summary>
public sealed class CriticReply
{
    /// <summary>
    /// The Critic's working notes. Read and discarded by the pipeline — it exists only so
    /// the "weigh the position, then report one objection" instruction has a place to do
    /// the weighing. Without it, small models either skip the analysis or smuggle it into
    /// the objection as an ad-hoc structure.
    /// </summary>
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Scratch { get; set; }

    /// <summary>True when the Critic has no further substantive objection (ends the round loop).</summary>
    [JsonConverter(typeof(FlexibleBoolConverter))]
    public bool Done { get; set; }

    /// <summary>The strongest objection to the restated answer (empty/omitted when <see cref="Done"/>).</summary>
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Objection { get; set; }
}

/// <summary>The Judge's final verdict over the debate transcript.</summary>
public sealed class JudgeVerdictReply
{
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string Answer { get; set; } = string.Empty;

    /// <summary><c>low</c>, <c>medium</c>, or <c>high</c>.</summary>
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string Confidence { get; set; } = string.Empty;

    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Justification { get; set; }

    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Uncertainty { get; set; }
}

/// <summary>The Judge's Phase 3 profile note: a single tentative tendency, or none.</summary>
public sealed class JudgeProfileReply
{
    /// <summary>A short tentative tendency, or null/empty/"none" when there is nothing to report.</summary>
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Tendency { get; set; }
}

/// <summary>
/// Reads a string field even when the model puts a number, boolean, object, or array
/// where a string was requested (a very common local-model mistake). Non-string JSON
/// is flattened to readable text: scalars become their literal text, and objects /
/// arrays are reduced to their string/number leaf values joined by newlines. This
/// salvages a well-structured-but-wrong reply (e.g. an <c>objection</c> returned as a
/// nested object) instead of discarding it and paying for a re-ask.
/// </summary>
public sealed class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
            case JsonTokenType.True:
            case JsonTokenType.False:
            {
                using var scalar = JsonDocument.ParseValue(ref reader);
                return scalar.RootElement.GetRawText();
            }

            case JsonTokenType.StartObject:
            case JsonTokenType.StartArray:
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                var sb = new StringBuilder();
                CollectLeafText(doc.RootElement, sb);
                return sb.ToString().Trim();
            }

            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);

    private static void CollectLeafText(JsonElement element, StringBuilder sb)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                Append(sb, element.GetString());
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                Append(sb, element.GetRawText());
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectLeafText(property.Value, sb);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectLeafText(item, sb);
                }

                break;
        }
    }

    private static void Append(StringBuilder sb, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (sb.Length > 0)
        {
            sb.Append('\n');
        }

        sb.Append(text.Trim());
    }
}

/// <summary>
/// Reads a boolean field even when the model emits it as a string ("true"/"yes"/"1")
/// or a number (0 = false, non-zero = true).
/// </summary>
public sealed class FlexibleBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return true;
            case JsonTokenType.False:
                return false;
            case JsonTokenType.String:
                var s = reader.GetString()?.Trim();
                if (bool.TryParse(s, out var parsed))
                {
                    return parsed;
                }

                return string.Equals(s, "1", StringComparison.Ordinal)
                    || string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase);
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var n) ? n != 0 : reader.GetDouble() != 0;
            default:
                reader.Skip();
                return false;
        }
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
        writer.WriteBooleanValue(value);
}

/// <summary>
/// Tolerant JSON parsing for actor replies. Local models frequently wrap JSON in
/// prose or markdown code fences; this extracts and parses the object leniently so
/// the pipeline never depends on exact-string formatting.
/// </summary>
public static class JsonProtocol
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>
    /// Attempts to extract and deserialize a single JSON object of type
    /// <typeparamref name="T"/> from a raw model reply. Returns false (and a null
    /// value) when no parseable object is found.
    /// </summary>
    public static bool TryParse<T>(string? raw, out T? value) where T : class =>
        TryParse(raw, out value, out _);

    /// <summary>
    /// As <see cref="TryParse{T}(string?, out T?)"/>, but also reports a short
    /// human-readable reason on failure (empty reply, no JSON object, or the
    /// deserializer's error message), for diagnostics/logging.
    /// </summary>
    public static bool TryParse<T>(string? raw, out T? value, out string? error) where T : class
    {
        value = null;
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "the reply was empty";
            return false;
        }

        var candidate = ExtractJsonObject(raw);
        if (candidate is null)
        {
            error = "no JSON object ({ ... }) was found in the reply";
            return false;
        }

        // First try the extracted text verbatim so a well-formed reply keeps its
        // escaped "\n" sequences (which become real newlines in the text).
        if (TryDeserialize(candidate, out value, out error))
        {
            return true;
        }

        // Fallback: some models put raw (unescaped) newlines inside string values,
        // which is invalid JSON. Collapsing literal CR/LF characters to spaces rescues
        // those replies. This does not affect well-formed JSON: the "\n" escape is two
        // characters (untouched), and newlines between tokens are insignificant.
        var collapsed = candidate.Replace('\r', ' ').Replace('\n', ' ');
        if (!string.Equals(collapsed, candidate, StringComparison.Ordinal)
            && TryDeserialize(collapsed, out value, out _))
        {
            error = null;
            return true;
        }

        // Keep the original (more informative) error from the verbatim attempt.
        return false;
    }

    private static bool TryDeserialize<T>(string json, out T? value, out string? error) where T : class
    {
        value = null;
        error = null;
        try
        {
            value = JsonSerializer.Deserialize<T>(json, Options);
            if (value is null)
            {
                error = "the extracted JSON deserialized to null";
                return false;
            }

            return true;
        }
        catch (JsonException e)
        {
            error = e.Message;
            return false;
        }
    }

    // Removes <think>...</think> reasoning emitted by "thinking" models (e.g. Qwen3)
    // so it never confuses JSON extraction.
    private static readonly Regex ThinkBlock =
        new("<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Isolates the most likely JSON object in <paramref name="raw"/>: strips any
    /// reasoning block, then returns the span from the first <c>{</c> to the last
    /// <c>}</c> (inclusive). Returns null if there is no brace pair.
    ///
    /// Markdown code fences need no special handling — they contain no braces, so the
    /// span between the outermost braces already excludes them wherever they sit. An
    /// earlier version stripped fences by cutting to the first newline, which silently
    /// destroyed the reply when a model put the fence and the JSON on one line.
    /// </summary>
    private static string? ExtractJsonObject(string raw)
    {
        var text = StripReasoning(raw.Trim());

        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return text.Substring(start, end - start + 1);
    }

    /// <summary>
    /// Drops "thinking" reasoning so it cannot confuse JSON extraction:
    /// <list type="number">
    /// <item>removes paired <c>&lt;think&gt;...&lt;/think&gt;</c> blocks;</item>
    /// <item>if a stray closing <c>&lt;/think&gt;</c> remains (opening tag omitted),
    /// drops everything up to and including it — that text was the reasoning;</item>
    /// <item>removes any remaining lone opening <c>&lt;think&gt;</c> tag WITHOUT
    /// dropping what follows it. A model that opens <c>&lt;think&gt;</c> but never
    /// closes it may have emitted its actual answer right after the tag (common with
    /// Qwen3 under <c>/no_think</c>); cutting to the end would throw that answer away.
    /// If instead it was a runaway reasoning loop, no JSON object follows and parsing
    /// fails cleanly anyway.</item>
    /// </list>
    /// Public so actors can scrub reasoning out of a reply before storing it in their
    /// conversation buffer — keeping a degenerate thinking loop from polluting the
    /// context of a subsequent re-ask.
    /// </summary>
    public static string StripReasoning(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        text = ThinkBlock.Replace(text, string.Empty);

        int close = text.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (close >= 0)
        {
            text = text[(close + "</think>".Length)..];
        }

        // Remove dangling opening tags but keep the text after them (it may be the answer).
        return text.Replace("<think>", string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
