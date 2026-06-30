using Debate.Core;

namespace Debate.Tests.Fakes;

/// <summary>
/// Serves scripted user replies to clarification requests, in order. Returns null
/// (cancel) once exhausted unless <see cref="DefaultReply"/> is set. Records the
/// questions it was asked.
/// </summary>
public sealed class QueueClarificationSource : IClarificationSource
{
    private readonly Queue<string?> _replies;

    public QueueClarificationSource(params string?[] replies) => _replies = new Queue<string?>(replies);

    public string? DefaultReply { get; set; }

    public List<string> AskedQuestions { get; } = new();

    public Task<string?> RequestClarificationAsync(string judgeMessage, CancellationToken cancellationToken)
    {
        AskedQuestions.Add(judgeMessage);
        var reply = _replies.Count > 0 ? _replies.Dequeue() : DefaultReply;
        return Task.FromResult(reply);
    }
}
