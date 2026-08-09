namespace Expansions.Core.Diagnostics;

/// <summary>
/// Process-wide probe list. Core ships the cross-cutting probes; each feature module adds its own
/// on enable and drops them on disable:
/// <code>Lifetime.Add(ProbeRegistry.Register(myProbe));</code>
/// </summary>
public static class ProbeRegistry
{
    private static readonly Dictionary<string, IProbe> ById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    private static IProbe[] _ordered = Array.Empty<IProbe>();

    public static int Count => _ordered.Length;

    /// <summary>Ordered by area then id, so two runs of the same set produce comparable reports.</summary>
    public static IReadOnlyList<IProbe> Probes => _ordered;

    /// <summary>
    /// Adds a probe. Returns a token that removes it again; a duplicate id is rejected with a
    /// warning and an inert token rather than throwing, so one bad module cannot break the harness.
    /// </summary>
    public static IDisposable Register(IProbe probe)
    {
        if (probe is null)
            throw new ArgumentNullException(nameof(probe));

        if (string.IsNullOrWhiteSpace(probe.Id))
            throw new ArgumentException("Probe Id must be a non-empty, stable string.", nameof(probe));

        lock (Gate)
        {
            if (ById.TryGetValue(probe.Id, out var existing))
            {
                ExpansionHost.Log.Warn(
                    $"Probe id '{probe.Id}' is already held by '{existing.Name}'; ignoring the duplicate '{probe.Name}'.");
                return Registration.Inert;
            }

            ById[probe.Id] = probe;
            Rebuild();
        }

        return new Registration(probe.Id);
    }

    public static bool Unregister(string id)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        lock (Gate)
        {
            if (!ById.Remove(id))
                return false;

            Rebuild();
            return true;
        }
    }

    public static IProbe? Find(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        lock (Gate)
            return ById.TryGetValue(id, out var probe) ? probe : null;
    }

    /// <summary>
    /// Probes whose id or area matches <paramref name="selector"/> (case-insensitive prefix or
    /// substring). An <see cref="IProbe.ExplicitOnly"/> probe is only ever returned for an exact id
    /// match — <c>expprobe police</c> must not sweep one in.
    /// </summary>
    public static IReadOnlyList<IProbe> Select(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return Array.Empty<IProbe>();

        var exact = Find(selector);
        if (exact is not null)
            return new[] { exact };

        return _ordered
            .Where(p => !p.ExplicitOnly)
            .Where(p => p.Id.StartsWith(selector, StringComparison.OrdinalIgnoreCase)
                        || p.Area.Contains(selector, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static void Rebuild()
    {
        var ordered = new IProbe[ById.Count];
        ById.Values.CopyTo(ordered, 0);
        Array.Sort(ordered, static (a, b) =>
        {
            var byArea = string.CompareOrdinal(a.Area, b.Area);
            return byArea != 0 ? byArea : string.CompareOrdinal(a.Id, b.Id);
        });

        _ordered = ordered;
    }

    private sealed class Registration : IDisposable
    {
        internal static readonly IDisposable Inert = new Registration(null);

        private string? _id;

        internal Registration(string? id) => _id = id;

        public void Dispose()
        {
            var id = Interlocked.Exchange(ref _id, null);
            if (id is not null)
                Unregister(id);
        }
    }
}
