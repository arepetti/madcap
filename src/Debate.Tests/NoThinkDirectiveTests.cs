using Debate.Core;
using Debate.Tests.Support;

namespace Debate.Tests;

/// <summary>
/// <c>/no_think</c> is a Qwen3 soft switch. On other model families it is an unexplained
/// command-looking token, which is the opposite of what a JSON-only prompt wants, so it
/// must be applied per model rather than baked into the persona files.
/// </summary>
public sealed class NoThinkDirectiveTests : IDisposable
{
    private readonly TempPersonas _personas = new();

    public void Dispose() => _personas.Dispose();

    private string ArbiterSystemPrompt(string judgeModel)
    {
        var provider = TestFactory.InertProvider();
        provider.ModelNames[DebateRole.Judge] = judgeModel;
        var ctx = new DebateContext(TestFactory.Config(), provider, _personas.Library);
        return ctx.JudgeArbiter.PreviewSystemPrompt();
    }

    [Fact]
    public void A_qwen_judge_receives_the_directive()
    {
        var prompt = ArbiterSystemPrompt("qwen3-4b");

        Assert.Contains(DebatePrompts.NoThinkDirective, prompt);
        Assert.DoesNotContain(DebatePrompts.NoThinkPlaceholder, prompt);
    }

    [Fact]
    public void Any_other_judge_receives_neither_the_directive_nor_the_placeholder()
    {
        var prompt = ArbiterSystemPrompt("gpt-4o");

        Assert.DoesNotContain(DebatePrompts.NoThinkDirective, prompt);
        Assert.DoesNotContain(DebatePrompts.NoThinkPlaceholder, prompt);
        Assert.EndsWith("PERSONA:judge-arbiter", prompt);
    }
}
