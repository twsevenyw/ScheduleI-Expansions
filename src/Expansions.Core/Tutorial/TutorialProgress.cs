using System.Reflection;
using S1API.Internal.Abstraction;
using S1API.Saveables;

namespace Expansions.Core.Tutorial;

/// <summary>
/// Per-save tutorial state: whether the line was started and which chapters are behind us.
/// <para>
/// Written to <c>&lt;save&gt;\Modded\Saveables\TutorialProgress\</c> by S1API, which discovers this
/// class by reflection because it derives <em>directly</em> from <see cref="Saveable"/> and
/// instantiates it itself. The instance S1API creates is the one it saves and loads, so the ctor
/// publishes it and the director always reads through <see cref="Current"/> rather than caching.
/// </para>
/// <para>
/// The chapter quests carry their own step-level progress; this only records the coarse position in
/// the line, which is the part a completed quest cannot tell us (S1API only restores quests that were
/// still active when the game was saved).
/// </para>
/// </summary>
public sealed class TutorialProgress : Saveable
{
    private static TutorialProgress? _instance;

    [SaveableField("ExpansionsTutorial")]
    private TutorialState? _state = new();

    public TutorialProgress() => _instance = this;

    /// <summary>
    /// The live instance, forcing S1API's discovery pass if it has not run yet. Never null: a
    /// standalone instance is better than a tutorial that cannot start, and it still persists as soon
    /// as S1API's own discovery catches up.
    /// </summary>
    public static TutorialProgress Current
    {
        get
        {
            if (_instance is null)
                ForceDiscovery();

            return _instance ??= new TutorialProgress();
        }
    }

    public bool Started
    {
        get => State.Started;
        set => State.Started = value;
    }

    public IReadOnlyList<string> CompletedChapters => Completed;

    public bool IsChapterComplete(string chapterId) =>
        Completed.Contains(chapterId, StringComparer.OrdinalIgnoreCase);

    public void MarkChapterComplete(string chapterId)
    {
        if (!IsChapterComplete(chapterId))
            Completed.Add(chapterId);
    }

    public void Wipe()
    {
        State.Started = false;
        Completed.Clear();
    }

    private List<string> Completed => State.CompletedChapters!;

    /// <summary>
    /// A save file holding <c>null</c>, or a hand-edited one missing the list, deserialises straight
    /// onto the field — so every read repairs rather than trusting it.
    /// </summary>
    private TutorialState State
    {
        get
        {
            _state ??= new TutorialState();
            _state.CompletedChapters ??= new List<string>();
            return _state;
        }
    }

    /// <summary>
    /// Drops the cached instance so a different save cannot inherit the previous one's state if S1API
    /// re-instantiates. Called when a save is unloaded.
    /// </summary>
    internal static void Forget() => _instance = null;

    /// <summary>
    /// S1API instantiates saveables lazily, on the first save or load. A brand-new game that has never
    /// saved would otherwise have no instance at all, so ask its registry to run discovery now. The
    /// registry is internal, hence reflection; failing is not fatal, just less durable.
    /// </summary>
    private static void ForceDiscovery()
    {
        try
        {
            var registry = typeof(Saveable).Assembly.GetType("S1API.Saveables.SaveableAutoRegistry", throwOnError: false);
            var method = registry?.GetMethod(
                "GetRegisteredSaveables",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            if (method?.Invoke(null, Array.Empty<object>()) is not System.Collections.IEnumerable saveables)
                return;

            // The registry is a lazy iterator: nothing is constructed until it is walked.
            foreach (var _ in saveables)
            {
                if (_instance is not null)
                    return;
            }
        }
        catch (Exception ex)
        {
            ExpansionHost.Log.Debug(
                $"Could not ask S1API to instantiate the tutorial saveable ({ex.GetType().Name}: {ex.Message}); " +
                $"using a standalone instance for this session.");
        }
    }

    /// <summary>Serialised verbatim by Newtonsoft; keep the members public and simple.</summary>
    private sealed class TutorialState
    {
        public bool Started { get; set; }

        public List<string>? CompletedChapters { get; set; } = new();
    }
}
