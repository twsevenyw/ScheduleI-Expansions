namespace Expansions.Updater.Engine;

/// <summary>One file to copy, resolved to an absolute destination.</summary>
internal sealed class InstallStep
{
    internal InstallStep(ManifestEntry entry, string source, string destination, FolderState folder, string note)
    {
        Entry = entry;
        Source = source;
        Destination = destination;
        Folder = folder;
        Note = note;
    }

    internal ManifestEntry Entry { get; }

    /// <summary>Absolute path of the verified file inside the staging folder.</summary>
    internal string Source { get; }

    /// <summary>Absolute destination. Carries the <c>.disabled</c> suffix when the local file had one.</summary>
    internal string Destination { get; }

    /// <summary>Whether the destination folder's assemblies are already loaded this session.</summary>
    internal FolderState Folder { get; }

    /// <summary>One clause explaining this step, for the log and the status file.</summary>
    internal string Note { get; }

    /// <summary>True when the destination already holds exactly these bytes, so there is nothing to do.</summary>
    internal bool AlreadySatisfied => Hashing.Matches(Destination, Entry.Sha256);
}

/// <summary>
/// The complete decision about what a staged release will and will not touch on this machine.
/// <para>
/// Built before anything is written, because the interesting part of an update on a modded install is
/// what it <em>leaves alone</em>: a mod they never installed, a mod they switched off, a newer S1API
/// they are running on purpose.
/// </para>
/// </summary>
internal sealed class InstallPlan
{
    private InstallPlan(
        UpdateManifest manifest,
        IReadOnlyList<InstallStep> steps,
        IReadOnlyList<string> skipped,
        IReadOnlyList<string> problems)
    {
        Manifest = manifest;
        Steps = steps;
        Skipped = skipped;
        Problems = problems;
    }

    internal UpdateManifest Manifest { get; }

    internal IReadOnlyList<InstallStep> Steps { get; }

    /// <summary>Files deliberately left alone, each with the reason. Not failures.</summary>
    internal IReadOnlyList<string> Skipped { get; }

    /// <summary>Things that stopped a file being planned and should not have. Rare; worth showing.</summary>
    internal IReadOnlyList<string> Problems { get; }

    /// <summary>
    /// Decides every file's fate. <paramref name="payloadRoot"/> is the folder holding the verified
    /// files; the plan only references them, so it can be built and inspected without anything being
    /// copied.
    /// </summary>
    internal static InstallPlan Build(UpdateManifest manifest, string payloadRoot, InstallLayout layout)
    {
        var steps = new List<InstallStep>();
        var skipped = new List<string>();
        var problems = new List<string>();

        foreach (var entry in manifest.Payload)
        {
            var local = InstalledFile.Resolve(entry, layout);

            if (local.State == InstalledStateKind.Unknown)
            {
                problems.Add($"{entry.Name}: the '{entry.InstallDir}' folder could not be resolved on this install.");
                continue;
            }

            var source = Path.Combine(payloadRoot, entry.RelativePath);
            var folder = InstallLayout.StateOf(entry.InstallDir);

            switch (entry.Kind)
            {
                case PayloadKind.Mod:
                    PlanMod(entry, local, source, folder, steps, skipped);
                    break;

                case PayloadKind.Dependency:
                    PlanDependency(entry, local, source, folder, steps, skipped);
                    break;

                default:
                    // Documents are informational text in the game root. Overwriting them is safe and is
                    // the point: they are the install runbook and the feature reference for this release.
                    steps.Add(new InstallStep(
                        entry, source, local.EnabledPath, folder, $"{entry.FileName} refreshed"));
                    break;
            }
        }

        return new InstallPlan(manifest, steps, skipped, problems);
    }

    private static void PlanMod(
        ManifestEntry entry,
        InstalledFile local,
        string source,
        FolderState folder,
        List<InstallStep> steps,
        List<string> skipped)
    {
        if (!local.Exists)
        {
            // The release is a whole-suite package, but this machine does not have this mod. Adding it
            // would hand the player a mod they never installed - which, for the mods that create NPCs,
            // means new save-persisted content appearing unasked. The zip stays available for anyone who
            // does want it.
            skipped.Add($"{entry.Name} is in the release but not installed here, so it is left out.");
            return;
        }

        if (entry.Version.Length > 0 && !local.IsOutdated)
        {
            var comparison = SemVer.Compare(local.LocalVersion, entry.Version) > 0 ? "newer than" : "already";
            skipped.Add($"{entry.Name} on disk is {comparison} {entry.Version}, so it is left alone.");
            return;
        }

        var note = local.IsDisabled
            ? $"{entry.Name} {Describe(local.LocalVersion)} to {entry.Version}, staying disabled"
            : $"{entry.Name} {Describe(local.LocalVersion)} to {entry.Version}";

        if (local.HasBothVariants)
            note += " (the stale .disabled copy beside it is untouched)";

        steps.Add(new InstallStep(entry, source, local.TargetPath, folder, note));
    }

    private static void PlanDependency(
        ManifestEntry entry,
        InstalledFile local,
        string source,
        FolderState folder,
        List<InstallStep> steps,
        List<string> skipped)
    {
        // overwriteExisting is false for every dependency the publisher currently ships, and it means
        // exactly what it says. A player may be running a newer S1API on purpose, and other mods on
        // their machine bind against the copy they already have.
        if (!entry.OverwriteExisting && local.Exists)
        {
            skipped.Add($"{entry.Name} is already installed, so your copy is kept.");
            return;
        }

        var destination = local.Exists ? local.TargetPath : local.EnabledPath;

        steps.Add(new InstallStep(
            entry,
            source,
            destination,
            folder,
            local.Exists ? $"{entry.Name} replaced" : $"{entry.Name} installed (it was missing)"));
    }

    private static string Describe(string version) => version.Length > 0 ? version : "an unread version";
}
