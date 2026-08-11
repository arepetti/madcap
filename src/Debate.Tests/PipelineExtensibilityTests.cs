using Debate.Core;
using Debate.Tests.Fakes;
using Debate.Tests.Support;

namespace Debate.Tests;

/// <summary>
/// architecture.md tells contributors to change the debate process by subclassing
/// <see cref="DebatePipeline"/> and overriding a phase. This holds that door open.
/// </summary>
public sealed class PipelineExtensibilityTests : IDisposable
{
    private readonly TempPersonas _personas = new();

    public void Dispose() => _personas.Dispose();

    /// <summary>A pipeline that redacts every restatement before the Critic sees it.</summary>
    private sealed class RedactingPipeline : DebatePipeline
    {
        public RedactingPipeline(
            DebateContext context, IDebateObserver observer, IClarificationSource clarifications, ITokenCounter tokens)
            : base(context, observer, clarifications, tokens)
        {
        }

        public int RestateCalls { get; private set; }

        protected override async Task<string> RestateAsync(
            string answererReply, int round, int maxRounds, CancellationToken cancellationToken)
        {
            RestateCalls++;
            await base.RestateAsync(answererReply, round, maxRounds, cancellationToken).ConfigureAwait(false);
            return "REDACTED";
        }
    }

    private static string Judge(string p)
    {
        if (Phase.IsRephrase(p)) return "{\"action\":\"rephrase\",\"text\":\"REPHRASED\"}";
        if (Phase.IsRestate(p)) return "{\"restatement\":\"NEUTRALFACTS\"}";
        if (Phase.IsVerdict(p)) return "{\"answer\":\"V\",\"confidence\":\"low\",\"justification\":\"j\",\"uncertainty\":\"\"}";
        return "{}";
    }

    [Fact]
    public async Task An_overridden_phase_replaces_the_built_in_one()
    {
        var provider = TestFactory.Provider(
            _ => "{\"answer\":\"ANSWER\"}",
            _ => "{\"done\":true}",
            Judge);
        var observer = new RecordingObserver();

        RedactingPipeline? pipeline = null;
        var engine = new DebateEngine(
            TestFactory.Config(buildProfile: false),
            provider,
            _personas.Library,
            new WordTokenCounter(),
            observer,
            new QueueClarificationSource(),
            (ctx, obs, clar, tok) => pipeline = new RedactingPipeline(ctx, obs, clar, tok));

        await engine.RunQuestionAsync("a question", CancellationToken.None);

        Assert.Equal(1, pipeline!.RestateCalls);

        // The Critic received the subclass's value, not the Judge's restatement.
        var criticPrompt = provider.Critic.Calls.Single().LastUserMessage;
        Assert.Contains("REDACTED", criticPrompt);
        Assert.DoesNotContain("NEUTRALFACTS", criticPrompt);
    }
}
