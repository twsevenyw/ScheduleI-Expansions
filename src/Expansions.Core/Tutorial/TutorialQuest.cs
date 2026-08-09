using S1API.Quests;
using S1API.Saveables;
using UnityEngine;
using QuestState = S1API.Quests.Constants.QuestState;

namespace Expansions.Core.Tutorial;

/// <summary>
/// One chapter of the line, as a real quest in the game's own journal.
/// <para>
/// S1API keys a mod quest's save folder on the runtime class <em>name</em>, so every chapter needs a
/// concrete subclass whose name never changes — see <c>TutorialQuestSlots.cs</c>. Which chapter a slot
/// hosts is resolved from <see cref="TutorialRegistry"/> through <see cref="SlotKey"/>, which is a
/// constant per subclass; that matters because S1API's base constructor reads <see cref="Title"/> and
/// <see cref="QuestIcon"/> before this class's own constructor body has run.
/// </para>
/// <para>
/// The lifecycle S1API imposes is worth knowing: constructing the quest only builds the GameObject.
/// <see cref="OnCreated"/> runs later, from a Harmony prefix on the game's <c>Quest.Start</c>, and is
/// the only window in which entries can be added before the journal UI is built. On a restored save
/// <see cref="OnLoaded"/> runs <em>before</em> <see cref="OnCreated"/>, so it only stores data.
/// </para>
/// </summary>
internal abstract class TutorialQuest : Quest
{
    /// <summary>
    /// Public so S1API's loader finds it: it reads fields with
    /// <c>GetType().GetFields(Instance | Public | NonPublic)</c>, which does not return a base class's
    /// private fields. Never name a saveable field "QuestData" — that one is S1API's own.
    /// </summary>
    [SaveableField("TutorialChapter")]
    public TutorialQuestSave Save = new();

    private readonly Dictionary<string, QuestEntry> _entries = new(StringComparer.Ordinal);
    private readonly List<TutorialStep> _steps = new();

    protected TutorialQuest() => TutorialDirector.Track(this);

    /// <summary>Stable slot identifier. Must be a literal — it is read from the base constructor.</summary>
    internal abstract string SlotKey { get; }

    protected override string Title => TutorialDirector.SlotTitle(SlotKey);

    protected override string Description => TutorialDirector.SlotDescription(SlotKey);

    /// <summary>Begun by the director once the entries exist, not by S1API on construction.</summary>
    protected override bool AutoBegin => false;

    /// <summary>
    /// Non-null on purpose. S1API falls back to the Contacts app's icon, which dereferences
    /// <c>PlayerSingleton&lt;ContactsApp&gt;.Instance</c> inside the constructor and would throw if the
    /// phone is not up yet.
    /// </summary>
    protected override Sprite? QuestIcon => TutorialDirector.QuestIcon();

    internal QuestState CurrentState => QuestState;

    internal IReadOnlyList<TutorialStep> Steps => _steps;

    internal bool EntriesBuilt { get; private set; }

    internal bool EntriesBegun { get; private set; }

    /// <summary>
    /// S1API calls this from a Harmony prefix on the game's <c>Quest.Start</c>. Throwing here would
    /// land inside the game's own quest startup, so the director swallows everything.
    /// </summary>
    protected override void OnCreated()
    {
        base.OnCreated();
        TutorialDirector.OnQuestCreated(this);
    }

    /// <summary>Runs before <see cref="OnCreated"/> on a restored save: entries do not exist yet.</summary>
    protected override void OnLoaded()
    {
        base.OnLoaded();
        TutorialDirector.OnQuestLoaded(this);
    }

    /// <summary>
    /// Adds one journal entry per step. Called from the director while it is inside S1API's
    /// <c>Quest.Start</c> prefix, so it must not throw: an exception here would surface inside the
    /// game's own quest startup.
    /// </summary>
    internal void BuildEntries(IReadOnlyList<TutorialStep> steps)
    {
        if (EntriesBuilt)
            return;

        EntriesBuilt = true;

        foreach (var step in steps)
        {
            var entry = step.Poi.HasValue ? AddEntry(step.Title, step.Poi.Value) : AddEntry(step.Title);
            _steps.Add(step);
            _entries[step.Id] = entry;
        }
    }

    /// <summary>Puts every entry into the journal's active state. Safe to call more than once.</summary>
    internal void BeginEntries()
    {
        if (EntriesBegun)
            return;

        EntriesBegun = true;

        foreach (var entry in _entries.Values)
            entry.Begin();
    }

    internal bool IsStepComplete(string stepId) => Save.Contains(stepId);

    /// <summary>Ticks the entry off and records it, so a reload restores the same journal state.</summary>
    internal bool CompleteStep(string stepId)
    {
        if (!_entries.TryGetValue(stepId, out var entry))
            return false;

        Save.Add(stepId);
        entry.Complete();
        return true;
    }

    /// <summary>Re-applies the entries recorded in the save, without re-running their rewards.</summary>
    internal int RestoreCompletedSteps()
    {
        var restored = 0;

        foreach (var stepId in Save.Snapshot())
        {
            if (!_entries.TryGetValue(stepId, out var entry))
                continue;

            entry.Complete();
            restored++;
        }

        return restored;
    }
}

/// <summary>Step-level progress for one chapter quest. Newtonsoft round-trips the public members.</summary>
internal sealed class TutorialQuestSave
{
    public List<string>? CompletedSteps { get; set; } = new();

    internal bool Contains(string stepId) => Steps.Contains(stepId, StringComparer.Ordinal);

    internal void Add(string stepId)
    {
        if (!Contains(stepId))
            Steps.Add(stepId);
    }

    internal IReadOnlyList<string> Snapshot() => Steps.ToArray();

    /// <summary>A save written as <c>null</c>, or hand-edited, deserialises straight onto the property.</summary>
    private List<string> Steps => CompletedSteps ??= new List<string>();
}
