using System.Reflection;
using System.Runtime.CompilerServices;
using Expansions.Core.Configuration;
using Expansions.Core.Diagnostics;
using Expansions.Core.Logging;
using Expansions.Core.Tutorial.Chapters;
using Expansions.Core.UI.Native;
using S1API.Quests;
using UnityEngine;
using QuestState = S1API.Quests.Constants.QuestState;

namespace Expansions.Core.Tutorial;

/// <summary>
/// Runs the tutorial quest line: decides which chapter is current, creates its quest, arms the
/// objectives and advances when they are all ticked off.
/// <para>
/// Nothing here is allowed to be fatal. Every entry point is wrapped, and after
/// <see cref="MaxTickFailures"/> consecutive failures the director parks itself for the session and
/// says so once — Core and the three mods keep working either way.
/// </para>
/// </summary>
public static class TutorialDirector
{
    /// <summary>Consecutive tick failures before the director gives up for the session.</summary>
    private const int MaxTickFailures = 10;

    /// <summary>Frames to let S1API's loaders restore quests before creating one ourselves.</summary>
    private const int SettleFrames = 240;

    /// <summary>Frames between the entries existing and the quest being begun, so the journal builds first.</summary>
    private const int BeginDelayFrames = 20;

    /// <summary>How often the (reflective) "is a save loaded" check runs.</summary>
    private const int SessionPollFrames = 30;

    private const int MaxTrackedQuests = 32;

    /// <summary>
    /// How long an availability answer is reused. Every row on the Tutorial tab asks its chapter
    /// whether it is ready on every frame the tab is open, and a chapter's answer can cost a registry
    /// walk or a session-state read.
    /// </summary>
    private const float AvailabilityTtlSeconds = 0.5f;

    /// <summary>
    /// How long a slot that produced no objectives is left alone before it is tried again. Long enough
    /// that the create-and-cancel cycle cannot spin, short enough that a module which finishes wiring
    /// itself into the loaded game a few seconds later still gets played without a restart.
    /// </summary>
    private const float BarrenRetrySeconds = 15f;

    private static readonly ModuleLogger Log = new("Tutorial");
    private static readonly List<TutorialQuest> Tracked = new();
    private static readonly Dictionary<string, int> ConditionFailures = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, CachedAvailability> AvailabilityCache = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ReportedAvailabilityFailures = new(StringComparer.Ordinal);

    /// <summary>
    /// Slots whose chapters claimed to be ready and then declared no objectives, and the moment each one
    /// becomes worth trying again.
    /// <para>
    /// A cooldown rather than a permanent exclusion, deliberately. Retrying every frame would create and
    /// cancel a quest forever, but writing the slot off for the session would mean one bad answer during a
    /// save load permanently costs the player a chapter — which is the failure mode this whole design exists
    /// to avoid. Nothing here ever marks a chapter complete; a slot on cooldown is simply stepped over, and
    /// the line comes back to it.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, float> BarrenUntil = new(StringComparer.Ordinal);

    private static bool _initialized;
    private static bool _parked;
    private static int _tickFailures;

    private static bool _sessionActive;
    private static int _sessionFrames;
    private static int _sessionPoll;

    private static bool _pendingStart;
    private static SlotRun? _active;
    private static Sprite? _icon;
    private static string _status = "Not started.";

    /// <summary>Fires when <see cref="Status"/> changes, so an open menu can repaint.</summary>
    public static event Action? StatusChanged;

    /// <summary>True once the line has been started on the loaded save.</summary>
    public static bool IsStarted
    {
        get
        {
            try
            {
                return _sessionActive ? TutorialProgress.Current.Started : _pendingStart;
            }
            catch
            {
                return _pendingStart;
            }
        }
    }

    /// <summary>True when a save is loaded, i.e. when the line can actually run.</summary>
    public static bool IsSessionActive => _sessionActive;

    /// <summary>One line for the menu footer. Always safe to read.</summary>
    public static string Status => _status;

    public static int ChapterCount => TutorialRegistry.Count;

    /// <summary>Registered chapters the owner has left switched on. This is the line's real length.</summary>
    public static int EnabledChapterCount
    {
        get
        {
            try
            {
                return TutorialSettings.EnabledCount();
            }
            catch
            {
                return TutorialRegistry.Count;
            }
        }
    }

    /// <summary>Id of the chapter being played, or empty. Useful for a mod gating on its own chapter.</summary>
    public static string CurrentChapterId
    {
        get
        {
            var run = _active;
            if (run is null)
                return string.Empty;

            var chapters = ChaptersFor(run.Slot);
            return chapters.Count > 0 ? chapters[0].Id : string.Empty;
        }
    }

    /// <summary>Chapters the loaded save records as done. Zero outside a game.</summary>
    public static int CompletedChapterCount
    {
        get
        {
            try
            {
                if (!_sessionActive)
                    return 0;

                var progress = TutorialProgress.Current;
                var done = 0;

                foreach (var chapter in TutorialRegistry.Chapters)
                {
                    if (TutorialSettings.IsEnabled(chapter.Id) && progress.IsChapterComplete(chapter.Id))
                        done++;
                }

                return done;
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>Title of the chapter being played, or empty.</summary>
    public static string CurrentChapterTitle
    {
        get
        {
            var id = CurrentChapterId;
            return id.Length == 0 ? string.Empty : TutorialRegistry.Find(id)?.Title ?? id;
        }
    }

    /// <summary>Where one chapter stands, for the Tutorial tab. Always safe to call.</summary>
    public static TutorialChapterState StateOf(string chapterId)
    {
        try
        {
            if (!TutorialSettings.IsEnabled(chapterId))
                return TutorialChapterState.Skipped;

            if (_sessionActive && TutorialProgress.Current.IsChapterComplete(chapterId))
                return TutorialChapterState.Complete;

            // Compared through the slot map rather than by listing the slot's chapters: this is polled
            // per row per frame while the Tutorial tab is open.
            if (IsPlaying(chapterId))
                return TutorialChapterState.Playing;

            var chapter = TutorialRegistry.Find(chapterId);
            if (chapter is not null && !AvailabilityOf(chapter).IsAvailable)
                return TutorialChapterState.Unavailable;

            return TutorialChapterState.Pending;
        }
        catch
        {
            return TutorialChapterState.Pending;
        }
    }

    /// <summary>
    /// Why a chapter is not playable, or empty when it is. This is the chapter's own sentence, shown
    /// verbatim on its row — the whole point of deferring instead of stubbing is that the owner is
    /// told what to switch on.
    /// </summary>
    public static string ReasonFor(string chapterId)
    {
        try
        {
            var chapter = TutorialRegistry.Find(chapterId);
            if (chapter is null)
                return string.Empty;

            var availability = AvailabilityOf(chapter);
            return availability.IsAvailable ? string.Empty : availability.Reason;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>The objective the player is on within <paramref name="chapterId"/>, or empty.</summary>
    public static string CurrentObjectiveTitle(string chapterId)
    {
        try
        {
            var run = _active;
            if (run is null || !IsPlaying(chapterId))
                return string.Empty;

            foreach (var step in run.Steps)
            {
                if (!run.Quest.IsStepComplete(step.Id))
                    return step.Title;
            }

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>True when the live chapter quest is the one hosting <paramref name="chapterId"/>.</summary>
    private static bool IsPlaying(string chapterId)
    {
        var run = _active;
        return run is not null &&
               string.Equals(TutorialSlots.SlotFor(chapterId), run.Slot, StringComparison.Ordinal);
    }

    /// <summary>
    /// Title of the objective the player is on, whichever chapter it belongs to, or empty. The Tutorial
    /// tab shows this so the answer to "what am I meant to be doing?" is on the screen that started it.
    /// </summary>
    public static string CurrentObjective
    {
        get
        {
            try
            {
                var run = _active;
                if (run is null)
                    return string.Empty;

                foreach (var step in run.Steps)
                {
                    if (!run.Quest.IsStepComplete(step.Id))
                        return step.Title;
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// Id of the objective the player is on, or empty. This is the id to hand to
    /// <see cref="TutorialSignals.Raise(string)"/>.
    /// </summary>
    public static string CurrentObjectiveId
    {
        get
        {
            var run = _active;
            if (run is null)
                return string.Empty;

            foreach (var step in run.Steps)
            {
                if (!run.Quest.IsStepComplete(step.Id))
                    return step.Id;
            }

            return string.Empty;
        }
    }

    /// <summary>Registers Core's own chapters. Idempotent; called from <c>ExpansionHost</c>.</summary>
    internal static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;

        try
        {
            TutorialChapters.RegisterDefaults();
            TutorialRegistry.Changed += OnRegistryChanged;
            TutorialSettings.Changed += OnChapterSwitchesChanged;
            SetStatus(Describe());
            Log.Msg(
                $"Tutorial ready: {TutorialRegistry.Count} chapter(s). Start it with the Enable Quest " +
                $"button on the Expansions screen ({ExpansionConfig.MenuHotkey}).");
        }
        catch (Exception ex)
        {
            _parked = true;
            Log.Error("The tutorial chapters could not be registered; the rest of Expansions is unaffected.", ex);
        }
    }

    /// <summary>Drops every session object. Called when the last mod unloads.</summary>
    internal static void Shutdown()
    {
        try
        {
            TutorialRegistry.Changed -= OnRegistryChanged;
            TutorialSettings.Changed -= OnChapterSwitchesChanged;
            EndSession();
            TutorialChapters.Unregister();
            TutorialSignals.Clear();
            Tracked.Clear();
            _icon = null;
            _initialized = false;
        }
        catch (Exception ex)
        {
            Log.Error("Tutorial shutdown threw; continuing.", ex);
        }
    }

    /// <summary>
    /// Starts the line. From the main menu there is no save to write to, so the request is held and
    /// applied to the next save loaded this session.
    /// </summary>
    public static string Start()
    {
        try
        {
            if (TutorialRegistry.Count == 0)
                return Announce("No tutorial chapters are registered.");

            if (EnabledChapterCount == 0)
                return Announce("Every chapter is switched off - turn one on from the Tutorial tab first.");

            if (!_sessionActive)
            {
                _pendingStart = true;
                return Announce("Tutorial armed. It begins as soon as you load or start a game.");
            }

            var progress = TutorialProgress.Current;
            if (progress.Started)
                return Announce(Describe());

            progress.Started = true;
            _pendingStart = false;
            Log.Msg("Tutorial quest line started.");
            return Announce(Describe());
        }
        catch (Exception ex)
        {
            Log.Error("Could not start the tutorial.", ex);
            return Announce("The tutorial could not be started; see the log.");
        }
    }

    /// <summary>Cancels any live chapter quest and clears the recorded progress for this save.</summary>
    public static string Reset()
    {
        try
        {
            _pendingStart = false;
            CancelActive();

            if (_sessionActive)
                TutorialProgress.Current.Wipe();

            // Latched signals are dropped with everything else. A signal raised before the line was reset
            // describes something the player did on the previous run, and honouring it would tick an
            // objective off the moment it appeared.
            TutorialSignals.Clear();
            ConditionFailures.Clear();
            ReportedAvailabilityFailures.Clear();
            InvalidateAvailability();
            Log.Msg("Tutorial progress reset.");
            return Announce("Tutorial reset. Press Enable Quest to run it again.");
        }
        catch (Exception ex)
        {
            Log.Error("Could not reset the tutorial.", ex);
            return Announce("The tutorial could not be reset; see the log.");
        }
    }

    public static string Restart()
    {
        Reset();
        return Start();
    }

    /// <summary>Per-frame pump, driven by <c>ExpansionHost.Update</c>.</summary>
    internal static void Tick()
    {
        if (!_initialized || _parked)
            return;

        try
        {
            TickCore();
            _tickFailures = 0;
        }
        catch (Exception ex)
        {
            if (++_tickFailures < MaxTickFailures)
                return;

            _parked = true;
            Log.Error(
                $"The tutorial threw {MaxTickFailures} times in a row and has been parked for this session. " +
                $"Everything else keeps running.",
                ex);
        }
    }

    internal static void OnSceneChanged(string sceneName)
    {
        // Sprites are per-scene and the menu drops its cache on every transition, so the quest icon
        // has to be re-harvested rather than kept as a dangling native handle.
        _icon = null;

        if (!string.Equals(sceneName, "Main", StringComparison.Ordinal) &&
            !string.Equals(sceneName, "Tutorial", StringComparison.Ordinal))
        {
            EndSession();
        }
    }

    /// <summary>Called from every <see cref="TutorialQuest"/> constructor, restored or fresh.</summary>
    internal static void Track(TutorialQuest quest)
    {
        try
        {
            Tracked.RemoveAll(static q => q is null);

            if (Tracked.Count >= MaxTrackedQuests)
                Tracked.RemoveAt(0);

            Tracked.Add(quest);
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not track a tutorial quest ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    /// <summary>Runs inside S1API's <c>Quest.Start</c> prefix. Must never throw.</summary>
    internal static void OnQuestCreated(TutorialQuest quest)
    {
        try
        {
            var run = _active is not null && ReferenceEquals(_active.Quest, quest)
                ? _active
                : Adopt(quest);

            if (run is null)
                return;

            quest.BuildEntries(run.Steps);
            run.BuiltAtFrame = Time.frameCount;

            Log.Debug($"Chapter '{run.Slot}' quest created with {run.Steps.Count} objective(s).");
        }
        catch (Exception ex)
        {
            Log.Error($"Could not build the objectives for the '{quest.SlotKey}' chapter quest.", ex);
        }
    }

    /// <summary>Runs before <see cref="OnQuestCreated"/> on a restored save.</summary>
    internal static void OnQuestLoaded(TutorialQuest quest)
    {
        try
        {
            Log.Debug($"Chapter '{quest.SlotKey}' quest restored from the save.");
        }
        catch
        {
            // Diagnostic only.
        }
    }

    /// <summary>Quest title for a slot. Read from S1API's constructor, so it must not throw.</summary>
    internal static string SlotTitle(string slot)
    {
        try
        {
            var chapters = ChaptersFor(slot);
            return chapters.Count switch
            {
                0 => "Expansions Tutorial",
                1 => chapters[0].Title,
                _ => "Expansions Tutorial: More",
            };
        }
        catch
        {
            return "Expansions Tutorial";
        }
    }

    internal static string SlotDescription(string slot)
    {
        try
        {
            var chapters = ChaptersFor(slot);
            if (chapters.Count == 0)
                return "A guided tour of the Expansions mod suite.";

            return string.Join(" ", chapters.Select(static c => c.Description));
        }
        catch
        {
            return "A guided tour of the Expansions mod suite.";
        }
    }

    /// <summary>
    /// A non-null icon keeps S1API away from <c>PlayerSingleton&lt;ContactsApp&gt;.Instance</c>, which
    /// it would otherwise dereference inside the quest constructor.
    /// </summary>
    internal static Sprite? QuestIcon()
    {
        try
        {
            if (_icon != null)
                return _icon;

            var trophy = GameSprites.TrophyIcon();
            _icon = trophy != null ? trophy : GameSprites.RoundedRect();
            return _icon;
        }
        catch
        {
            return null;
        }
    }

    private static void TickCore()
    {
        RefreshSession();

        if (!_sessionActive)
            return;

        _sessionFrames++;
        if (_sessionFrames < SettleFrames)
            return;

        var progress = TutorialProgress.Current;

        if (_pendingStart)
        {
            _pendingStart = false;
            progress.Started = true;
            Log.Msg("Tutorial quest line started (armed from the main menu).");
        }

        if (!progress.Started)
            return;

        // A live chapter keeps the floor for as long as any of its objectives can still be finished, even if
        // an earlier chapter has just become playable. The alternative — always honouring the first playable
        // slot — would yank a half-finished quest out of the journal the moment a module further up the line
        // woke up, throwing away objectives the player had already earned.
        if (_active is { } running)
        {
            if (StillPlayable(running, progress))
            {
                AdvanceRun(running, progress);
                return;
            }

            StandDown(running);
        }

        var slot = CurrentSlot(progress);
        if (slot is null)
        {
            ReportIdle(progress);
            return;
        }

        _active ??= BeginChapter(slot);
        if (_active is null)
            return;

        AdvanceRun(_active, progress);
    }

    /// <summary>
    /// True while at least one of the chapters that put objectives into this quest is still switched on,
    /// unfinished and ready. Once none are, the quest holds nothing the player can act on.
    /// </summary>
    private static bool StillPlayable(SlotRun run, TutorialProgress progress)
    {
        foreach (var chapterId in run.PlayedChapterIds)
        {
            var chapter = TutorialRegistry.Find(chapterId);
            if (chapter is not null && IsPlayable(chapter, progress))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Retires a live chapter whose objectives can no longer be met — the module was switched off, withdrew
    /// its chapter, or reported itself not ready after the quest had begun.
    /// <para>
    /// Cancelled, never completed. Nothing is written to the save, so the chapter stays unfinished and the
    /// line replays it in full once it is ready again, which is the same thing that happens when the owner
    /// flips its switch off and on. Cancelling also matters for the journal: leaving the quest active would
    /// park an entry the player can never tick off.
    /// </para>
    /// </summary>
    private static void StandDown(SlotRun run)
    {
        Log.Msg(
            $"Tutorial chapter '{run.Slot}' can no longer be played and has been withdrawn. It is not marked " +
            $"complete and will run again from the start when it is ready.");

        CancelQuest(run.Quest);

        if (_active is not null && ReferenceEquals(_active, run))
            _active = null;

        SetStatus(Describe());
    }

    private static void RefreshSession()
    {
        if (_sessionPoll-- > 0)
            return;

        _sessionPoll = SessionPollFrames;

        bool loaded;
        try
        {
            loaded = GameSessionState.Capture().IsSaveLoaded;
        }
        catch
        {
            loaded = false;
        }

        if (loaded == _sessionActive)
            return;

        if (loaded)
        {
            _sessionActive = true;
            _sessionFrames = 0;

            // Readiness answers taken in the main menu, or during the load, describe a game that was not
            // running yet. Keeping any of them is how a chapter ends up written off for the whole save.
            InvalidateAvailability();
            Log.Debug("A save is loaded; the tutorial will settle before touching anything.");
        }
        else
        {
            EndSession();
        }
    }

    private static void EndSession()
    {
        if (!_sessionActive && _active is null)
            return;

        _sessionActive = false;
        _sessionFrames = 0;
        _active = null;
        Tracked.Clear();
        TutorialProgress.Forget();
        InvalidateAvailability();
        SetStatus("Not in a game.");
    }

    /// <summary>
    /// The first slot with a chapter that can be played right now.
    /// <para>
    /// Three things make a chapter unplayable and none of them mark it complete: it is switched off on
    /// the Tutorial tab, it is already recorded as done on this save, or it reports itself not ready.
    /// The third is the one that matters here — the line steps past a deferred chapter and picks it up
    /// on a later tick, so a mod switched on halfway through still gets played rather than skipped.
    /// </para>
    /// </summary>
    private static string? CurrentSlot(TutorialProgress progress)
    {
        foreach (var slot in TutorialSlots.Ordered)
        {
            if (IsOnBarrenCooldown(slot))
                continue;

            foreach (var chapter in ChaptersFor(slot))
            {
                if (IsPlayable(chapter, progress))
                    return slot;
            }
        }

        return null;
    }

    private static bool IsPlayable(ITutorialChapter chapter, TutorialProgress progress) =>
        TutorialSettings.IsEnabled(chapter.Id) &&
        !progress.IsChapterComplete(chapter.Id) &&
        AvailabilityOf(chapter).IsAvailable;

    private static bool IsOnBarrenCooldown(string slot) =>
        BarrenUntil.TryGetValue(slot, out var until) && UnscaledNow() < until;

    /// <summary>
    /// Steps over a slot for a while. Said once per cooldown rather than once per session, because the
    /// interesting case is a slot that recovers and this is the only trace of why it stalled.
    /// </summary>
    private static void MarkBarren(string slot)
    {
        // UnscaledNow yields infinity when there is no clock, which would turn a cooldown into a life
        // sentence. Zero is the safe reading: the slot is retried on the next tick.
        var now = UnscaledNow();
        if (!float.IsFinite(now))
            now = 0f;

        if (!BarrenUntil.ContainsKey(slot) || now >= BarrenUntil[slot])
        {
            Log.Warn(
                $"Tutorial slot '{slot}' had nothing to play; stepping over it and trying again in " +
                $"{BarrenRetrySeconds:0} seconds. Nothing has been marked complete.");
        }

        BarrenUntil[slot] = now + BarrenRetrySeconds;
    }

    /// <summary>
    /// Drops every cached readiness answer and every cooldown, so the next tick asks the chapters again.
    /// Called whenever something that can change an answer changes: a chapter registered or withdrawn, a
    /// per-chapter switch flipped, a save loaded, the line reset.
    /// </summary>
    private static void InvalidateAvailability()
    {
        AvailabilityCache.Clear();
        BarrenUntil.Clear();
    }

    /// <summary>
    /// Nothing to play. Either the line is finished, or every chapter left is waiting on something —
    /// and in the second case the owner is told which one and why, rather than being congratulated.
    /// </summary>
    private static void ReportIdle(TutorialProgress progress)
    {
        var total = EnabledChapterCount;
        var done = CompletedChapterCount;

        if (total > 0 && done >= total)
        {
            if (!_status.StartsWith("Tutorial complete", StringComparison.Ordinal))
                Log.Msg("Tutorial quest line complete.");

            SetStatus($"Tutorial complete - all {total} chapters done. Reset to run it again.");
            return;
        }

        foreach (var chapter in TutorialRegistry.Chapters)
        {
            if (!TutorialSettings.IsEnabled(chapter.Id) || progress.IsChapterComplete(chapter.Id))
                continue;

            var availability = AvailabilityOf(chapter);
            if (availability.IsAvailable)
                continue;

            SetStatus($"Tutorial {done}/{total} - waiting on '{chapter.Title}': {availability.Reason}.");
            return;
        }

        SetStatus($"Tutorial {done}/{total} - preparing the next chapter.");
    }

    private static SlotRun? BeginChapter(string slot)
    {
        var type = TutorialSlots.QuestTypeFor(slot);
        if (type is null)
            return null;

        // S1API may already have restored this chapter's quest from the save, in which case its
        // constructor tracked it before we got here.
        foreach (var candidate in Tracked)
        {
            if (candidate is not null && candidate.GetType() == type)
                return Adopt(candidate);
        }

        if (!IsHostOrOffline())
        {
            SetStatus("Tutorial paused: only the host can run it in multiplayer.");
            return null;
        }

        try
        {
            var created = CreateQuest(type);
            if (created is not TutorialQuest quest)
            {
                Log.Warn($"Creating the '{slot}' chapter quest returned {created?.GetType().Name ?? "null"}.");
                return null;
            }

            // The quest object exists but Unity has not run Start on it yet, so the run is stitched up
            // here and its entries are filled in from OnQuestCreated a frame or two later.
            return _active = Adopt(quest);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not create the '{slot}' chapter quest; parking the tutorial for this session.", ex);
            _parked = true;
            return null;
        }
    }

    /// <summary>
    /// Isolated so a <see cref="TypeLoadException"/> from the S1API quest types is caught by the
    /// caller instead of tearing down the whole tick: the JIT resolves them when this body is
    /// compiled, which happens at the call site inside the try block.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Quest CreateQuest(Type type) => QuestManager.CreateQuest(type);

    /// <summary>
    /// Builds the run state describing what <paramref name="quest"/> must do, from the chapters in its
    /// slot that are switched on, unfinished and ready. Null when that set turns out to be empty,
    /// which is the one case where the quest is cancelled rather than played.
    /// </summary>
    private static SlotRun? Adopt(TutorialQuest quest)
    {
        var slot = quest.SlotKey;
        var progress = _sessionActive ? TutorialProgress.Current : null;
        var steps = new List<TutorialStep>();
        var played = new List<string>();

        foreach (var chapter in ChaptersFor(slot))
        {
            // Each of these contributes no objectives at all rather than a self-completing one: the
            // journal should never carry an entry the player did not earn.
            if (!TutorialSettings.IsEnabled(chapter.Id))
                continue;

            if (progress is not null && progress.IsChapterComplete(chapter.Id))
                continue;

            if (!AvailabilityOf(chapter).IsAvailable)
                continue;

            var built = SafeBuild(chapter);
            if (built.Count == 0)
            {
                Log.Warn($"Chapter '{chapter.Id}' reported itself ready but declared no objectives.");
                continue;
            }

            steps.AddRange(built);
            played.Add(chapter.Id);
        }

        if (steps.Count == 0)
        {
            MarkBarren(slot);
            CancelQuest(quest);

            if (_active is not null && ReferenceEquals(_active.Quest, quest))
                _active = null;

            return null;
        }

        var run = new SlotRun(slot, quest, steps, played);
        _active = run;
        ApplyCompletionReward(quest);
        return run;
    }

    private static void AdvanceRun(SlotRun run, TutorialProgress progress)
    {
        var quest = run.Quest;

        if (!quest.EntriesBuilt)
            return;

        if (!quest.EntriesBegun)
        {
            if (Time.frameCount - run.BuiltAtFrame < BeginDelayFrames)
                return;

            quest.Begin();
            quest.BeginEntries();

            var restored = quest.RestoreCompletedSteps();
            if (restored > 0)
                Log.Debug($"Chapter '{run.Slot}': restored {restored} completed objective(s).");

            SetStatus(Describe());
            return;
        }

        PollSteps(run);

        var state = quest.CurrentState;
        if (state == QuestState.Active || state == QuestState.Inactive)
            return;

        // Only the chapters that actually contributed objectives. A chapter switched off, already
        // finished, or held back as not-ready put nothing in this quest, so finishing it says nothing
        // about them.
        foreach (var chapterId in run.PlayedChapterIds)
            progress.MarkChapterComplete(chapterId);

        Tracked.Remove(quest);
        _active = null;
        Log.Msg($"Tutorial chapter '{run.Slot}' {state.ToString().ToLowerInvariant()}.");
        SetStatus(Describe());
    }

    private static void PollSteps(SlotRun run)
    {
        foreach (var step in run.Steps)
        {
            if (run.Quest.IsStepComplete(step.Id))
                continue;

            if (!ShouldComplete(step))
                continue;

            if (!run.Quest.CompleteStep(step.Id))
                continue;

            Log.Msg($"Tutorial objective complete: {step.Title}");
            GrantReward(step);
            SetStatus(Describe());
        }
    }

    private static bool ShouldComplete(TutorialStep step)
    {
        if (TutorialSignals.Consume(step.Id))
            return true;

        if (step.Condition is null)
            return false;

        try
        {
            return step.Condition();
        }
        catch (Exception ex)
        {
            var failures = ConditionFailures.TryGetValue(step.Id, out var count) ? count + 1 : 1;
            ConditionFailures[step.Id] = failures;

            if (failures == MaxTickFailures)
            {
                Log.Error(
                    $"The completion check for objective '{step.Id}' has thrown {failures} times; it will " +
                    $"keep being polled but is clearly broken.",
                    ex);
            }

            return false;
        }
    }

    private static void GrantReward(TutorialStep step)
    {
        if (step.Reward is null)
            return;

        try
        {
            step.Reward();

            if (step.RewardDescription.Length > 0)
                Log.Msg($"Reward: {step.RewardDescription}");
        }
        catch (Exception ex)
        {
            Log.Error($"The reward for objective '{step.Id}' threw; the objective still counts as done.", ex);
        }
    }

    private static void CancelActive()
    {
        var quest = _active?.Quest;
        _active = null;

        if (quest is not null)
            CancelQuest(quest);
    }

    private static void CancelQuest(TutorialQuest quest)
    {
        try
        {
            if (quest.CurrentState is QuestState.Active or QuestState.Inactive)
                quest.Cancel();
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not cancel the chapter quest ({ex.GetType().Name}: {ex.Message}).");
        }

        Tracked.Remove(quest);
    }

    private static IReadOnlyList<ITutorialChapter> ChaptersFor(string slot)
    {
        var matched = new List<ITutorialChapter>();

        foreach (var chapter in TutorialRegistry.Chapters)
        {
            if (string.Equals(TutorialSlots.SlotFor(chapter.Id), slot, StringComparison.Ordinal))
                matched.Add(chapter);
        }

        return matched;
    }

    /// <summary>
    /// A chapter's readiness, reused for <see cref="AvailabilityTtlSeconds"/>. Every row on the
    /// Tutorial tab asks this on every frame the tab is open.
    /// </summary>
    private static TutorialAvailability AvailabilityOf(ITutorialChapter chapter)
    {
        var now = UnscaledNow();

        if (AvailabilityCache.TryGetValue(chapter.Id, out var cached) &&
            now - cached.Stamp < AvailabilityTtlSeconds)
        {
            return cached.Value;
        }

        var value = SafeAvailability(chapter);
        AvailabilityCache[chapter.Id] = new CachedAvailability(now, value);
        return value;
    }

    private static float UnscaledNow()
    {
        try
        {
            return Time.unscaledTime;
        }
        catch
        {
            // Off the Unity thread there is no clock; recompute rather than serve a stale answer.
            return float.PositiveInfinity;
        }
    }

    private static TutorialAvailability SafeAvailability(ITutorialChapter chapter)
    {
        try
        {
            return chapter.GetAvailability();
        }
        catch (Exception ex)
        {
            // Once per chapter: this is polled, and a broken predicate would otherwise fill the log.
            if (ReportedAvailabilityFailures.Add(chapter.Id))
            {
                Log.Warn(
                    $"Chapter '{chapter.Id}' threw while reporting availability " +
                    $"({ex.GetType().Name}: {ex.Message}); it will be held back until it stops.");
            }

            return TutorialAvailability.ComingSoon("this chapter could not be prepared");
        }
    }

    private static IReadOnlyList<TutorialStep> SafeBuild(ITutorialChapter chapter)
    {
        try
        {
            var builder = new TutorialChapterBuilder(chapter.Id);
            chapter.BuildSteps(builder);
            return builder.Build();
        }
        catch (Exception ex)
        {
            Log.Error($"Chapter '{chapter.Id}' threw while declaring its objectives; it will be skipped.", ex);
            return Array.Empty<TutorialStep>();
        }
    }

    /// <summary>
    /// Hands the chapter's XP to the game's own completion reward rather than granting anything
    /// ourselves. <c>CompletionXP</c> is a field on the game's quest behaviour, reachable only through
    /// S1API's internal handle to it, so the whole path is reflective and entirely optional.
    /// </summary>
    private static void ApplyCompletionReward(TutorialQuest quest)
    {
        try
        {
            var handle = typeof(Quest).GetField(
                "S1Quest",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            var gameQuest = handle?.GetValue(quest);
            if (gameQuest is null)
                return;

            var xp = gameQuest.GetType().GetProperty("CompletionXP") ??
                     (MemberInfo?)gameQuest.GetType().GetField("CompletionXP");

            switch (xp)
            {
                case PropertyInfo property when property.CanWrite:
                    property.SetValue(gameQuest, 50);
                    break;
                case FieldInfo field:
                    field.SetValue(gameQuest, 50);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not set the chapter's completion XP ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    /// <summary>
    /// Creating a quest writes to the save, so it is host-only. No FishNet manager means single
    /// player, which is the common case and is allowed.
    /// </summary>
    private static bool IsHostOrOffline()
    {
        try
        {
            var finder = GameReflection.FindType("Il2CppFishNet.InstanceFinder");
            if (finder is null)
                return true;

            if (!GameReflection.TryReadStatic(finder, "NetworkManager", out var manager, out _) ||
                !GameReflection.IsPresent(manager))
            {
                return true;
            }

            return GameReflection.TryReadStatic(finder, "IsServer", out var isServer, out _) && isServer is true;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// A chapter arriving or being withdrawn can change which slot is next, so every cached answer is
    /// dropped rather than left to expire. A mod that registers its chapter late gets played on the next
    /// tick, without a restart and without a tutorial reset.
    /// </summary>
    private static void OnRegistryChanged()
    {
        InvalidateAvailability();
        SetStatus(Describe());
    }

    /// <summary>
    /// A chapter switched off while its own quest is live stops being played on the next tick, which sees the
    /// switch through <see cref="StillPlayable"/> and withdraws the quest. Handled there rather than here so
    /// there is exactly one rule for a chapter that stops being playable, whether the cause was a switch, a
    /// mod withdrawing its chapter, or the chapter reporting itself not ready.
    /// </summary>
    private static void OnChapterSwitchesChanged()
    {
        try
        {
            InvalidateAvailability();
            SetStatus(Describe());
        }
        catch (Exception ex)
        {
            Log.Warn($"Applying a tutorial chapter switch threw ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    /// <summary>Recomputes the menu footer line from whatever state we are in.</summary>
    private static string Describe()
    {
        try
        {
            if (TutorialRegistry.Count == 0)
                return "No tutorial chapters are registered.";

            var total = EnabledChapterCount;
            if (total == 0)
                return "Every tutorial chapter is switched off on the Tutorial tab.";

            if (!_sessionActive)
                return _pendingStart
                    ? $"Tutorial armed - {total} chapters. It begins when you load a game."
                    : $"Tutorial: {total} chapters. Load a game, then press Enable Quest.";

            var progress = TutorialProgress.Current;
            if (!progress.Started)
                return $"Tutorial: {total} chapters, not started.";

            var done = TutorialRegistry.Chapters.Count(
                c => TutorialSettings.IsEnabled(c.Id) && progress.IsChapterComplete(c.Id));
            var run = _active;

            if (run is null)
                return done >= total
                    ? $"Tutorial complete - all {total} chapters done."
                    : $"Tutorial {done}/{total} - preparing the next chapter.";

            var next = run.Steps.FirstOrDefault(s => !run.Quest.IsStepComplete(s.Id));
            var objective = next is null ? "wrapping up" : next.Title;
            return $"Tutorial {done + 1}/{total} - {objective}";
        }
        catch
        {
            return "Tutorial status unavailable.";
        }
    }

    private static string Announce(string status)
    {
        SetStatus(status);
        return status;
    }

    private static void SetStatus(string status)
    {
        if (string.Equals(_status, status, StringComparison.Ordinal))
            return;

        _status = status;

        try
        {
            StatusChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Debug($"A tutorial status listener threw ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    /// <summary>What one chapter slot is doing right now.</summary>
    private sealed class SlotRun
    {
        internal SlotRun(
            string slot,
            TutorialQuest quest,
            IReadOnlyList<TutorialStep> steps,
            IReadOnlyList<string> playedChapterIds)
        {
            Slot = slot;
            Quest = quest;
            Steps = steps;
            PlayedChapterIds = playedChapterIds;
            BuiltAtFrame = Time.frameCount;
        }

        internal string Slot { get; }

        internal TutorialQuest Quest { get; }

        internal IReadOnlyList<TutorialStep> Steps { get; }

        /// <summary>The chapters that actually put objectives in this quest, and therefore the only
        /// ones finishing it may mark complete.</summary>
        internal IReadOnlyList<string> PlayedChapterIds { get; }

        /// <summary>Frame the journal entries were added on; the quest is begun a few frames later.</summary>
        internal int BuiltAtFrame { get; set; }
    }

    /// <summary>One chapter's readiness plus when it was asked. See <see cref="AvailabilityOf"/>.</summary>
    private readonly struct CachedAvailability
    {
        internal CachedAvailability(float stamp, TutorialAvailability value)
        {
            Stamp = stamp;
            Value = value;
        }

        internal float Stamp { get; }

        internal TutorialAvailability Value { get; }
    }
}
