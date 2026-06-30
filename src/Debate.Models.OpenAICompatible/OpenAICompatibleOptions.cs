namespace Debate.Models.OpenAICompatible;

/// <summary>
/// Role -> model name mapping for the remote endpoint. Values are the single
/// source of truth in configuration (<c>appsettings.json</c>); not hardcoded
/// here so the lineup is defined in exactly one place. An unset model fails fast.
/// </summary>
public sealed class RemoteRoleModelMap
{
    public string Answerer { get; set; } = string.Empty;
    public string Critic { get; set; } = string.Empty;
    public string Judge { get; set; } = string.Empty;
}

/// <summary>
/// Configuration for a generic OpenAI-compatible remote endpoint (OpenAI, Azure
/// AI Foundry model endpoints, self-hosted gateways, ...), bound from
/// <c>Debate:Remote</c>.
/// </summary>
public sealed class OpenAICompatibleOptions
{
    public const string SectionName = "Debate:Remote";

    /// <summary>
    /// Base URL of the OpenAI-compatible API (its "/v1" root). Leave empty to use
    /// the official OpenAI default endpoint.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Environment variable holding the API key. Preferred over <see cref="ApiKey"/>
    /// so secrets stay out of config files.
    /// </summary>
    public string? ApiKeyEnvVar { get; set; } = "DEBATE_API_KEY";

    /// <summary>Literal API key. Used only if <see cref="ApiKeyEnvVar"/> is unset or empty.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Context window in tokens reported to stats for fill calculations.</summary>
    public int ContextSize { get; set; } = 128_000;

    public RemoteRoleModelMap Models { get; set; } = new();

    public string ResolveApiKey()
    {
        if (!string.IsNullOrEmpty(ApiKeyEnvVar))
        {
            var fromEnv = Environment.GetEnvironmentVariable(ApiKeyEnvVar);
            if (!string.IsNullOrEmpty(fromEnv))
            {
                return fromEnv;
            }
        }

        if (!string.IsNullOrEmpty(ApiKey))
        {
            return ApiKey;
        }

        throw new InvalidOperationException(
            "No API key for the remote provider. Set the environment variable " +
            $"'{ApiKeyEnvVar}' or 'Debate:Remote:ApiKey' in configuration.");
    }
}
