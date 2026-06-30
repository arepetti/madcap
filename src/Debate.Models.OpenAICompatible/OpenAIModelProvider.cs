using System.ClientModel;
using Debate.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Debate.Models.OpenAICompatible;

/// <summary>
/// Remote model backend over any OpenAI-compatible endpoint. Builds a per-role
/// <see cref="IChatClient"/> from the official OpenAI client pointed at the
/// configured endpoint. To the debate algorithm this is indistinguishable from
/// the local Foundry provider; both are just <see cref="IModelProvider"/>.
/// </summary>
public sealed class OpenAIModelProvider : IModelProvider
{
    private readonly OpenAICompatibleOptions _options;
    private readonly Lazy<OpenAIClient> _client;

    public OpenAIModelProvider(IOptions<OpenAICompatibleOptions> options)
    {
        _options = options.Value;
        _client = new Lazy<OpenAIClient>(CreateClient);
    }

    public int EffectiveContextSize => _options.ContextSize;

    public string ModelName(DebateRole role) => ModelFor(role);

    // The remote backend has no per-role generation cap (hosted models don't loop the
    // way small local "thinking" models do); leave generation unbounded.
    public int? MaxOutputTokens(DebateRole role) => null;

    public IChatClient GetClient(DebateRole role) =>
        _client.Value.GetChatClient(ModelFor(role)).AsIChatClient();

    private OpenAIClient CreateClient()
    {
        var credential = new ApiKeyCredential(_options.ResolveApiKey());
        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            clientOptions.Endpoint = new Uri(_options.Endpoint);
        }

        return new OpenAIClient(credential, clientOptions);
    }

    private string ModelFor(DebateRole role)
    {
        var model = role switch
        {
            DebateRole.Answerer => _options.Models.Answerer,
            DebateRole.Critic => _options.Models.Critic,
            DebateRole.Judge => _options.Models.Judge,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException(
                $"No remote model configured for the {role} role. " +
                $"Set '{OpenAICompatibleOptions.SectionName}:Models:{role}' in appsettings.json.");
        }

        return model;
    }
}
