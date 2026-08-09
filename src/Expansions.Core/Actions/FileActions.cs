using Expansions.Core.Configuration;
using Expansions.Core.Diagnostics;
using Expansions.Core.Tutorial;

namespace Expansions.Core.Actions;

/// <summary>
/// Gets the owner to the files. The settings file and the probe reports are both edited or read
/// outside the game, and a path printed to a log they cannot see is not a route to either.
/// </summary>
internal static class FileActions
{
    internal static ExpansionAction OpenSettings() => new(
        id: "core.files.settings",
        label: "Reveal Expansions.cfg",
        description: "Opens Explorer on the settings file, for the options with no toggle on this screen.",
        isAvailable: () => ExpansionConfig.FilePath.Length == 0
            ? ActionAvailability.Unavailable("the settings file path is not known yet")
            : ActionAvailability.Ready,
        invoke: () =>
        {
            var path = ExpansionConfig.FilePath;
            ActionLog.Ok($"Settings file: {path}");

            var revealed = File.Exists(path)
                ? ShellReveal.File(path, out var fileFailure)
                : ShellReveal.Folder(Path.GetDirectoryName(path) ?? string.Empty, out fileFailure);

            return revealed
                ? ActionResult.Ok("Revealed Expansions.cfg in Explorer.")
                : ActionResult.Failed($"Could not open Explorer ({fileFailure}). The path above is still correct.");
        },
        order: 40);

    internal static ExpansionAction OpenUserData() => new(
        id: "core.files.userdata",
        label: "Reveal the UserData folder",
        description: "Where every probe report, journal and quarantine list is written.",
        isAvailable: null,
        invoke: () =>
        {
            // One of the guide chapter's objectives is "find this folder", and pressing this button is the
            // only evidence of that there is. Counted rather than signalled so a press made before that
            // objective opened does not complete it.
            TutorialActivity.UserDataRevealed();

            var directory = ProbeRunner.OutputDirectory;
            ActionLog.Ok($"UserData: {directory}");

            return ShellReveal.Folder(directory, out var failure)
                ? ActionResult.Ok("Opened the UserData folder.")
                : ActionResult.Failed($"Could not open Explorer ({failure}). The path above is still correct.");
        },
        order: 41);
}
