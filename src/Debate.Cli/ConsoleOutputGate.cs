namespace Debate.Cli;

/// <summary>
/// Serializes console writes and lets the foreground temporarily suspend background
/// output while it is reading a line from the user.
///
/// All console writes that may race with user input (the debate observer, background
/// model-loading logs) go through <see cref="Write"/>. While a <see cref="Suspend"/>
/// scope is open, those writes are queued instead of printed and then flushed, in order,
/// when the scope is disposed — so a background log line can never land in the middle of
/// an input prompt. Outside a suspend scope, <see cref="Write"/> prints immediately but
/// still under the lock, so two threads can't interleave a half-written line.
///
/// Suspend scopes are not reentrant; the app opens at most one at a time (around each
/// console read).
/// </summary>
public sealed class ConsoleOutputGate
{
    private readonly object _lock = new();
    private readonly List<Action> _buffer = new();
    private bool _suspended;

    /// <summary>
    /// Perform a console write. Printed immediately unless output is suspended, in which
    /// case it is queued and flushed when the current <see cref="Suspend"/> scope ends.
    /// </summary>
    public void Write(Action render)
    {
        lock (_lock)
        {
            if (_suspended)
            {
                _buffer.Add(render);
            }
            else
            {
                render();
            }
        }
    }

    /// <summary>
    /// Suspend background output until the returned scope is disposed. Use around a
    /// console read so queued lines flush only after the user has finished typing.
    /// </summary>
    public IDisposable Suspend()
    {
        lock (_lock)
        {
            _suspended = true;
        }

        return new Scope(this);
    }

    private void Resume()
    {
        lock (_lock)
        {
            _suspended = false;
            foreach (var render in _buffer)
            {
                try
                {
                    render();
                }
                catch
                {
                    // A rendering failure must not break the flush of the remaining lines.
                }
            }

            _buffer.Clear();
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly ConsoleOutputGate _gate;
        private bool _disposed;

        public Scope(ConsoleOutputGate gate) => _gate = gate;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _gate.Resume();
        }
    }
}
