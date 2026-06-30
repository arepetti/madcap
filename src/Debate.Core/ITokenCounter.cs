namespace Debate.Core;

/// <summary>
/// Counts tokens for stats. Abstracted so the counting strategy (a real
/// tokenizer vs a heuristic) is swappable and testable.
/// </summary>
public interface ITokenCounter
{
    int Count(string text);

    /// <summary>Human-readable description of the method, shown in stats.</summary>
    string Method { get; }
}
