using Microsoft.Extensions.AI;

namespace Debate.Core;

/// <summary>
/// The single seam between the debate algorithm and whatever runs the models.
///
/// Implementations decide whether a role is served by a local Foundry model, a
/// hosted/cloud endpoint, or anything else. The algorithm only ever asks for an
/// <see cref="IChatClient"/> by <see cref="DebateRole"/> and never learns where
/// it runs. Swapping local for cloud is a matter of registering a different
/// implementation; no algorithm code changes.
/// </summary>
public interface IModelProvider
{
    /// <summary>
    /// The chat client for a role. Local vs cloud is invisible to the caller.
    /// </summary>
    IChatClient GetClient(DebateRole role);

    /// <summary>
    /// A human-readable model name for a role, shown in stats and the personas
    /// listing. Has no effect on behaviour.
    /// </summary>
    string ModelName(DebateRole role);

    /// <summary>
    /// Context window in tokens used by stats to compute fill percentages.
    /// For backends where this is per-model, pick the smallest in play.
    /// </summary>
    int EffectiveContextSize { get; }

    /// <summary>
    /// Optional cap on the number of tokens a role may generate in a single reply, or
    /// null for no bound. Applied to <c>ChatOptions.MaxOutputTokens</c> per call. Used
    /// to stop runaway / looping generations (small "thinking" models can loop until
    /// they exhaust the context). Per-role because roles differ in how much output they
    /// legitimately need.
    /// </summary>
    int? MaxOutputTokens(DebateRole role);
}
