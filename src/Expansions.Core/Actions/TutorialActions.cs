using Expansions.Core.Tutorial;

namespace Expansions.Core.Actions;

/// <summary>
/// The tutorial quest line, moved out of the screen footer and onto the Actions tab so it sits beside
/// everything else the owner can do. <see cref="TutorialDirector"/> already returns the status line it
/// wants shown, so these are thin.
/// </summary>
internal static class TutorialActions
{
    internal static ExpansionAction StartOrRestart() => ExpansionAction.WithDynamicLabel(
        id: "core.tutorial.start",
        label: static () => TutorialDirector.IsStarted ? "Restart the tutorial quest line" : "Enable the tutorial quest line",
        description: "Runs the enabled chapters as real quests. Switch individual chapters on and off " +
                     "from the Tutorial tab. From the main menu it arms itself and begins on load.",
        isAvailable: () => TutorialDirector.ChapterCount == 0
            ? ActionAvailability.Unavailable("no tutorial chapters are registered")
            : TutorialDirector.EnabledChapterCount == 0
                ? ActionAvailability.Unavailable("every chapter is switched off on the Tutorial tab")
                : ActionAvailability.Ready,
        invoke: () => ActionResult.Ok(TutorialDirector.IsStarted ? TutorialDirector.Restart() : TutorialDirector.Start()),
        order: 30);

    internal static ExpansionAction Reset() => new(
        id: "core.tutorial.reset",
        label: "Reset tutorial progress",
        description: "Cancels the live chapter quest and wipes the recorded progress for this save.",
        isAvailable: () => TutorialDirector.ChapterCount == 0
            ? ActionAvailability.Unavailable("no tutorial chapters are registered")
            : ActionAvailability.Ready,
        invoke: () => ActionResult.Ok(TutorialDirector.Reset()),
        order: 31);

    internal static ExpansionAction ShowStatus() => new(
        id: "core.tutorial.status",
        label: "Show tutorial status",
        description: "Which chapter is live and which objective you are on.",
        isAvailable: null,
        invoke: () =>
        {
            var status = TutorialDirector.Status;
            var chapter = TutorialDirector.CurrentChapterId;
            var objective = TutorialDirector.CurrentObjectiveId;

            if (chapter.Length > 0)
                ActionLog.Note($"Chapter '{chapter}', objective '{(objective.Length > 0 ? objective : "none pending")}'.");

            return ActionResult.Ok(status.Length > 0 ? status : "The tutorial has no status to report.");
        },
        order: 32);
}
