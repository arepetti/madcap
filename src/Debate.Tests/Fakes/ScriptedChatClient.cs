using Microsoft.Extensions.AI;

namespace Debate.Tests.Fakes;

/// <summary>
/// A fake <see cref="IChatClient"/> that returns a scripted reply chosen from the
/// last user message, and records every request it received (full message list and
/// the <see cref="ChatOptions"/>). Lets a test drive the pipeline deterministically
/// and then assert on exactly what each actor was sent.
/// </summary>
public sealed class ScriptedChatClient : IChatClient
{
    private readonly Func<string, string> _respond;

    public ScriptedChatClient(Func<string, string> respond) => _respond = respond;

    /// <summary>Every call's (last user message, full history, options), in order.</summary>
    public List<RecordedCall> Calls { get; } = new();

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        var lastUser = list.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;
        Calls.Add(new RecordedCall(lastUser, list, options));

        var reply = _respond(lastUser);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The test client does not stream.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    public sealed record RecordedCall(string LastUserMessage, IReadOnlyList<ChatMessage> History, ChatOptions? Options);
}
