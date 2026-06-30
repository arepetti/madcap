using Debate.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Debate.Models.OpenAICompatible;

public static class OpenAICompatibleServiceCollectionExtensions
{
    /// <summary>
    /// Register a generic OpenAI-compatible remote backend as the
    /// <see cref="IModelProvider"/>. Options bind from <c>Debate:Remote</c>.
    /// </summary>
    public static IServiceCollection AddOpenAICompatibleProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<OpenAICompatibleOptions>(configuration.GetSection(OpenAICompatibleOptions.SectionName));
        services.AddSingleton<IModelProvider, OpenAIModelProvider>();
        return services;
    }
}
