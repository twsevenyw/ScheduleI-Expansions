namespace Expansions.Core.Tutorial;

/// <summary>
/// Process-wide chapter list, the tutorial counterpart of <see cref="Diagnostics.ProbeRegistry"/>.
/// Core registers its own chapters plus a placeholder per feature mod; each mod overrides its
/// placeholder on enable and restores it on disable:
/// <code>Lifetime.Add(TutorialRegistry.Register(new DriversChapter()));</code>
/// </summary>
public static class TutorialRegistry
{
    private static readonly Dictionary<string, Entry> ById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    private static ITutorialChapter[] _ordered = Array.Empty<ITutorialChapter>();

    /// <summary>Fires whenever the chapter set or its ordering changes.</summary>
    public static event Action? Changed;

    public static int Count => _ordered.Length;

    /// <summary>Ordered by <see cref="ITutorialChapter.Order"/> then id, so the line is stable.</summary>
    public static IReadOnlyList<ITutorialChapter> Chapters => _ordered;

    /// <summary>
    /// Adds a chapter, overriding a Core placeholder with the same id. Returns a token that removes it
    /// again and puts any displaced placeholder back. A second non-placeholder claim on one id is
    /// rejected with a warning and an inert token, so one bad mod cannot break the line.
    /// </summary>
    public static IDisposable Register(ITutorialChapter chapter) => Register(chapter, isPlaceholder: false);

    /// <summary>Core's own registration path: yields to any mod that claims the same id.</summary>
    internal static IDisposable RegisterPlaceholder(ITutorialChapter chapter) =>
        Register(chapter, isPlaceholder: true);

    public static ITutorialChapter? Find(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        lock (Gate)
            return ById.TryGetValue(id, out var entry) ? entry.Chapter : null;
    }

    public static bool IsRegistered(string id) => Find(id) is not null;

    private static IDisposable Register(ITutorialChapter chapter, bool isPlaceholder)
    {
        if (chapter is null)
            throw new ArgumentNullException(nameof(chapter));

        if (string.IsNullOrWhiteSpace(chapter.Id))
            throw new ArgumentException("Chapter Id must be a non-empty, stable string.", nameof(chapter));

        var id = chapter.Id;

        lock (Gate)
        {
            if (ById.TryGetValue(id, out var existing))
            {
                if (!existing.IsPlaceholder)
                {
                    ExpansionHost.Log.Warn(
                        $"Tutorial chapter id '{id}' is already held by '{existing.Chapter.Title}'; " +
                        $"ignoring the duplicate '{chapter.Title}'.");
                    return Registration.Inert;
                }

                if (isPlaceholder)
                    return Registration.Inert;

                ById[id] = new Entry(chapter, false, existing.Chapter);
                Rebuild();
                ExpansionHost.Log.Debug($"Tutorial chapter '{id}' is now supplied by '{chapter.Title}'.");
                return new Registration(id, chapter);
            }

            ById[id] = new Entry(chapter, isPlaceholder, null);
            Rebuild();
        }

        Changed?.Invoke();
        return new Registration(id, chapter);
    }

    /// <summary>Removes <paramref name="chapter"/> only if it is still the live holder of the id.</summary>
    private static void Unregister(string id, ITutorialChapter chapter)
    {
        lock (Gate)
        {
            if (!ById.TryGetValue(id, out var entry) || !ReferenceEquals(entry.Chapter, chapter))
                return;

            if (entry.Displaced is not null)
                ById[id] = new Entry(entry.Displaced, true, null);
            else
                ById.Remove(id);

            Rebuild();
        }

        Changed?.Invoke();
    }

    private static void Rebuild()
    {
        var ordered = new ITutorialChapter[ById.Count];
        var index = 0;
        foreach (var entry in ById.Values)
            ordered[index++] = entry.Chapter;

        Array.Sort(ordered, static (a, b) =>
        {
            var byOrder = a.Order.CompareTo(b.Order);
            return byOrder != 0 ? byOrder : string.CompareOrdinal(a.Id, b.Id);
        });

        _ordered = ordered;
    }

    private readonly struct Entry
    {
        internal Entry(ITutorialChapter chapter, bool isPlaceholder, ITutorialChapter? displaced)
        {
            Chapter = chapter;
            IsPlaceholder = isPlaceholder;
            Displaced = displaced;
        }

        internal ITutorialChapter Chapter { get; }

        internal bool IsPlaceholder { get; }

        /// <summary>The placeholder this chapter took over from, restored when it is disposed.</summary>
        internal ITutorialChapter? Displaced { get; }
    }

    private sealed class Registration : IDisposable
    {
        internal static readonly IDisposable Inert = new Registration(null, null);

        private readonly ITutorialChapter? _chapter;
        private string? _id;

        internal Registration(string? id, ITutorialChapter? chapter)
        {
            _id = id;
            _chapter = chapter;
        }

        public void Dispose()
        {
            var id = Interlocked.Exchange(ref _id, null);
            if (id is not null && _chapter is not null)
                Unregister(id, _chapter);
        }
    }
}
