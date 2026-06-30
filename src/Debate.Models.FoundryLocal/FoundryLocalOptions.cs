using Debate.Core;

namespace Debate.Models.FoundryLocal;

/// <summary>
/// Optional per-role caps on how many tokens a model may generate in a single reply.
/// A null entry means "no bound" for that role. The cap exists to stop runaway /
/// looping generations (small "thinking" models can loop until they exhaust the
/// context); it is per-role because roles differ in how much output they legitimately
/// need (a verdict is longer than a restatement).
/// </summary>
public sealed class RoleTokenLimits
{
    public int? Answerer { get; set; }
    public int? Critic { get; set; }
    public int? Judge { get; set; }

    public int? For(DebateRole role) => role switch
    {
        DebateRole.Answerer => Answerer,
        DebateRole.Critic => Critic,
        DebateRole.Judge => Judge,
        _ => null,
    };
}

/// <summary>
/// How a profile's model processes are kept resident.
/// </summary>
public enum ModelExecutionMode
{
    /// <summary>
    /// All model processes are started up front and stay resident (fast: no
    /// per-turn reload), at the cost of holding every model in memory at once.
    /// </summary>
    Parallel,

    /// <summary>
    /// At most one model process is resident at a time. Before serving a role,
    /// the other model processes are terminated, fully releasing their RAM/VRAM.
    /// Avoids out-of-memory at the cost of reloading a model whenever the active
    /// role changes.
    /// </summary>
    Sequential,

    /// <summary>
    /// The Judge model is kept resident at all times while the Answerer and Critic
    /// load one at a time (the others are terminated when a different non-Judge role
    /// runs). A middle ground: peak memory is the Judge plus one other model, but the
    /// frequently-used Judge (rephrase, restatement, verdict, profile) is never
    /// reloaded - far less thrashing than <see cref="Sequential"/>.
    /// </summary>
    SemiSequential,
}

/// <summary>
/// A named model profile: the role -> model alias lineup (Foundry Local catalog
/// aliases) plus how to run it. Values are the single source of truth in
/// configuration (<c>appsettings.json</c>); they are intentionally not hardcoded
/// here so the lineup is defined in exactly one place. An unset alias fails fast
/// at startup.
/// </summary>
public sealed class ModelProfile
{
    public string Answerer { get; set; } = string.Empty;
    public string Critic { get; set; } = string.Empty;
    public string Judge { get; set; } = string.Empty;

    /// <summary>
    /// Execution provider for this profile: <c>auto</c> (or empty) inherits the
    /// top-level <see cref="FoundryLocalOptions.ExecutionProvider"/>; otherwise
    /// <c>cpu</c>, <c>cuda</c>, or <c>webgpu</c>. Lets a profile bind its lineup to a
    /// device, e.g. a resource-light profile that runs on <c>cpu</c> to avoid GPU
    /// out-of-memory while a full-size profile uses the GPU.
    /// </summary>
    public string? ExecutionProvider { get; set; }

    /// <summary>
    /// How this profile's per-model processes are kept resident:
    /// <see cref="ModelExecutionMode.Parallel"/> (default) keeps every model loaded
    /// at once for speed; <see cref="ModelExecutionMode.Sequential"/> keeps only one
    /// model resident at a time (terminating the others), trading reload latency for
    /// much lower peak memory. A resource-light profile can pair a GPU provider with
    /// <c>Sequential</c> to fit on a smaller GPU.
    /// </summary>
    public ModelExecutionMode ExecutionMode { get; set; } = ModelExecutionMode.Parallel;

    /// <summary>
    /// Per-role cap on tokens generated per reply for this profile, or null (the
    /// default) for no bound on any role. See <see cref="RoleTokenLimits"/>.
    /// </summary>
    public RoleTokenLimits? MaxOutputTokens { get; set; }
}

/// <summary>
/// Configuration for the Foundry Local backend, bound from
/// <c>Debate:FoundryLocal</c>.
/// </summary>
public sealed class FoundryLocalOptions
{
    public const string SectionName = "Debate:FoundryLocal";

    /// <summary>Application name passed to Foundry Local (controls its data/log dirs).</summary>
    public string AppName { get; set; } = "debate";

    /// <summary>
    /// Directory where models are cached on disk. Leave empty to share the
    /// <c>foundry</c> CLI's cache (<c>~/.foundry/cache/models</c>), so models
    /// pre-downloaded via <c>foundry model download</c> are reused instead of
    /// re-downloaded into a separate, app-specific cache.
    /// </summary>
    public string ModelCacheDir { get; set; } = string.Empty;

    /// <summary>
    /// When true (default), discover and register available hardware execution
    /// providers (e.g. NVIDIA TensorRT-RTX, CUDA) at startup. This is required to
    /// load GPU/NPU-optimized model variants; without it only the CPU execution
    /// provider is available and GPU variants fail to load. EP binaries are large
    /// and downloaded once, then cached. Set false to force CPU-only execution.
    /// </summary>
    public bool RegisterExecutionProviders { get; set; } = true;

    /// <summary>
    /// Top-level execution provider override: <c>auto</c> (default), <c>cpu</c>,
    /// <c>cuda</c>, or <c>webgpu</c>. When set to a concrete provider it wins over the
    /// active profile's <see cref="ModelProfile.ExecutionProvider"/> (this is what
    /// <c>--execution-provider</c> sets); when <c>auto</c>/empty, the active profile's
    /// setting is used, falling back to fully automatic selection. Forcing <c>cpu</c>
    /// runs in system RAM and avoids GPU "out of memory" errors when several models must
    /// be resident at once (the debate keeps the Answerer, Critic, and Judge models
    /// loaded simultaneously), at the cost of speed.
    /// </summary>
    public string ExecutionProvider { get; set; } = "auto";

    /// <summary>Context window in tokens reported to stats for fill calculations.</summary>
    public int ContextSize { get; set; } = 8192;

    /// <summary>
    /// Per-request timeout, in seconds, for calls to the local OpenAI-compatible web
    /// service. Local model inference (especially the first, cold call on GPU) can far
    /// exceed the OpenAI client's 100-second default, surfacing as a "network timeout".
    /// Defaults to 10 minutes. Set to 0 or a negative value to disable the timeout.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// Bind address for the embedded OpenAI-compatible web service. Defaults to a
    /// random loopback port.
    /// </summary>
    public string WebServiceUrl { get; set; } = "http://127.0.0.1:0";

    /// <summary>
    /// When true (default), each model's host process is launched so it can own a
    /// console window; set false to create them with no window (<c>CreateNoWindow</c>).
    /// Note: model host processes always communicate over redirected stdin/stdout, so
    /// on a console host this primarily suppresses window creation rather than
    /// guaranteeing a distinct window per model. Honored on Windows; ignored elsewhere.
    /// </summary>
    public bool SeparateWindows { get; set; } = true;

    /// <summary>
    /// Name of the active model profile (a key in <see cref="Profiles"/>). A profile is
    /// a complete role -> model lineup, so users can switch between, e.g., a resource-light
    /// "small" set and a higher-quality "normal" set. Overridable from the command line
    /// (<c>--profile</c>). Defaults to "small".
    /// </summary>
    public string Profile { get; set; } = "small";

    /// <summary>
    /// Named model profiles (profile name -> role/model lineup). The active one is
    /// selected by <see cref="Profile"/>. Defined in configuration so the lineups live
    /// in exactly one place; a missing or empty active profile fails fast at startup.
    /// </summary>
    public Dictionary<string, ModelProfile> Profiles { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
