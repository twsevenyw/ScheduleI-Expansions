using UnityEngine;

namespace Expansions.Core.Game;

/// <summary>
/// A value recomputed at most once every <c>ttl</c> seconds.
/// <para>
/// The Actions page re-reads every action's availability on every frame the screen is open, which is
/// what lets a row grey itself out the moment a save unloads. Some of those answers cost a
/// <c>Resources.FindObjectsOfTypeAll</c> heap walk, a walk of the whole NPC registry, or a directory
/// listing — none of which can run sixty times a second. Caching the expensive primitive rather than the
/// predicate keeps one source of truth and speeds up the action bodies too.
/// </para>
/// <para>
/// Unscaled time, so the cache does not freeze when the game is paused or time-scaled.
/// </para>
/// </summary>
internal sealed class TimedCache<T>
    where T : class
{
    private readonly Func<T> _produce;
    private readonly float _ttl;

    private T? _value;
    private float _stamp = float.NegativeInfinity;

    internal TimedCache(float ttlSeconds, Func<T> produce)
    {
        _ttl = ttlSeconds;
        _produce = produce;
    }

    internal T Value
    {
        get
        {
            var now = Now();
            if (_value is not null && now - _stamp < _ttl)
                return _value;

            _value = _produce();
            _stamp = now;
            return _value;
        }
    }

    /// <summary>Forces the next read to recompute — after an action that changed what the value describes.</summary>
    internal void Invalidate() => _stamp = float.NegativeInfinity;

    private static float Now()
    {
        try
        {
            return Time.unscaledTime;
        }
        catch
        {
            // Off the Unity main thread there is no time; recompute rather than serve a stale answer.
            return float.PositiveInfinity;
        }
    }
}
