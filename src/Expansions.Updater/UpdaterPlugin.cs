using Expansions.Updater;
using Expansions.Updater.Engine;
using MelonLoader;
using MelonLoader.Utils;

// A plugin, not a mod: MelonPlugin derives from MelonTypeBase<MelonPlugin>, and MelonLoader loads
// Plugins in Core.Initialize() while Mods do not arrive until part-way through Core.Start(). Getting
// this wrong is silent - a MelonMod placed in Plugins is simply never registered.
[assembly: MelonInfo(typeof(UpdaterPlugin), UpdaterPlugin.Name, UpdaterPlugin.Version, "Evan")]
[assembly: MelonGame("TVGS", "Schedule I")]

// Lower runs earlier. The whole point of this plugin is to be the first thing that touches the mod
// folder, before any other plugin has a chance to look at what is in it.
[assembly: MelonPriority(-1000)]

namespace Expansions.Updater;

/// <summary>
/// Keeps the Schedule I Expansions install current, with nobody being asked anything.
/// <para>
/// Two things happen, in this order, every launch:
/// </para>
/// <list type="number">
///   <item>
///     Anything a previous session downloaded and verified is swapped into place. This runs from
///     <c>OnApplicationEarlyStart</c>, which MelonLoader raises in <c>Core.Start()</c> <em>before</em>
///     <c>MelonFolderHandler.LoadMelons(ScanType.Mods)</c> — so every mod DLL is still an ordinary file
///     and can be replaced with a plain copy. There is no sidecar process, nothing waits for the game to
///     close, and the player is never told to restart.
///   </item>
///   <item>
///     A check for the next release starts on a thread-pool thread and the callback returns immediately.
///     Whatever it finds is downloaded, verified and left staged for the next launch to install.
///   </item>
/// </list>
/// <para>
/// Nothing in here can delay a launch and nothing in here can fail a launch: every path is wrapped, the
/// network has a hard deadline, and an offline machine or an unpublished repository is a log line.
/// </para>
/// </summary>
public sealed class UpdaterPlugin : MelonPlugin
{
    internal const string Name = "Expansions Updater";

    /// <summary>Kept in step with <c>&lt;Version&gt;</c> in the csproj, which is what the publisher reads.</summary>
    internal const string Version = "1.0.0";

    /// <summary>
    /// The whole check, end to end. Long enough for a slow connection to pull a megabyte of zip, short
    /// enough that a black-holed route cannot leave a thread alive for a session.
    /// </summary>
    private static readonly TimeSpan CheckDeadline = TimeSpan.FromMinutes(5);

    private CancellationTokenSource? _cancellation;

    public override void OnApplicationEarlyStart()
    {
        try
        {
            Run();
        }
        catch (Exception ex)
        {
            // Belt and braces. Run() already catches everything it can; this is here so that a bug in
            // the updater cannot be the reason someone's game does not start.
            LoggerInstance.Error($"The updater did not start ({ex.GetType().Name}: {ex.Message}). Nothing was " +
                                 "changed and the rest of the suite is unaffected.");
        }
    }

    public override void OnApplicationQuit()
    {
        try
        {
            _cancellation?.Cancel();
        }
        catch
        {
            // Cancelling a source that has already been disposed is not worth a line.
        }

        UpdaterHttp.Shutdown();
    }

    private void Run()
    {
        var settings = UpdaterSettings.Read(Path.Combine(MelonEnvironment.UserDataDirectory, "Expansions.cfg"));
        var log = BuildLog(settings.VerboseLogging);

        var workspace = new UpdateWorkspace(
            Path.Combine(MelonEnvironment.UserDataDirectory, UpdateWorkspace.FolderName));

        var layout = new InstallLayout(
            MelonEnvironment.GameRootDirectory,
            MelonEnvironment.ModsDirectory,
            MelonEnvironment.PluginsDirectory,
            MelonEnvironment.UserLibsDirectory);

        UpdaterHttp.UserAgentVersion = Version;

        // Done first, and at the start of a launch rather than the end of one, so the build an update
        // replaced is still on disk for the whole session after it was replaced.
        var swept = workspace.SweepAttic();
        if (swept > 0)
            log.Debug($"Cleared {swept} folder(s) of superseded files from the updater's attic.");

        var status = new UpdateStatus { UpdaterVersion = Version };

        if (!settings.AutoUpdate || settings.ManifestUrl.Length == 0)
        {
            status.Stage = UpdateStage.Disabled;
            status.Detail = settings.ManifestUrl.Length == 0
                ? "'update_manifest_url' is empty in Expansions.cfg, so the updater does nothing."
                : "'auto_update' is off in Expansions.cfg, so the updater does nothing.";

            log.Info(status.Detail);
            Write(workspace, status);
            return;
        }

        ApplyStaged(workspace, layout, log, status);

        if (!ReleaseSource.TryResolve(settings, out var source, out var reason))
        {
            status.Stage = UpdateStage.Disabled;
            status.Detail = $"Nothing is checked: {reason}.";
            log.Warning(status.Detail);
            Write(workspace, status);
            return;
        }

        // Written before the check so the menu has something to show even if the check is still running,
        // or if the process is closed while it is.
        status.Stage = UpdateStage.Checking;
        Write(workspace, status);

        var cancellation = _cancellation = new CancellationTokenSource(CheckDeadline);
        var fetcher = new UpdateFetcher(workspace, layout, log);

        // The launch continues from here. Nothing below this line is waited on.
        Task.Run(() =>
        {
            try
            {
                fetcher.Run(source!, status, cancellation.Token);
            }
            catch (Exception ex)
            {
                status.Stage = UpdateStage.Failed;
                status.Detail = $"The update check threw ({ex.GetType().Name}: {ex.Message}).";
                log.Warning(status.Detail);
            }
            finally
            {
                Write(workspace, status);
            }
        });
    }

    /// <summary>
    /// Installs whatever the last session left staged. Synchronous on purpose: this has to finish before
    /// MelonLoader loads the mods, and it is a handful of local file copies.
    /// </summary>
    private void ApplyStaged(UpdateWorkspace workspace, InstallLayout layout, UpdateLog log, UpdateStatus status)
    {
        if (!workspace.HasStagedPayload)
            return;

        var report = UpdateApplier.Apply(workspace, layout, log);

        if (report.ConsumeStaging)
            workspace.ClearStaging();

        foreach (var note in report.Changed)
            log.Info($"Updated: {note}.");

        foreach (var note in report.Skipped)
            log.Debug($"Left alone: {note}");

        foreach (var problem in report.Problems)
            log.Warning(problem);

        switch (report.Outcome)
        {
            case ApplyOutcome.Applied:
                status.AppliedVersion = report.Version;
                status.AppliedAt = UpdateStatus.Timestamp();
                status.AppliedDetail = report.Detail;
                log.Info($"Updated to {report.Version} before the mods loaded. Nothing was deleted, and a mod you " +
                         "switched off is still switched off.");
                break;

            case ApplyOutcome.Prepared:
                status.AppliedVersion = report.Version;
                status.AppliedAt = UpdateStatus.Timestamp();
                status.AppliedDetail = report.Detail;
                status.PendingFiles = report.HeldBack;
                log.Info($"Version {report.Version} is half in: the shared library and this plugin were already " +
                         "loaded when the update arrived, so they were swapped for the next launch and the mods " +
                         "are held back until then. Both halves go live together; nothing is asked of you.");
                break;

            case ApplyOutcome.AlreadyCurrent:
                log.Debug($"The staged payload for {report.Version} matches what is installed; discarded.");
                break;

            case ApplyOutcome.Aborted:
                log.Warning($"The staged update was not installed: {report.Detail}. The install is exactly as it " +
                            "was.");
                break;
        }
    }

    private static void Write(UpdateWorkspace workspace, UpdateStatus status)
    {
        try
        {
            status.Write(workspace.StatusPath);
        }
        catch
        {
            // The status file is a convenience for the in-game menu. Failing to write it changes nothing
            // about whether the install is up to date.
        }
    }

    private UpdateLog BuildLog(bool verbose) => new((level, message) =>
    {
        switch (level)
        {
            case UpdateLogLevel.Error:
                LoggerInstance.Error(message);
                break;
            case UpdateLogLevel.Warning:
                LoggerInstance.Warning(message);
                break;
            case UpdateLogLevel.Info:
                LoggerInstance.Msg(message);
                break;
            default:
                if (verbose)
                    LoggerInstance.Msg(message);
                break;
        }
    });
}
