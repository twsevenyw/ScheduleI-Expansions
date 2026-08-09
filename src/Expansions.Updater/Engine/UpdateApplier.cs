using System.Globalization;

namespace Expansions.Updater.Engine;

internal enum ApplyOutcome
{
    /// <summary>No verified payload is waiting. The ordinary state.</summary>
    NothingStaged,

    /// <summary>A payload is staged and every file it declares is already what is on disk.</summary>
    AlreadyCurrent,

    /// <summary>Everything the release changes on this install is now in place.</summary>
    Applied,

    /// <summary>
    /// The files in folders MelonLoader had already loaded have been swapped and take effect on the next
    /// launch; the rest is deliberately held back until then so the loaded set is never half-updated.
    /// </summary>
    Prepared,

    /// <summary>Nothing was changed. Either the payload failed verification or a write failed and was undone.</summary>
    Aborted,
}

internal sealed class ApplyReport
{
    internal ApplyReport(
        ApplyOutcome outcome,
        string version,
        string detail,
        IReadOnlyList<string> changed,
        IReadOnlyList<string> skipped,
        IReadOnlyList<string> problems,
        bool consumeStaging,
        int heldBack = 0)
    {
        Outcome = outcome;
        Version = version;
        Detail = detail;
        Changed = changed;
        Skipped = skipped;
        Problems = problems;
        ConsumeStaging = consumeStaging;
        HeldBack = heldBack;
    }

    internal ApplyOutcome Outcome { get; }

    internal string Version { get; }

    internal string Detail { get; }

    /// <summary>One line per file actually written.</summary>
    internal IReadOnlyList<string> Changed { get; }

    /// <summary>One line per file deliberately left alone, with the reason.</summary>
    internal IReadOnlyList<string> Skipped { get; }

    internal IReadOnlyList<string> Problems { get; }

    /// <summary>True when the staging folder has served its purpose and should be cleared.</summary>
    internal bool ConsumeStaging { get; }

    /// <summary>Files deliberately left for the next launch. Non-zero only for <see cref="ApplyOutcome.Prepared"/>.</summary>
    internal int HeldBack { get; }

    internal static ApplyReport Nothing(string detail) => new(
        ApplyOutcome.NothingStaged, string.Empty, detail,
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), consumeStaging: false);
}

/// <summary>
/// Puts a staged release into the game folder.
/// <para>
/// This is the whole reason the updater is a MelonLoader <em>plugin</em>. MelonLoader loads
/// <c>UserLibs</c>, then <c>Plugins</c>, then raises <c>OnApplicationEarlyStart</c>, and only then loads
/// <c>Mods</c>. That gap is the one moment in the process's life when every mod assembly is still just a
/// file on disk, so a mod DLL can be replaced with a plain copy and the loader picks up the new one a
/// heartbeat later. Nothing has to wait for the game to close and no second process is involved.
/// </para>
/// <para>
/// The files in <c>UserLibs</c> and <c>Plugins</c> are already mapped by then, so they cannot be
/// overwritten — but they <em>can</em> be moved aside, which frees the path for the new file and lets the
/// swap happen a launch early. That is why an update that touches those folders lands in two passes:
/// pass one swaps the loaded folders and stops, pass two (next launch, with the new shared library
/// already live) does the mods. At no point is the set of assemblies actually running a mixture of two
/// releases.
/// </para>
/// <para>
/// Four rules are absolute, and each one is a test in <c>Expansions.Updater.Tests</c>. Every staged byte
/// is SHA-256 verified before anything is touched, and one bad hash abandons the whole update. A file
/// the player switched off is updated as <c>*.dll.disabled</c> and stays off. Nothing is ever deleted:
/// the file being replaced is moved into an attic under <c>UserData</c>, where it stays until a later
/// launch. And a failure at any point rolls every completed write back, so a partial apply is not a
/// state this can end in.
/// </para>
/// </summary>
internal static class UpdateApplier
{
    /// <summary>Suffix for the fully written, re-verified copy that is swapped into place last.</summary>
    private const string IncomingSuffix = ".expansions-incoming";

    internal static ApplyReport Apply(UpdateWorkspace workspace, InstallLayout layout, UpdateLog log)
    {
        if (!workspace.HasStagedPayload)
            return ApplyReport.Nothing("nothing is staged");

        var manifestJson = workspace.TryReadStagedManifestJson();
        var outcome = UpdateManifest.TryRead(manifestJson, out var manifest, out var manifestFailure);

        if (outcome != ManifestOutcome.Ok || manifest is null)
        {
            // Only this code ever writes into the staging folder, so a payload whose own manifest no
            // longer reads is corrupt rather than someone else's business. Clearing it is the fix.
            log.Warning($"The staged update could not be read ({manifestFailure}); it has been discarded and " +
                        "nothing in the game folder was touched.");

            return new ApplyReport(
                ApplyOutcome.Aborted, string.Empty, manifestFailure,
                Array.Empty<string>(), Array.Empty<string>(), new[] { manifestFailure }, consumeStaging: true);
        }

        var plan = InstallPlan.Build(manifest, workspace.PayloadDirectory, layout);

        // Verified before a single destination is considered, so a bad payload cannot get as far as
        // moving a file. This is the second hashing of these bytes; the first was at download time, and
        // an unknown amount of disk and time has passed since.
        if (!Verify(plan, log, out var verificationFailure))
        {
            log.Error($"The staged update failed verification ({verificationFailure}). Nothing was changed and " +
                      "the download has been discarded; it will be fetched again.");

            return new ApplyReport(
                ApplyOutcome.Aborted, manifest.Version, verificationFailure,
                Array.Empty<string>(), Array.Empty<string>(), new[] { verificationFailure }, consumeStaging: true);
        }

        var pending = plan.Steps.Where(static step => !step.AlreadySatisfied).ToList();
        var loaded = pending.Where(static step => step.Folder == FolderState.Loaded).ToList();

        // One pass or the other, never both. See the type comment: doing the mods in the same pass as a
        // shared library that only goes live next launch is what produces a mixed-version session.
        var batch = loaded.Count > 0 ? loaded : pending;
        var holdBack = loaded.Count > 0 ? pending.Count - loaded.Count : 0;

        if (batch.Count == 0)
        {
            return new ApplyReport(
                ApplyOutcome.AlreadyCurrent, manifest.Version,
                $"version {manifest.Version} is already installed",
                Array.Empty<string>(), plan.Skipped, plan.Problems, consumeStaging: true);
        }

        var attic = workspace.NewAtticRun();

        if (!Execute(batch, attic, log, out var changed, out var executionFailure))
        {
            return new ApplyReport(
                ApplyOutcome.Aborted, manifest.Version, executionFailure,
                Array.Empty<string>(), plan.Skipped, new[] { executionFailure }, consumeStaging: false);
        }

        if (holdBack > 0)
        {
            var detail =
                $"version {manifest.Version}: {Count(changed.Count, "shared file")} swapped, " +
                $"{Count(holdBack, "mod file")} held back until the next launch";

            return new ApplyReport(
                ApplyOutcome.Prepared, manifest.Version, detail, changed, plan.Skipped, plan.Problems,
                consumeStaging: false, heldBack: holdBack);
        }

        return new ApplyReport(
            ApplyOutcome.Applied, manifest.Version,
            $"version {manifest.Version}: {Count(changed.Count, "file")} updated",
            changed, plan.Skipped, plan.Problems, consumeStaging: true);
    }

    /// <summary>
    /// Hashes every file the plan intends to copy. Size is checked too, because a truncated file that
    /// somehow hashed the same is not a case worth reasoning about.
    /// </summary>
    private static bool Verify(InstallPlan plan, UpdateLog log, out string failure)
    {
        foreach (var step in plan.Steps)
        {
            var info = new FileInfo(step.Source);

            if (!info.Exists)
            {
                failure = $"the staged payload is missing '{step.Entry.ArchivePath}'";
                return false;
            }

            if (info.Length != step.Entry.SizeBytes)
            {
                failure = $"'{step.Entry.ArchivePath}' is {info.Length} bytes and the manifest says " +
                          $"{step.Entry.SizeBytes}";
                return false;
            }

            if (!Hashing.Matches(step.Source, step.Entry.Sha256))
            {
                failure = $"'{step.Entry.ArchivePath}' does not match its declared SHA-256";
                return false;
            }

            log.Debug($"Verified {step.Entry.ArchivePath}.");
        }

        failure = string.Empty;
        return true;
    }

    /// <summary>
    /// Writes the batch, or writes none of it.
    /// <para>
    /// Each file goes down in three moves: the new bytes are written beside the destination under a
    /// temporary name and re-hashed there, the file being replaced is moved into the attic, and only
    /// then does the temporary file take the destination's name. The destination therefore only ever
    /// holds a complete, verified file or the previous complete file — never a half-copy — and the
    /// previous one still exists on disk afterwards.
    /// </para>
    /// </summary>
    private static bool Execute(
        IReadOnlyList<InstallStep> batch,
        string atticRun,
        UpdateLog log,
        out IReadOnlyList<string> changed,
        out string failure)
    {
        var undo = new List<Action>();
        var notes = new List<string>();
        var index = 0;

        foreach (var step in batch)
        {
            var incoming = step.Destination + IncomingSuffix;
            index++;

            try
            {
                var directory = Path.GetDirectoryName(step.Destination);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                Delete(incoming);
                File.Copy(step.Source, incoming);

                // Re-hashed at the destination, not just at the source: this is the copy that is about
                // to become the mod the game loads, and a short write here would be silent otherwise.
                if (!Hashing.Matches(incoming, step.Entry.Sha256))
                    throw new IOException($"the copy of {step.Entry.FileName} did not match its SHA-256");

                if (File.Exists(step.Destination))
                {
                    var backup = Path.Combine(
                        atticRun,
                        index.ToString("00", CultureInfo.InvariantCulture) + "_" + Path.GetFileName(step.Destination));

                    Directory.CreateDirectory(atticRun);

                    // A move, not a delete. Windows allows a mapped assembly's file to be renamed even
                    // though it cannot be opened for writing, which is exactly what makes the UserLibs
                    // and Plugins pass possible - and it means the previous build is still recoverable.
                    File.Move(step.Destination, backup);

                    var destination = step.Destination;
                    undo.Add(() => File.Move(backup, destination, overwrite: true));
                }
                else
                {
                    var destination = step.Destination;
                    undo.Add(() => Delete(destination));
                }

                File.Move(incoming, step.Destination);

                notes.Add(step.Note);
                log.Debug($"Wrote {step.Destination}.");
            }
            catch (Exception ex)
            {
                Delete(incoming);
                Rollback(undo, log);

                changed = Array.Empty<string>();
                failure = $"{step.Entry.Name} could not be written ({ex.GetType().Name}: {ex.Message}); " +
                          "every file this update had already replaced was put back";
                return false;
            }
        }

        changed = notes;
        failure = string.Empty;
        return true;
    }

    private static void Rollback(List<Action> undo, UpdateLog log)
    {
        for (var i = undo.Count - 1; i >= 0; i--)
        {
            try
            {
                undo[i]();
            }
            catch (Exception ex)
            {
                // Nothing better is available here, and it is worth being loud about: the install is in
                // whatever state this left it, and the attic under UserData holds the originals.
                log.Error($"Rolling an update step back failed ({ex.GetType().Name}: {ex.Message}). The files " +
                          "that were replaced are still in the updater's attic folder.");
            }
        }
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static string Count(int value, string noun) =>
        value == 1 ? $"1 {noun}" : $"{value} {noun}s";
}
