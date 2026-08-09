namespace Expansions.Core.Tutorial;

/// <summary>
/// One chapter of the tutorial quest line. Core ships chapters for the shipped surfaces; each feature
/// mod contributes its own the same way it contributes probes:
/// <code>Lifetime.Add(TutorialRegistry.Register(new MyChapter()));</code>
/// <para>
/// Registering a chapter whose <see cref="Id"/> matches one of Core's built-in placeholders replaces
/// it, and disposing the registration puts the placeholder back — so a feature mod being toggled off
/// mid-session degrades to "coming soon" rather than leaving a hole in the line.
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

    /// <summary>Ascending. Core's own chapters occupy 100, 200, 300, 400; the features 500 upwards.</summary>
    int Order { get; }

    /// <summary>
    /// Called every time the chapter's quest is about to be created. Return
    /// <see cref="TutorialAvailability.ComingSoon"/> when the feature is not implemented or not
    /// enabled, and the chapter collapses to a single self-completing objective carrying the reason.
    /// </summary>
    TutorialAvailability GetAvailability();

    /// <summary>
    /// Declares the objectives. Called once per quest creation, so conditions may capture per-run
    /// state. Not called at all when <see cref="GetAvailability"/> says the feature is unavailable.
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
