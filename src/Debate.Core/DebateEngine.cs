using Debate.Core.Actors;
using Microsoft.Extensions.AI;

namespace Debate.Core;

/// <summary>
/// The reusable, host-agnostic entry point to a debate session. Wraps the
/// <see cref="DebateContext"/> and <see cref="DebatePipeline"/> and exposes a
/// small API any host (console, GUI, web) can drive: ask a question, clear the
/// session, and read state for display. Holds no console/printing concern.
/// </summary>
public sealed class DebateEngine
{
    private readonly DebateContext _context;
    private readonly DebatePipeline _pipeline;
    private readonly ITokenCounter _tokens;

    /// <param name="pipelineFactory">
    /// Optional hook for supplying a <see cref="DebatePipeline"/> subclass that overrides
    /// one or more phases. Defaults to the standard pipeline.
    /// </param>
    public DebateEngine(
        SessionConfig config,
        IModelProvider provider,
        PersonaLibrary personas,
        ITokenCounter tokens,
        IDebateObserver observer,
        IClarificationSource clarifications,
        Func<DebateContext, IDebateObserver, IClarificationSource, ITokenCounter, DebatePipeline>? pipelineFactory = null)
    {
        _context = new DebateContext(config, provider, personas);
        _pipeline = pipelineFactory is null
            ? new DebatePipeline(_context, observer, clarifications, tokens)
            : pipelineFactory(_context, observer, clarifications, tokens);
        _tokens = tokens;
    }

    public SessionConfig Config => _context.Config;
    public string PersonaDirectory => _context.Personas.Directory;

    /// <summary>Run one full question through the pipeline (rephrase, debate, verdict, profile).</summary>
    public Task RunQuestionAsync(string question, CancellationToken cancellationToken) =>
        _pipeline.RunAsync(question, cancellationToken);

    /// <summary>Wipe Answerer memory, history, profile, and stats.</summary>
    public Task ClearSessionAsync() => _context.ClearSessionAsync();

    public IReadOnlyList<PersonaRoleInfo> GetPersonaInfo()
    {
        var result = new List<PersonaRoleInfo>();
        foreach (var actor in _context.AllActors())
        {
            result.Add(new PersonaRoleInfo(
                actor.DisplayName,
                actor.Role,
                _context.Provider.ModelName(actor.Role),
                _context.Config.TemperatureFor(actor.Role),
                _context.Personas.ResolvePersonaPath(_context.Config.PersonaName, actor.PersonaToken)));
        }

        return result;
    }

    /// <summary>
    /// Snapshot, per actor, of the rendered system prompt and current conversation
    /// buffer — exactly what each actor receives. Drives the <c>!context</c> command.
    /// </summary>
    public IReadOnlyList<ActorContextView> GetActorContexts()
    {
        var result = new List<ActorContextView>();
        foreach (var actor in _context.AllActors())
        {
            var messages = actor.Messages
                .Where(m => m.Role != ChatRole.System)
                .Select(m => new ActorMessageView(m.Role.ToString(), m.Text ?? string.Empty))
                .ToList();

            result.Add(new ActorContextView(
                actor.Role,
                actor.PersonaToken,
                actor.DisplayName,
                actor.PreviewSystemPrompt(),
                messages));
        }

        return result;
    }

    public StatsSnapshot GetStatsSnapshot()
    {
        int contextSize = _context.Provider.EffectiveContextSize;

        var actors = new List<ActorContextInfo>();
        foreach (var actor in _context.AllActors())
        {
            int tokens = EstimateTokens(actor);
            actors.Add(new ActorContextInfo(
                actor.DisplayName,
                actor.Role,
                _context.Provider.ModelName(actor.Role),
                _context.Config.TemperatureFor(actor.Role),
                tokens,
                actor.IsBuilt));
        }

        var active = _context.ProfileEntries
            .Where(e => e.Count >= Profile.MinCountToSurface)
            .Select(e => new ProfileEntryView(e.Text, e.Count))
            .ToList();

        var pending = _context.ProfileEntries
            .Where(e => e.Count < Profile.MinCountToSurface)
            .Select(e => new ProfileEntryView(e.Text, e.Count))
            .ToList();

        return new StatsSnapshot(
            _tokens.Method,
            contextSize,
            _context.Config.BuildProfile,
            _context.Stats,
            _context.PriorRephrased.ToList(),
            active,
            pending,
            actors);
    }

    private int EstimateTokens(Actor actor)
    {
        if (!actor.IsBuilt)
        {
            return 0;
        }

        int total = 0;
        foreach (var message in actor.Messages)
        {
            var text = message.Text;
            if (!string.IsNullOrEmpty(text))
            {
                total += _tokens.Count(text);
            }
        }

        return total;
    }
}
