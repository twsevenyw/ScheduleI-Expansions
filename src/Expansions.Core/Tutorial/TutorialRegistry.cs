namespace Expansions.Core.Tutorial;

/// <summary>
/// Process-wide chapter list, the tutorial counterpart of <see cref="Diagnostics.ProbeRegistry"/>.
/// Core registers only its own chapters; each feature mod contributes its own on enable and drops it
/// on disable:
/// <code>Lifetime.Add(TutorialRegistry.Register(new DriversChapter()));</code>
/// <para>
/// There is no stub, placeholder or reserved slot for a mod that is not installed. A chapter named
/// after a mod the player has never had reads as a broken install rather than as content they are
/// missing, and a chapter that ticks itself off to keep the line moving teaches nothing. What is
/// listed here is exactly what is playable.
/// </para>
/// </summary>
public static class TutorialRegistry
{
    private static readonly Dictionary<string, ITutorialChapter> ById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    private static ITutorialChapter[] _ordered = Array.Empty<ITutorialChapter>();

    /// <summary>Fires whenever the chapter set or its ordering changes.</summary>
    public static event Action? Changed;

    public static int Count => _ordered.Length;

    /// <summary>Ordered by <see cref="ITutorialChapter.Order"/> then id, so the line is stable.</summary>
    public static IReadOnlyList<ITutorialChapter> Chapters => _ordered;

    /// <summary>
    /// Adds a chapter. Returns a token that removes it again; a second claim on one id is rejected
    /// with a warning and an inert token, so one bad mod cannot break the line.
    /// </summary>
    public static IDisposable Register(ITutorialChapter chapter)
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
                ExpansionHost.Log.Warn(
                    $"Tutorial chapter id '{id}' is already held by '{existing.Title}'; " +
                    $"ignoring the duplicate '{chapter.Title}'.");
                return Registration.Inert;
            }

            ById[id] = chapter;
            Rebuild();
        }

        Announce();
        return new Registration(id, chapter);
    }

    public static ITutorialChapter? Find(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        lock (Gate)
            return ById.TryGetValue(id, out var chapter) ? chapter : null;
    }

    public static bool IsRegistered(string id) => Find(id) is not null;

    /// <summary>Removes <paramref name="chapter"/> only if it is still the live holder of the id.</summary>
    private static void Unregister(string id, ITutorialChapter chapter)
    {
        lock (Gate)
        {
            if (!ById.TryGetValue(id, out var existing) || !ReferenceEquals(existing, chapter))
                return;

            ById.Remove(id);
            Rebuild();
        }

        Announce();
    }

    private static void Announce()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            ExpansionHost.Log.Debug($"A tutorial-registry listener threw ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    private static void Rebuild()
    {
        var ordered = new ITutorialChapter[ById.Count];
        ById.Values.CopyTo(ordered, 0);

        Array.Sort(ordered, static (a, b) =>
        {
            var byOrder = a.Order.CompareTo(b.Order);
            return byOrder != 0 ? byOrder : string.CompareOrdinal(a.Id, b.Id);
        });

        _ordered = ordered;
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
