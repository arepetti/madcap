using Microsoft.ML.Tokenizers;

namespace Debate.Core;

/// <summary>
/// Token counter backed by the cl100k_base BPE (the same encoding the original
/// Python used via tiktoken). Falls back to a char/4 heuristic if the tokenizer
/// cannot be created. Approximate for non-OpenAI models, but consistent, which
/// is what the comparative stats need.
/// </summary>
public sealed class TiktokenCounter : ITokenCounter
{
    private readonly Tokenizer? _tokenizer;

    public TiktokenCounter()
    {
        try
        {
            _tokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");
            Method = "cl100k_base BPE (approximate for non-OpenAI models)";
        }
        catch
        {
            _tokenizer = null;
            Method = "char/4 heuristic";
        }
    }

    public string Method { get; }

    public int Count(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return _tokenizer is not null
            ? _tokenizer.CountTokens(text)
            : Math.Max(1, text.Length / 4);
    }
}
