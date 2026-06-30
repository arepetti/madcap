using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using FoundryConfiguration = Microsoft.AI.Foundry.Local.Configuration;

namespace Debate.Models.FoundryLocal;

/// <summary>
/// Reusable Foundry Local bootstrap: initializes the process-wide
/// <see cref="FoundryLocalManager"/>, registers hardware execution providers, and
/// selects/downloads/loads the best usable variant of a model alias for the
/// available providers.
///
/// This lives apart from any <see cref="Debate.Core.IModelProvider"/> so the
/// per-model host process (which loads exactly one model and serves it over
/// stdin/stdout) can reuse the same EP and variant-selection logic. Progress and
/// status are reported through an injected callback, letting the host route them to
/// stderr (keeping stdout a clean protocol channel).
/// </summary>
public sealed class FoundryModelLoader
{
    private readonly Action<string> _status;
    private readonly ILogger _logger;

    public FoundryModelLoader(Action<string>? status = null, ILogger? logger = null)
    {
        _status = status ?? (_ => { });
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Ensures the <see cref="FoundryLocalManager"/> singleton is created with the
    /// given app name and cache directory.
    /// </summary>
    public async Task<FoundryLocalManager> EnsureManagerAsync(
        string appName, string? modelCacheDir, string webServiceUrl, CancellationToken cancellationToken)
    {
        var cacheDir = ResolveModelCacheDir(modelCacheDir);
        _logger.LogInformation("Foundry Local model cache directory: {CacheDir}", cacheDir);

        var config = new FoundryConfiguration
        {
            AppName = appName,
            ModelCacheDir = cacheDir,
            Web = new FoundryConfiguration.WebService { Urls = webServiceUrl },
        };

        if (!FoundryLocalManager.IsInitialized)
        {
            await FoundryLocalManager.CreateAsync(config, NullLogger.Instance, cancellationToken)
                .ConfigureAwait(false);
        }

        return FoundryLocalManager.Instance;
    }

    /// <summary>
    /// Discovers and registers available hardware execution providers so GPU/NPU
    /// model variants can load, and returns the set of EPs that are registered
    /// afterward. Registration is skipped when disabled or when every discoverable
    /// EP is already registered. Best-effort: failures are surfaced as warnings
    /// rather than aborting, so a usable (CPU) configuration can still proceed.
    /// </summary>
    public async Task<IReadOnlySet<string>> RegisterExecutionProvidersAsync(
        FoundryLocalManager manager, bool registerExecutionProviders, string? forcedEp, CancellationToken cancellationToken)
    {
        // No need to download/register GPU EPs when the run is pinned to CPU.
        bool cpuOnly = string.Equals(forcedEp, CpuExecutionProvider, StringComparison.OrdinalIgnoreCase);

        if (registerExecutionProviders && !cpuOnly)
        {
            var pending = manager.DiscoverEps().Where(ep => !ep.IsRegistered).Select(ep => ep.Name).ToArray();
            if (pending.Length > 0)
            {
                _status($"Registering execution provider(s): {string.Join(", ", pending)}...");

                try
                {
                    int lastReported = -1;
                    var result = await manager.DownloadAndRegisterEpsAsync(
                        (epName, percent) =>
                        {
                            int bucket = (int)(percent / 10) * 10;
                            if (bucket > lastReported && bucket < 100)
                            {
                                lastReported = bucket;
                                _status($"  execution provider '{epName}': {percent:F0}%");
                            }
                        },
                        cancellationToken).ConfigureAwait(false);

                    if (!result.Success)
                    {
                        _logger.LogWarning(
                            "Some execution providers failed to register: {Failed}. Status: {Status}",
                            string.Join(", ", result.FailedEps),
                            result.Status);
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _logger.LogWarning(e, "Execution provider registration failed; continuing with available EPs.");
                    _status("Execution provider registration failed; continuing with available providers.");
                }
            }
        }

        var registered = manager.DiscoverEps()
            .Where(ep => ep.IsRegistered)
            .Select(ep => ep.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _status($"Execution providers available: {string.Join(", ", registered.OrderBy(n => n))}.");
        return registered;
    }

    /// <summary>
    /// Resolves the on-disk model cache directory. Honors an explicit value;
    /// otherwise points at the <c>foundry</c> CLI's default cache so pre-downloaded
    /// models are reused instead of being fetched again into an app-specific location.
    /// </summary>
    public static string ResolveModelCacheDir(string? modelCacheDir)
    {
        if (!string.IsNullOrWhiteSpace(modelCacheDir))
        {
            return Environment.ExpandEnvironmentVariables(modelCacheDir);
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".foundry", "cache", "models");
    }

    // Maps a model-variant id token (Foundry's catalog naming convention) to the
    // execution provider that variant requires, in order of preference (most capable
    // backend first). A variant is only usable if its EP is registered; a cached
    // variant whose EP is missing (e.g. a TensorRT-RTX build with no NvTensorRTRTX EP)
    // cannot load and must be skipped in favor of a compatible one.
    private static readonly (string Token, string Ep)[] EpPreference =
    [
        ("trtrtx", "NvTensorRTRTXExecutionProvider"),
        ("cuda", "CUDAExecutionProvider"),
        ("openvino", "OpenVINOExecutionProvider"),
        ("generic-gpu", "WebGpuExecutionProvider"),
        ("generic-cpu", CpuExecutionProvider),
    ];

    public const string CpuExecutionProvider = "CPUExecutionProvider";

    // Friendly names accepted in the ExecutionProvider option, mapped to EP identifiers.
    private static readonly Dictionary<string, string> ExecutionProviderAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["cpu"] = CpuExecutionProvider,
            ["cuda"] = "CUDAExecutionProvider",
            ["gpu"] = "CUDAExecutionProvider",
            ["webgpu"] = "WebGpuExecutionProvider",
            ["trtrtx"] = "NvTensorRTRTXExecutionProvider",
            ["openvino"] = "OpenVINOExecutionProvider",
        };

    /// <summary>
    /// Maps a friendly execution-provider name to an EP identifier, treating empty or
    /// <c>auto</c> as "no preference" (null).
    /// </summary>
    public static string? MapExecutionProvider(string? value)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v) || v.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return ExecutionProviderAliases.TryGetValue(v, out var ep) ? ep : v;
    }

    /// <summary>
    /// Computes the execution providers that variant selection may use. A null forced
    /// provider returns everything registered; a forced provider returns just that one
    /// (CPU is always permitted). Falls back to all registered providers with a warning
    /// if a forced GPU provider is not actually available.
    /// </summary>
    public IReadOnlySet<string> ResolveEffectiveEps(string? forcedEp, IReadOnlySet<string> registeredEps)
    {
        if (forcedEp is null)
        {
            return registeredEps;
        }

        bool isCpu = string.Equals(forcedEp, CpuExecutionProvider, StringComparison.OrdinalIgnoreCase);
        if (isCpu || registeredEps.Contains(forcedEp))
        {
            _status($"Forcing execution provider: {forcedEp}.");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { forcedEp };
        }

        _status(
            $"Requested execution provider '{forcedEp}' is not registered; " +
            $"using all available providers ({string.Join(", ", registeredEps.OrderBy(n => n))}).");
        return registeredEps;
    }

    /// <summary>
    /// Resolves a model alias against the live catalog, failing fast with a helpful
    /// message if the alias does not exist.
    /// </summary>
    public async Task<IModel> GetModelAsync(FoundryLocalManager manager, string alias, CancellationToken cancellationToken)
    {
        var catalog = await manager.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        return await catalog.GetModelAsync(alias, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Foundry Local model alias '{alias}' was not found in the catalog. " +
                $"Run 'foundry model list' to see available aliases and update configuration.");
    }

    /// <summary>
    /// Selects, downloads (if needed), and loads the best usable variant of a model for
    /// the given execution providers. Variants are ordered to (a) reuse already-cached
    /// files first and (b) prefer more capable hardware backends, while excluding
    /// variants whose required EP is not registered. Each candidate is tried in turn so
    /// an unexpected load failure falls through to the next instead of aborting; a CPU
    /// variant is always a candidate, guaranteeing a usable fallback.
    /// </summary>
    public async Task<IModel> LoadCompatibleVariantAsync(
        IModel model, string alias, IReadOnlySet<string> registeredEps, CancellationToken cancellationToken)
    {
        var candidates = await BuildCandidatesAsync(model, registeredEps, cancellationToken).ConfigureAwait(false);

        Exception? lastError = null;
        foreach (var candidate in candidates)
        {
            try
            {
                await EnsureDownloadedAndLoadedAsync(candidate, alias, cancellationToken).ConfigureAwait(false);
                return candidate;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                lastError = e;
                var firstLine = e.Message.Split('\n', '\r')[0];
                _logger.LogWarning(
                    e, "Variant '{Id}' for alias '{Alias}' could not be loaded; trying next.", candidate.Id, alias);
                _status($"  variant '{candidate.Id}' unavailable ({firstLine}); trying another...");
            }
        }

        throw new InvalidOperationException(
            $"No loadable variant found for model alias '{alias}' with the available execution providers " +
            $"({string.Join(", ", registeredEps)}). Enable additional execution providers via " +
            $"'{FoundryLocalOptions.SectionName}:RegisterExecutionProviders', or choose a different model.",
            lastError);
    }

    /// <summary>
    /// Downloads a variant if it is not already cached (reporting coarse, deduplicated
    /// progress), then loads it. The progress lambda lives here so the selection loop stays
    /// a flat try-the-next-candidate.
    /// </summary>
    private async Task EnsureDownloadedAndLoadedAsync(IModel candidate, string alias, CancellationToken cancellationToken)
    {
        if (!await candidate.IsCachedAsync(cancellationToken).ConfigureAwait(false))
        {
            int lastReported = -1;
            _status($"  downloading '{alias}' variant '{candidate.Id}'...");
            await candidate.DownloadAsync(
                percent =>
                {
                    int bucket = (int)(percent / 10) * 10;
                    if (bucket > lastReported && bucket < 100)
                    {
                        lastReported = bucket;
                        _status($"  downloading '{alias}': {percent:F0}%");
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }

        await candidate.LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the ordered candidate variant list for <see cref="LoadCompatibleVariantAsync"/>:
    /// variants whose required EP is registered, ordered cached-first then by hardware
    /// preference. Variants with an unrecognized naming convention are kept as a
    /// lowest-priority fallback so a usable model can still be found.
    /// </summary>
    private static async Task<List<IModel>> BuildCandidatesAsync(
        IModel model, IReadOnlySet<string> registeredEps, CancellationToken cancellationToken)
    {
        var variants = new List<IModel>();
        foreach (var v in model.Variants)
        {
            variants.Add(v);
        }

        if (variants.Count == 0)
        {
            variants.Add(model);
        }

        var scored = new List<(IModel Variant, int Tier, bool Cached)>();
        foreach (var v in variants)
        {
            int tier = ClassifyTier(v.Id, registeredEps);
            if (tier < 0)
            {
                continue;
            }

            bool cached = await v.IsCachedAsync(cancellationToken).ConfigureAwait(false);
            scored.Add((v, tier, cached));
        }

        return scored
            .OrderBy(s => s.Cached ? 0 : 1)
            .ThenBy(s => s.Tier)
            .Select(s => s.Variant)
            .ToList();
    }

    /// <summary>
    /// Classifies a variant id into a preference tier (lower is better). Returns -1 when
    /// the variant's required execution provider is known but not registered (unusable),
    /// and a lowest-priority tier when the naming is unrecognized.
    /// </summary>
    private static int ClassifyTier(string id, IReadOnlySet<string> registeredEps)
    {
        for (int i = 0; i < EpPreference.Length; i++)
        {
            var (token, ep) = EpPreference[i];
            if (id.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return registeredEps.Contains(ep) ? i : -1;
            }
        }

        return EpPreference.Length;
    }
}
