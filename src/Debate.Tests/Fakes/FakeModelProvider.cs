using Debate.Core;
using Microsoft.Extensions.AI;

namespace Debate.Tests.Fakes;

/// <summary>
/// An <see cref="IModelProvider"/> backed by one <see cref="ScriptedChatClient"/> per
/// role. All four Judge contexts share the single Judge client (as in production, where
/// they are all the same model), so routing replies by prompt content is what keeps
/// their turns apart.
/// </summary>
public sealed class FakeModelProvider : IModelProvider
{
    private readonly Dictionary<DebateRole, ScriptedChatClient> _clients;

    public FakeModelProvider(
        ScriptedChatClient answerer, ScriptedChatClient critic, ScriptedChatClient judge)
    {
        _clients = new Dictionary<DebateRole, ScriptedChatClient>
        {
            [DebateRole.Answerer] = answerer,
            [DebateRole.Critic] = critic,
            [DebateRole.Judge] = judge,
        };
    }

    public ScriptedChatClient Answerer => _clients[DebateRole.Answerer];
    public ScriptedChatClient Critic => _clients[DebateRole.Critic];
    public ScriptedChatClient Judge => _clients[DebateRole.Judge];

    /// <summary>Optional per-role output cap, surfaced to assert it reaches ChatOptions.</summary>
    public Dictionary<DebateRole, int?> OutputCaps { get; } = new();

    public IChatClient GetClient(DebateRole role) => _clients[role];

    public string ModelName(DebateRole role) => $"fake-{role}".ToLowerInvariant();

    public int EffectiveContextSize => 8192;

    public int? MaxOutputTokens(DebateRole role) =>
        OutputCaps.TryGetValue(role, out var cap) ? cap : null;
}
