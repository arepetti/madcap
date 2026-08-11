namespace Debate.Core;

/// <summary>
/// One OS process a backend uses to serve models, for the <c>!stats</c> display:
/// its process id, a human-readable label (typically the model alias it loaded), the
/// debate role(s) routed to it, and whether it is still alive.
/// </summary>
public sealed record BackendProcessInfo(
    int Pid,
    string Label,
    IReadOnlyList<DebateRole> Roles,
    bool Running);

/// <summary>
/// Optional capability for backends that run models in separate processes and can
/// report on them. Implemented alongside <see cref="IModelProvider"/>; hosts test for
/// it rather than for a concrete backend type, so a new backend can join the display
/// without the CLI knowing it exists.
/// </summary>
public interface IBackendDiagnostics
{
    /// <summary>
    /// A snapshot of the backend's model processes, or null if the backend is busy
    /// (e.g. loading a model) and could not produce one promptly. This is display-only
    /// information, so it must never block the caller: returning null lets the host say
    /// "busy" instead of freezing the prompt behind a multi-minute model load.
    /// </summary>
    IReadOnlyList<BackendProcessInfo>? TryDescribeProcesses();
}

/// <summary>
/// Optional capability for backends with a meaningful warm-up step (downloading and
/// loading models, registering execution providers) that can be run ahead of time so
/// the first interactive use is fast.
/// </summary>
public interface IPrefetchable
{
    /// <summary>
    /// Forces every model the backend will need to be fetched and loaded once, then
    /// released. Used by <c>--prefetch</c> during setup.
    /// </summary>
    Task PrefetchAsync(CancellationToken cancellationToken);
}
