namespace Debate.Core.Actors;

/// <summary>
/// The Judge plays four single-job roles, each backed by the Judge model but kept
/// in its own conversation buffer so the jobs never share context they shouldn't:
///
/// <list type="bullet">
/// <item><see cref="JudgeRephraser"/> — turns the user's question into a neutral
/// one (Phase 1). Never sees the debate; only the prior rephrased questions and the
/// verdicts they reached, so follow-up questions stay consistent.</item>
/// <item><see cref="JudgeRestater"/> — the channel constraint. The only context that
/// ever ingests the raw Answerer reply; it emits neutral restatements the Critic
/// sees.</item>
/// <item><see cref="JudgeArbiter"/> — issues the verdict from the rephrased-only
/// transcript (restatements + objections); never sees raw Answerer text.</item>
/// <item><see cref="JudgeProfiler"/> — extracts at most one Answerer tendency from
/// the Critic's objections alone.</item>
/// </list>
///
/// All four are rebuilt fresh each round; none carries memory across rounds except
/// through the session state rendered into their system prompts.
/// </summary>
public sealed class JudgeRephraser : Actor
{
    public JudgeRephraser(DebateContext context) : base(context)
    {
    }

    public override DebateRole Role => DebateRole.Judge;
    public override string DisplayName => "Judge (rephraser)";
    public override string PersonaToken => PersonaTokens.JudgeRephraser;

    protected override string RenderSystemPrompt()
    {
        var template = Context.Personas.Load(Context.Config.PersonaName, PersonaToken);
        return template.Replace(
            "{prior_exchanges}",
            PersonaLibrary.RenderPriorExchanges(Context.PriorRephrased, Context.PriorVerdicts));
    }
}

/// <inheritdoc cref="JudgeRephraser"/>
public sealed class JudgeRestater : Actor
{
    public JudgeRestater(DebateContext context) : base(context)
    {
    }

    public override DebateRole Role => DebateRole.Judge;
    public override string DisplayName => "Judge (restater)";
    public override string PersonaToken => PersonaTokens.JudgeRestater;
}

/// <inheritdoc cref="JudgeRephraser"/>
public sealed class JudgeArbiter : Actor
{
    public JudgeArbiter(DebateContext context) : base(context)
    {
    }

    public override DebateRole Role => DebateRole.Judge;
    public override string DisplayName => "Judge (arbiter)";
    public override string PersonaToken => PersonaTokens.JudgeArbiter;
}

/// <inheritdoc cref="JudgeRephraser"/>
public sealed class JudgeProfiler : Actor
{
    public JudgeProfiler(DebateContext context) : base(context)
    {
    }

    public override DebateRole Role => DebateRole.Judge;
    public override string DisplayName => "Judge (profiler)";
    public override string PersonaToken => PersonaTokens.JudgeProfiler;
}
