using System.Diagnostics;
using System.Globalization;

namespace Expansions.Core.Updates;

/// <summary>One file of the suite, as it stands on this machine.</summary>
public sealed class InstalledComponent
{
    internal InstalledComponent(string name, string fileName, string installDir, string state, string installed, string available)
    {
        Name = name;
        FileName = fileName;
        InstallDir = installDir;
        State = state;
        InstalledVersion = installed;
        AvailableVersion = available;
    }

    public string Name { get; }

    public string FileName { get; }

    public string InstallDir { get; }

    /// <summary><c>enabled</c>, <c>disabled</c>, <c>absent</c> or <c>unknown</c>.</summary>
    public string State { get; }

    public string InstalledVersion { get; }

    /// <summary>What the newest release carries, when a manifest has been read. Empty otherwise.</summary>
    public string AvailableVersion { get; }

    public bool IsOutdated =>
        AvailableVersion.Length > 0 && SemVer.IsNewer(AvailableVersion, InstalledVersion);

    /// <summary>One line for the output pane.</summary>
    public string Describe()
    {
        var version = InstalledVersion.Length > 0 ? InstalledVersion : "an unreadable version";

        var line = State switch
        {
            "absent" => $"{Name}: not installed here",
            "disabled" => $"{Name}: {version}, switched off",
            "unknown" => $"{Name}: its folder could not be resolved",
            _ => $"{Name}: {version}",
        };

        return IsOutdated ? $"{line} - {AvailableVersion} is available" : line;
    }
}

/// <summary>
/// What the update plugin has been doing, read off disk.
/// <para>
/// Core does not check for updates, download them or install them, and it deliberately cannot. All of
/// that belongs to <c>Expansions.Updater</c>, a MelonLoader plugin, because plugins load before mods and
/// that is the only moment in a launch when a mod DLL can be replaced. Core's job here is to be able to
/// answer "what happened?" if someone asks — nothing on this screen is a step anyone has to take.
/// </para>
/// <para>
/// The two are joined by a small JSON file under <c>UserData</c> and by nothing else. A compile-time
/// reference either way would load the other assembly, and a loaded assembly is exactly what cannot be
/// updated.
/// </para>
/// </summary>
public sealed class UpdateReport
{
    /// <summary>Re-read at most this often. Availability predicates ask for it on every frame.</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(2);

    private static readonly object Gate = new();

    private static UpdateReport? _cached;
    private static DateTime _readAt = DateTime.MinValue;

    private UpdateReport(
        bool pluginInstalled,
        bool statusRead,
        string stage,
        string detail,
        string lastCheckAt,
        string latestVersion,
        string changelog,
        string releaseNotesUrl,
        string appliedVersion,
        string appliedDetail,
        int pendingFiles,
        IReadOnlyList<InstalledComponent> components)
    {
        PluginInstalled = pluginInstalled;
        StatusRead = statusRead;
        Stage = stage;
        Detail = detail;
        LastCheckAt = lastCheckAt;
        LatestVersion = latestVersion;
        Changelog = changelog;
        ReleaseNotesUrl = releaseNotesUrl;
        AppliedVersion = appliedVersion;
        AppliedDetail = appliedDetail;
        PendingFiles = pendingFiles;
        Components = components;
    }

    /// <summary>False means nothing on this machine updates itself, which is worth saying out loud.</summary>
    public bool PluginInstalled { get; }

    /// <summary>False when the plugin has not written a status yet — normally only on a first launch.</summary>
    public bool StatusRead { get; }

    /// <summary>The plugin's own word: <c>up-to-date</c>, <c>no-release</c>, <c>offline</c>, and so on.</summary>
    public string Stage { get; }

    public string Detail { get; }

    public string LastCheckAt { get; }

    public string LatestVersion { get; }

    public string Changelog { get; }

    public string ReleaseNotesUrl { get; }

    /// <summary>Set when this launch installed something before the mods loaded.</summary>
    public string AppliedVersion { get; }

    public string AppliedDetail { get; }

    /// <summary>Files still to go in. Non-zero only between the two passes of a shared-library update.</summary>
    public int PendingFiles { get; }

    public IReadOnlyList<InstalledComponent> Components { get; }

    /// <summary>One sentence, written for the output pane and for a button's description.</summary>
    public string StatusLine
    {
        get
        {
            if (!PluginInstalled)
            {
                return $"Automatic updates are not installed - Plugins\\{UpdatePaths.PluginFileName} is missing, " +
                       "so nothing here updates itself.";
            }

            if (!StatusRead)
                return "The updater has not reported yet. It runs at startup, before the mods load.";

            if (Detail.Length > 0)
                return Detail;

            return Stage switch
            {
                "disabled" => "Update checks are switched off.",
                "checking" => "Checking for a newer release.",
                "no-release" => "No release is published yet.",
                "offline" => "The release manifest could not be reached.",
                "up-to-date" => "Everything installed is up to date.",
                "downloading" => "Downloading a newer release.",
                "staged" => "A newer release is downloaded and installs itself at the next launch.",
                "failed" => "The last update check did not finish.",
                _ => "The updater has nothing to report.",
            };
        }
    }

    public static UpdateReport Current
    {
        get
        {
            lock (Gate)
            {
                if (_cached is not null && DateTime.UtcNow - _readAt < CacheFor)
                    return _cached;

                _cached = Read();
                _readAt = DateTime.UtcNow;
                return _cached;
            }
        }
    }

    /// <summary>Everything worth printing, in the order it is worth reading.</summary>
    public IReadOnlyList<string> Describe()
    {
        var lines = new List<string> { StatusLine };

        if (AppliedVersion.Length > 0)
        {
            lines.Add($"This launch installed version {AppliedVersion} before the mods loaded" +
                      (AppliedDetail.Length > 0 ? $" ({AppliedDetail})." : "."));
        }

        if (PendingFiles > 0)
        {
            lines.Add("Part of that update goes in at the next launch: the shared library was already loaded when " +
                      "it arrived, so it and the mods are swapped one launch apart to keep them in step. Nothing " +
                      "is needed from you.");
        }

        if (LatestVersion.Length > 0)
            lines.Add($"Newest published release: {LatestVersion}.");

        if (Changelog.Length > 0)
            lines.Add(Changelog);

        if (ReleaseNotesUrl.Length > 0)
            lines.Add(ReleaseNotesUrl);

        if (LastCheckAt.Length > 0)
            lines.Add($"Last checked {Humanise(LastCheckAt)}.");

        foreach (var component in Components)
            lines.Add(component.Describe());

        return lines;
    }

    private static UpdateReport Read()
    {
        var pluginInstalled = UpdatePaths.PluginInstalled;
        var json = TryReadStatus();

        if (json is null || !Json.TryParse(json, out var root, out _) || !root.IsObject)
            return new UpdateReport(
                pluginInstalled, false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty, 0, ScanInstalled());

        var components = new List<InstalledComponent>();

        foreach (var file in root.ArrayOr("files"))
        {
            components.Add(new InstalledComponent(
                file.StringOr("name", file.StringOr("fileName")),
                file.StringOr("fileName"),
                file.StringOr("installDir"),
                file.StringOr("state", "unknown"),
                file.StringOr("installedVersion"),
                file.StringOr("availableVersion")));
        }

        // A status file written before any manifest was read carries no file list. Falling back to a
        // scan means the versions panel still answers the question it exists to answer.
        if (components.Count == 0)
            components.AddRange(ScanInstalled());

        return new UpdateReport(
            pluginInstalled,
            true,
            root.StringOr("stage"),
            root.StringOr("detail"),
            root.StringOr("lastCheckAt"),
            root.StringOr("latestVersion"),
            root.StringOr("changelog"),
            root.StringOr("releaseNotesUrl"),
            root.StringOr("appliedVersion"),
            root.StringOr("appliedDetail"),
            (int)root.IntegerOr("pendingFiles", 0),
            components);
    }

    private static string? TryReadStatus()
    {
        try
        {
            var path = UpdatePaths.StatusPath;
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex)
        {
            ExpansionHost.Log.Debug($"The updater's status file could not be read ({ex.GetType().Name}).");
            return null;
        }
    }

    /// <summary>
    /// What is on disk, found by looking rather than by consulting a list. A fifth expansion mod appears
    /// here the moment someone drops it in, which is the same reason the publisher discovers projects
    /// instead of enumerating them.
    /// </summary>
    private static IReadOnlyList<InstalledComponent> ScanInstalled()
    {
        var found = new List<InstalledComponent>();

        Scan("UserLibs", UpdatePaths.UserLibsDirectory, found);
        Scan("Plugins", UpdatePaths.PluginsDirectory, found);
        Scan("Mods", UpdatePaths.ModsDirectory, found);

        return found;
    }

    private static void Scan(string installDir, string directory, List<InstalledComponent> found)
    {
        if (directory.Length == 0 || !Directory.Exists(directory))
            return;

        try
        {
            foreach (var path in Directory.GetFiles(directory, "Expansions.*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(path);
                var disabled = name.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase);

                if (!disabled && !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                var fileName = disabled ? name[..^".disabled".Length] : name;

                found.Add(new InstalledComponent(
                    Path.GetFileNameWithoutExtension(fileName),
                    fileName,
                    installDir,
                    disabled ? "disabled" : "enabled",
                    ReadVersion(path),
                    string.Empty));
            }
        }
        catch (Exception ex)
        {
            ExpansionHost.Log.Debug($"'{directory}' could not be scanned ({ex.GetType().Name}).");
        }
    }

    /// <summary>
    /// Read out of the PE resources, which works on a DLL this process has already loaded and on one
    /// renamed to <c>.disabled</c>. It is also exactly how the publisher reads the version when it builds
    /// the manifest, so the two cannot disagree.
    /// </summary>
    private static string ReadVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var product = info.ProductVersion?.Trim() ?? string.Empty;

            return product.Length > 0 ? product : info.FileVersion?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Humanise(string timestamp)
    {
        if (!DateTime.TryParse(
                timestamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var when))
        {
            return timestamp;
        }

        var ago = DateTime.UtcNow - when;

        return ago switch
        {
            { TotalMinutes: < 2 } => "a moment ago",
            { TotalHours: < 1 } => $"{(int)ago.TotalMinutes} minutes ago",
            { TotalDays: < 1 } => $"{(int)ago.TotalHours} hours ago",
            _ => when.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture),
        };
    }
}
