using System.Diagnostics;

namespace Expansions.Updater.Engine;

/// <summary>What a declared file looks like on this machine right now.</summary>
internal enum InstalledStateKind
{
    /// <summary>Neither the enabled nor the disabled file exists.</summary>
    Absent,

    /// <summary>Present and loadable.</summary>
    Enabled,

    /// <summary>Present as <c>*.dll.disabled</c> — deliberately switched off by the player.</summary>
    Disabled,

    /// <summary>The install directory could not be resolved, so nothing can be said about the file.</summary>
    Unknown,
}

/// <summary>
/// Resolves one <see cref="ManifestEntry"/> against the local install: is it here, is it switched off,
/// and what version is on disk.
/// <para>
/// The disabled case is the whole reason this type exists. Any mod may legitimately be sitting as
/// <c>*.dll.disabled</c>, and an updater that matched only <c>*.dll</c> would write a fresh enabled DLL
/// beside the disabled one and silently turn a mod back on that the player deliberately turned off. So
/// the suffix is detected and carried through to <see cref="TargetPath"/>: the file is updated where it
/// actually is, keeping its state.
/// </para>
/// <para>
/// Nothing here ever deletes. Removing a mod DLL orphans that mod's NPC folders inside the player's
/// saves, and a save-persisted custom NPC cannot be unmade — so the rule is replace in place, and the
/// advice to players is disable, never delete.
/// </para>
/// </summary>
internal sealed class InstalledFile
{
    /// <summary>The suffix that switches a mod off without removing it.</summary>
    internal const string DisabledSuffix = ".disabled";

    private InstalledFile(
        ManifestEntry entry,
        InstalledStateKind state,
        string directory,
        string enabledPath,
        string disabledPath,
        bool hasBothVariants,
        string localVersion)
    {
        Entry = entry;
        State = state;
        Directory = directory;
        EnabledPath = enabledPath;
        DisabledPath = disabledPath;
        HasBothVariants = hasBothVariants;
        LocalVersion = localVersion;
    }

    internal ManifestEntry Entry { get; }

    internal InstalledStateKind State { get; }

    internal string Directory { get; }

    internal string EnabledPath { get; }

    internal string DisabledPath { get; }

    /// <summary>
    /// Both <c>Foo.dll</c> and <c>Foo.dll.disabled</c> exist. The enabled one is what the loader binds,
    /// so that is the one updated; the stale disabled copy is left exactly where it is.
    /// </summary>
    internal bool HasBothVariants { get; }

    /// <summary>
    /// The version stamped into the file on disk, read the same way the publisher reads it when
    /// packaging — <c>FileVersionInfo.ProductVersion</c>, falling back to the file version. Empty when
    /// the file is absent or carries no version resource.
    /// </summary>
    internal string LocalVersion { get; }

    internal bool Exists => State is InstalledStateKind.Enabled or InstalledStateKind.Disabled;

    internal bool IsDisabled => State == InstalledStateKind.Disabled;

    /// <summary>Where an update to this file must be written. Preserves the disabled suffix.</summary>
    internal string TargetPath => IsDisabled ? DisabledPath : EnabledPath;

    /// <summary>True when the release carries a strictly newer build of this file than the local one.</summary>
    internal bool IsOutdated =>
        Exists && Entry.Version.Length > 0 && SemVer.IsNewer(Entry.Version, LocalVersion);

    internal static InstalledFile Resolve(ManifestEntry entry, InstallLayout layout)
    {
        if (!layout.TryResolve(entry.InstallDir, out var directory))
        {
            return new InstalledFile(
                entry, InstalledStateKind.Unknown, string.Empty, string.Empty, string.Empty, false, string.Empty);
        }

        var enabledPath = Path.Combine(directory, entry.FileName);
        var disabledPath = enabledPath + DisabledSuffix;

        var enabledExists = FileExists(enabledPath);
        var disabledExists = FileExists(disabledPath);

        var state = enabledExists
            ? InstalledStateKind.Enabled
            : disabledExists
                ? InstalledStateKind.Disabled
                : InstalledStateKind.Absent;

        var localVersion = state switch
        {
            InstalledStateKind.Enabled => ReadVersion(enabledPath),
            InstalledStateKind.Disabled => ReadVersion(disabledPath),
            _ => string.Empty,
        };

        return new InstalledFile(
            entry,
            state,
            directory,
            enabledPath,
            disabledPath,
            enabledExists && disabledExists,
            localVersion);
    }

    /// <summary>One line for the version report.</summary>
    internal string DescribeState() => State switch
    {
        InstalledStateKind.Enabled => LocalVersion.Length > 0 ? LocalVersion : "installed",
        InstalledStateKind.Disabled => (LocalVersion.Length > 0 ? LocalVersion : "installed") + " (disabled)",
        InstalledStateKind.Absent => "not installed",
        _ => "unknown location",
    };

    private static bool FileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch
        {
            // An unreadable directory reads as absent, which only means the file is left alone.
            return false;
        }
    }

    /// <summary>
    /// Reads the assembly's version out of its PE resources. This works on a DLL the process has
    /// already loaded and on one renamed to <c>.disabled</c>, which <c>Assembly.LoadFrom</c>-style reads
    /// would not: it is a plain shared read of the file header.
    /// </summary>
    private static string ReadVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);

            var product = info.ProductVersion?.Trim() ?? string.Empty;
            if (product.Length > 0)
                return product;

            return info.FileVersion?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
