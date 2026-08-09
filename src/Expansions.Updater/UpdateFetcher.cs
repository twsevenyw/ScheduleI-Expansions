using Expansions.Updater.Engine;

namespace Expansions.Updater;

/// <summary>
/// Looks for the next release and, if there is one, downloads and verifies it into the staging folder.
/// <para>
/// Runs on a thread-pool thread with a hard deadline and writes nothing outside
/// <c>UserData\Expansions.Update</c>. The staged payload is not installed here and is not installed this
/// session: <see cref="UpdateApplier"/> puts it in at the start of the next launch, in the window before
/// MelonLoader loads the mods. That is the whole reason the recipient never sees a prompt — by the time
/// the update matters, it has already happened.
/// </para>
/// </summary>
internal sealed class UpdateFetcher
{
    /// <summary>Refuses a package larger than this before a byte is written.</summary>
    private const long MaxPackageBytes = 256L * 1024 * 1024;

    private readonly UpdateWorkspace _workspace;
    private readonly InstallLayout _layout;
    private readonly UpdateLog _log;

    internal UpdateFetcher(UpdateWorkspace workspace, InstallLayout layout, UpdateLog log)
    {
        _workspace = workspace;
        _layout = layout;
        _log = log;
    }

    /// <summary>
    /// Runs a whole check. Never throws: every exit path leaves <paramref name="status"/> describing what
    /// happened, and the caller writes it out.
    /// </summary>
    internal void Run(ReleaseSource source, UpdateStatus status, CancellationToken cancellation)
    {
        status.LastCheckAt = UpdateStatus.Timestamp();

        try
        {
            var response = source.Fetch(cancellation);

            switch (response.Outcome)
            {
                case HttpOutcome.NotFound:
                    // The stable asset URL 404s until the first non-prerelease release exists. Quiet by
                    // design: there is nothing wrong and nothing for anyone to do.
                    status.Stage = UpdateStage.NoRelease;
                    status.Detail = "No release is published yet.";
                    _log.Info($"No release is published at {source.Describe()} yet. Nothing to do.");
                    return;

                case HttpOutcome.Offline:
                    status.Stage = UpdateStage.Offline;
                    status.Detail = "The release manifest could not be reached.";
                    _log.Info($"Could not reach the release manifest ({response.Failure}). Nothing was changed.");
                    return;

                case HttpOutcome.Failed:
                    status.Stage = UpdateStage.Failed;
                    status.Detail = $"The update check failed: {response.Failure}.";
                    _log.Warning(status.Detail);
                    return;
            }

            var outcome = UpdateManifest.TryRead(response.Body, out var manifest, out var failure);

            if (outcome != ManifestOutcome.Ok || manifest is null)
            {
                status.Stage = UpdateStage.Failed;
                status.Detail = outcome == ManifestOutcome.UnsupportedSchema
                    ? $"{failure} Update by hand from {source.Describe()}."
                    : $"The release manifest could not be used: {failure}.";
                _log.Warning(status.Detail);
                return;
            }

            status.LatestVersion = manifest.Version;
            status.Changelog = manifest.Changelog;
            status.ReleaseNotesUrl = manifest.ReleaseNotesUrl;
            status.DescribeInstall(manifest, _layout);

            var plan = InstallPlan.Build(manifest, _workspace.PayloadDirectory, _layout);
            var outstanding = plan.Steps.Count(static step => !step.AlreadySatisfied);

            foreach (var note in plan.Skipped)
                status.Notes.Add(note);

            if (outstanding == 0)
            {
                status.Stage = UpdateStage.UpToDate;
                status.Detail = $"Everything installed is up to date at version {manifest.Version}.";
                _log.Info(status.Detail);
                _workspace.ClearStaging();
                return;
            }

            // Half-applied updates are the normal case whenever a release touches the shared library:
            // the applier swapped what it could this launch and left the rest staged. Re-downloading a
            // payload that is already here and already verified would be pure waste, and clearing it to
            // do so would put a failed download between the two halves of an update.
            if (_workspace.HasStagedPayload && StagedVersionIs(manifest.Version))
            {
                status.Stage = UpdateStage.Staged;
                status.PendingFiles = outstanding;
                status.Detail = $"Version {manifest.Version} is already downloaded and finishes installing at the " +
                                "next launch.";
                _log.Info(status.Detail);
                return;
            }

            status.Stage = UpdateStage.Downloading;
            _log.Info($"Version {manifest.Version} is available ({outstanding} file(s) differ here). Downloading.");

            if (!TryStage(manifest, response.Body, cancellation, out var stageFailure))
            {
                _workspace.ClearStaging();
                status.Stage = UpdateStage.Failed;
                status.Detail = $"Version {manifest.Version} was not downloaded: {stageFailure}.";
                _log.Warning($"{status.Detail} Nothing in the game folder was touched.");
                return;
            }

            status.Stage = UpdateStage.Staged;
            status.PendingFiles = outstanding;
            status.Detail = $"Version {manifest.Version} is downloaded and verified. It installs itself the next " +
                            "time the game starts.";
            _log.Info(status.Detail);
        }
        catch (OperationCanceledException)
        {
            status.Stage = UpdateStage.Offline;
            status.Detail = "The update check ran out of time and was abandoned.";
            _log.Info(status.Detail);
        }
        catch (Exception ex)
        {
            status.Stage = UpdateStage.Failed;
            status.Detail = $"The update check threw ({ex.GetType().Name}: {ex.Message}).";
            _log.Warning(status.Detail);
        }
    }

    private bool StagedVersionIs(string version)
    {
        var json = _workspace.TryReadStagedManifestJson();

        return UpdateManifest.TryRead(json, out var staged, out _) == ManifestOutcome.Ok &&
               staged is not null &&
               string.Equals(staged.Version, version, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Downloads the zip, checks it against the manifest's hash <em>before opening it</em>, extracts only
    /// the declared files, and verifies each one. The manifest is written last, because its presence is
    /// what makes the payload count as staged.
    /// </summary>
    private bool TryStage(UpdateManifest manifest, string manifestJson, CancellationToken cancellation, out string failure)
    {
        if (manifest.Package.SizeBytes > MaxPackageBytes)
        {
            failure = $"the release package is {Hashing.Describe(manifest.Package.SizeBytes)}, which is larger " +
                      "than this updater will download";
            return false;
        }

        var zipPath = Path.Combine(_workspace.DownloadDirectory, manifest.Package.FileName);

        try
        {
            _workspace.ClearStaging();
            var payload = _workspace.ResetPayload();

            var download = UpdaterHttp.Download(
                manifest.Package.Url, zipPath, manifest.Package.SizeBytes, cancellation);

            if (!download.Succeeded)
            {
                failure = download.Outcome == HttpOutcome.Offline
                    ? "the download could not reach GitHub"
                    : $"the download failed ({download.Failure})";
                return false;
            }

            // Before extracting, never after. An archive whose hash does not match is not the
            // publisher's archive, and opening it at all is a decision this code declines to make.
            if (!Hashing.Matches(zipPath, manifest.Package.Sha256))
            {
                failure = "the download's SHA-256 does not match the manifest, so it was thrown away without " +
                          "being opened";
                return false;
            }

            if (!PayloadExtractor.TryExtract(zipPath, manifest, payload, _log, out failure))
                return false;

            _workspace.WriteStagedManifestJson(manifestJson);

            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = $"staging failed ({ex.GetType().Name}: {ex.Message})";
            return false;
        }
        finally
        {
            // The zip has served its purpose once the payload is verified out of it, and it is by far
            // the largest thing the updater writes.
            TryDelete(zipPath);
            TryDeleteDirectory(_workspace.DownloadDirectory);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _log.Debug($"Could not remove '{path}' ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            _log.Debug($"Could not clear '{path}' ({ex.GetType().Name}: {ex.Message}).");
        }
    }
}
