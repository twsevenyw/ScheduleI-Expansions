using Expansions.Core.Logging;
using Object = UnityEngine.Object;

namespace Expansions.Core;

/// <summary>
/// Cleanup scope for a single enable/disable cycle. Anything a module creates while enabled should
/// be registered here, so disabling it leaves no residue and re-enabling starts from a clean slate.
/// <para>
/// A fresh instance is handed out on every enable; the previous one is disposed on disable.
/// Cleanup runs in reverse registration order and one failure never blocks the rest.
/// </para>
/// </summary>
public sealed class ModuleLifetime : IDisposable
{
    private readonly List<Action> _cleanup = new();
    private readonly ModuleLogger _log;

    internal ModuleLifetime(ModuleLogger log) => _log = log;

    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Registers work to undo on disable. Typical use is unsubscribing an event:
    /// <c>Lifetime.OnDispose(() =&gt; Something.Changed -= Handler);</c>
    /// Runs straight away if this scope has already been disposed.
    /// </summary>
    public void OnDispose(Action cleanup)
    {
        if (cleanup is null)
            throw new ArgumentNullException(nameof(cleanup));

        if (IsDisposed)
        {
            Run(cleanup);
            return;
        }

        _cleanup.Add(cleanup);
    }

    /// <summary>Disposes <paramref name="disposable"/> on disable and returns it for chaining.</summary>
    public T Add<T>(T disposable) where T : IDisposable
    {
        OnDispose(() => disposable.Dispose());
        return disposable;
    }

    /// <summary>Destroys a spawned Unity object on disable and returns it for chaining.</summary>
    public T Track<T>(T obj) where T : Object
    {
        OnDispose(() => Object.Destroy(obj));
        return obj;
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;

        for (var i = _cleanup.Count - 1; i >= 0; i--)
            Run(_cleanup[i]);

        _cleanup.Clear();
    }

    private void Run(Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception ex)
        {
            _log.Error("A cleanup action threw during teardown; continuing.", ex);
        }
    }
}
