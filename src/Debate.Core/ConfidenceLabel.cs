namespace Debate.Core;

/// <summary>
/// The verdict's self-reported confidence. Parsed from the Judge's verdict text;
/// <c>null</c> when no label could be found.
/// </summary>
public enum ConfidenceLabel
{
    Low,
    Medium,
    High,
}
