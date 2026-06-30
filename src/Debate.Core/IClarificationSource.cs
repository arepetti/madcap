namespace Debate.Core;

/// <summary>
/// Asks the user to answer a Judge clarifying question mid-question. Abstracted
/// (and async) so the algorithm doesn't assume a console: a GUI/web host can
/// satisfy it however it likes.
/// </summary>
public interface IClarificationSource
{
    /// <summary>
    /// Request a reply to the Judge's <c>CLARIFY:</c> message. Return the user's
    /// text, or <c>null</c>/empty to abort the current question.
    /// </summary>
    Task<string?> RequestClarificationAsync(string judgeMessage, CancellationToken cancellationToken);
}
