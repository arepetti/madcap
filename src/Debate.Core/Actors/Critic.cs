namespace Debate.Core.Actors;

/// <summary>
/// The Critic stress-tests the Answerer's position. Rebuilt fresh each round so
/// its system prompt is primed with the latest prior rephrased questions and the
/// active Answerer profile. It only ever sees the Judge's restatements.
/// </summary>
public sealed class Critic : Actor
{
    public Critic(DebateContext context) : base(context)
    {
    }

    public override DebateRole Role => DebateRole.Critic;
    public override string DisplayName => "Critic";

    protected override string RenderSystemPrompt()
    {
        var template = Context.Personas.Load(Context.Config.PersonaName, PersonaToken);
        return template
            .Replace("{prior_rephrased}", PersonaLibrary.RenderPriorRephrased(Context.PriorRephrased))
            .Replace("{answerer_profile}", PersonaLibrary.RenderAnswererProfile(Context.ActiveProfile()));
    }
}
