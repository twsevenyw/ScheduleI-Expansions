namespace Expansions.Core.Tutorial;

/// <summary>
/// One chapter of the tutorial quest line. Core ships chapters for its own surfaces; each feature
/// mod contributes its own the same way it contributes probes:
/// <code>Lifetime.Add(TutorialRegistry.Register(new MyChapter()));</code>
/// <para>
/// Disposing the registration removes the chapter outright. There is no placeholder underneath it: a
/// mod that is not installed contributes nothing to the line rather than an entry the player cannot
/// play, and a mod switched off mid-session reports that through
/// <see cref="TutorialAvailability.ComingSoon"/> so the chapter is deferred rather than skipped.
/// </para>
/// <para>
/// Nothing here is ever completed on the player's behalf. A chapter that cannot be played contributes no
/// objectives and is not recorded as done, whether it was withdrawn, switched off, or reported not ready —
/// so it is still there to play, in full, whenever it becomes possible.
/// </para>
/// </summary>
public interface ITutorialChapter
{
    /// <summary>Stable and unique. Feature mods should use their module id.</summary>
    string Id { get; }

    /// <summary>Quest title in the journal.</summary>
    string Title { get; }

    /// <summary>Quest description in the journal. Two or three sentences.</summary>
    string Description { get; }

    /// <summary>Ascending. Core's own chapters occupy 100-450; the feature mods 500 upwards.</summary>
    int Order { get; }

    /// <summary>
    /// Polled continuously — while the Tutorial tab is open, before the chapter's quest is created, and for as
    /// long as that quest is live. Return <see cref="TutorialAvailability.ComingSoon"/> with a player-facing
    /// reason when the thing this chapter teaches cannot be done yet; the line plays on and comes back once it
    /// can. Must be cheap and must not mutate the world; answers are cached briefly.
    /// <para>
    /// A false is never final and never costs the player the chapter. Answer honestly rather than defensively.
    /// </para>
    /// </summary>
    TutorialAvailability GetAvailability();

    /// <summary>
    /// Declares the objectives. Called once per quest creation, so conditions may capture per-run
    /// state. Not called at all while <see cref="GetAvailability"/> says the chapter is not ready.
    /// Must declare at least one step: a chapter that builds none is treated as not ready.
    /// </summary>
    void BuildSteps(ITutorialChapterBuilder builder);
}

/// <summary>Collects the steps of one chapter. See <see cref="ITutorialChapter.BuildSteps"/>.</summary>
public interface ITutorialChapterBuilder
{
    /// <summary><paramref name="id"/> is chapter-local; the builder prefixes the chapter id.</summary>
    ITutorialStepBuilder AddStep(string id, string title);
}

/// <summary>Fluent configuration of a single objective.</summary>
public interface ITutorialStepBuilder
{
    ITutorialStepBuilder Describe(string hint);

    /// <summary>Polled every frame. Must be cheap and must not mutate the world.</summary>
    ITutorialStepBuilder CompletesWhen(Func<bool> condition);

    /// <summary>
    /// Completed by <see cref="TutorialSignals.Raise(string)"/> instead of polling — the right choice
    /// when a Harmony patch already knows the moment the objective was met.
    /// </summary>
    ITutorialStepBuilder CompletesOnSignal();

    /// <summary>Adds a compass marker at <paramref name="poi"/>.</summary>
    ITutorialStepBuilder At(UnityEngine.Vector3 poi);

    /// <summary>Runs <paramref name="grant"/> once, immediately after the objective ticks off.</summary>
    ITutorialStepBuilder Rewards(string description, Action grant);
}
