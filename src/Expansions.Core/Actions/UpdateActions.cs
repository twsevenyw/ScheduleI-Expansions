using Expansions.Core.Tutorial;
using Expansions.Core.Updates;

namespace Expansions.Core.Actions;

/// <summary>
/// The update surface, which is deliberately read-only.
/// <para>
/// There used to be Check, Download and Apply here. There is nothing to press any more: the
/// <c>Expansions.Updater</c> plugin checks in the background every launch and installs what it found in
/// the window before MelonLoader loads the mods, so by the time this screen can be opened the install is
/// already current. What is left is the answer to "did that work?", which is a different question and
/// still worth being able to ask.
/// </para>
/// </summary>
internal static class UpdateActions
{
    internal static ExpansionAction ShowStatus() => new(
        id: "core.update.status",
        label: "Update status",
        description: "What the updater did at startup, when it last checked, and what the newest release is. " +
                     "Updates install themselves; there is nothing here to trigger.",
        isAvailable: static () => ActionAvailability.Available(UpdateReport.Current.StatusLine),
        invoke: static () =>
        {
            // The guide chapter asks for this once; opening it is the only evidence of it. Counted
            // rather than signalled so an earlier look does not tick the objective off on sight.
            TutorialActivity.UpdateStatusViewed();

            var report = UpdateReport.Current;

            foreach (var line in report.Describe())
                ActionLog.Note(line);

            if (!report.PluginInstalled)
            {
                return ActionResult.Failed(
                    $"Plugins\\{UpdatePaths.PluginFileName} is missing, so this install will not update itself. " +
                    "Re-run the installer, or copy that one file across from the release zip.");
            }

            return ActionResult.Ok(report.StatusLine);
        },
        order: 50);

    internal static ExpansionAction ShowVersions() => new(
        id: "core.update.versions",
        label: "Show installed versions",
        description: "One line per file: what is on disk, whether it is switched off, and what the newest " +
                     "release carries.",
        isAvailable: null,
        invoke: static () =>
        {
            var report = UpdateReport.Current;

            foreach (var component in report.Components)
                ActionLog.Note(component.Describe());

            if (report.Components.Count == 0)
                ActionLog.Note("Nothing belonging to the suite was found in Mods, Plugins or UserLibs.");

            return ActionResult.Ok(report.StatusLine);
        },
        order: 51);

    internal static ExpansionAction RevealUpdateFolder() => new(
        id: "core.update.reveal",
        label: "Reveal the update folder",
        description: $"UserData\\{UpdatePaths.WorkFolderName} - what the updater downloaded, what it reported, " +
                     "and an attic holding the files the last update replaced.",
        isAvailable: static () => Directory.Exists(UpdatePaths.WorkDirectory)
            ? ActionAvailability.Ready
            : ActionAvailability.Unavailable("the updater has not written anything yet"),
        invoke: static () =>
        {
            var directory = UpdatePaths.WorkDirectory;
            ActionLog.Ok($"Update folder: {directory}");

            return ShellReveal.Folder(directory, out var failure)
                ? ActionResult.Ok("Opened the update folder.")
                : ActionResult.Failed($"Could not open Explorer ({failure}). The path above is still correct.");
        },
        order: 52);
}
