using Debate.Core;
using Debate.Tests.Fakes;
using Debate.Tests.Support;
using Microsoft.Extensions.AI;

namespace Debate.Tests;

/// <summary>
/// The conversation buffer must only ever advance in complete user/assistant pairs. The
/// Answerer is the one actor that is never invalidated, so a turn left dangling by a
/// failed call would survive into every later question.
/// </summary>
public sealed class ActorBufferTests : IDisposable
{
    private readonly TempPersonas _personas = new();

    public void Dispose() => _personas.Dispose();

    private DebateContext Context(Func<string, string> answerer) =>
        new(TestFactory.Config(),
            TestFactory.Provider(answerer, _ => "{}", _ => "{}"),
            _personas.Library);

    [Fact]
    public async Task Successful_send_appends_the_user_turn_and_the_reply()
    {
        var ctx = Context(_ => "ANSWER");

        var reply = await ctx.Answerer.SendAsync("question one", CancellationToken.None);

        Assert.Equal("ANSWER", reply);
        Assert.Collection(
            ctx.Answerer.Messages,
            m => Assert.Equal(ChatRole.System, m.Role),
            m =>
            {
                Assert.Equal(ChatRole.User, m.Role);
                Assert.Equal("question one", m.Text);
            },
            m =>
            {
                Assert.Equal(ChatRole.Assistant, m.Role);
                Assert.Equal("ANSWER", m.Text);
            });
    }

    [Fact]
    public async Task Failed_send_leaves_no_dangling_user_turn()
    {
        var ctx = Context(_ => throw new InvalidOperationException("model host is not running"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ctx.Answerer.SendAsync("question one", CancellationToken.None));

        Assert.Equal(ChatRole.System, Assert.Single(ctx.Answerer.Messages).Role);
    }

    [Fact]
    public async Task A_failed_send_does_not_corrupt_the_next_one()
    {
        bool fail = true;
        var ctx = Context(_ => fail
            ? throw new InvalidOperationException("transient failure")
            : "ANSWER");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ctx.Answerer.SendAsync("question one", CancellationToken.None));

        fail = false;
        await ctx.Answerer.SendAsync("question two", CancellationToken.None);

        // The model must never see two user turns in a row.
        var roles = ctx.Answerer.Messages.Select(m => m.Role).ToArray();
        Assert.Equal([ChatRole.System, ChatRole.User, ChatRole.Assistant], roles);
        Assert.Equal("question two", ctx.Answerer.Messages[1].Text);
    }

    [Fact]
    public async Task The_failed_turn_is_not_sent_again_on_the_next_call()
    {
        bool fail = true;
        var provider = TestFactory.Provider(
            _ => fail ? throw new InvalidOperationException("transient failure") : "ANSWER",
            _ => "{}",
            _ => "{}");
        var ctx = new DebateContext(TestFactory.Config(), provider, _personas.Library);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ctx.Answerer.SendAsync("question one", CancellationToken.None));

        fail = false;
        await ctx.Answerer.SendAsync("question two", CancellationToken.None);

        var lastCall = provider.Answerer.Calls[^1];
        Assert.DoesNotContain(lastCall.History, m => m.Text == "question one");
    }
}
