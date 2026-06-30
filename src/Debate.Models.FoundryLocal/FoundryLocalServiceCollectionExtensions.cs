using Debate.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Debate.Models.FoundryLocal;

public static class FoundryLocalServiceCollectionExtensions
{
    /// <summary>
    /// Register the Foundry Local backend as the <see cref="IModelProvider"/> and
    /// wire its bootstrap as a hosted service. Options bind from
    /// <c>Debate:FoundryLocal</c>.
    /// </summary>
    public static IServiceCollection AddFoundryLocalProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<FoundryLocalOptions>(configuration.GetSection(FoundryLocalOptions.SectionName));
        services.AddSingleton<ProcessModelProvider>();
        services.AddSingleton<IModelProvider>(sp => sp.GetRequiredService<ProcessModelProvider>());
        services.AddHostedService(sp => sp.GetRequiredService<ProcessModelProvider>());
        return services;
    }
}
