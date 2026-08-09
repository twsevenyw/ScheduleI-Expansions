namespace Expansions.Core.Diagnostics;

/// <summary>
/// An unknown that cannot be settled without changing the world — spawning something, driving
/// something, taking a fine. The harness will not do those, so it prints the recipe instead of
/// silently dropping the question.
/// </summary>
public sealed class MutatingTestNote
{
    public MutatingTestNote(string id, string area, string unknown, string recipe)
    {
        Id = id;
        Area = area;
        Unknown = unknown;
        Recipe = recipe;
    }

    /// <summary>Plan-local identifier, e.g. <c>HD-P3</c> or <c>SC-V6</c>.</summary>
    public string Id { get; }

    public string Area { get; }

    public string Unknown { get; }

    public string Recipe { get; }
}

/// <summary>
/// Read-only-probe blind spots, reported at the bottom of every run. Feature modules append their
/// own the same way they register probes.
/// </summary>
public static class MutatingTestCatalogue
{
    private static readonly Dictionary<string, MutatingTestNote> ById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    private static MutatingTestNote[] _ordered = Array.Empty<MutatingTestNote>();

    public static IReadOnlyList<MutatingTestNote> Notes => _ordered;

    public static IDisposable Register(MutatingTestNote note)
    {
        if (note is null)
            throw new ArgumentNullException(nameof(note));

        lock (Gate)
        {
            if (ById.ContainsKey(note.Id))
                return Registration.Inert;

            ById[note.Id] = note;
            Rebuild();
        }

        return new Registration(note.Id);
    }

    public static bool Unregister(string id)
    {
        lock (Gate)
        {
            if (string.IsNullOrEmpty(id) || !ById.Remove(id))
                return false;

            Rebuild();
            return true;
        }
    }

    private static void Rebuild()
    {
        var ordered = new MutatingTestNote[ById.Count];
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
