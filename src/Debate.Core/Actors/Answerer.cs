namespace Debate.Core.Actors;

/// <summary>The Answerer drafts and defends answers. Keeps full memory across rounds.</summary>
public sealed class Answerer : Actor
{
    public Answerer(DebateContext context) : base(context)
    {
    }

    public override DebateRole Role => DebateRole.Answerer;
    public override string DisplayName => "Answerer";
}
