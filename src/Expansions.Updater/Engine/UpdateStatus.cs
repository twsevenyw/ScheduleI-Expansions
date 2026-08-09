using System.Globalization;
using System.Text;

namespace Expansions.Updater.Engine;

/// <summary>
/// Where the updater has got to, in one word. Written into <c>status.json</c> and rendered verbatim by
/// the in-game menu, so these strings are part of a contract.
/// </summary>
internal static class UpdateStage
{
    internal const string Disabled = "disabled";
    internal const string Checking = "checking";
    internal const string NoRelease = "no-release";
    internal const string Offline = "offline";
    internal const string UpToDate = "up-to-date";
    internal const string Downloading = "downloading";
    internal const string Staged = "staged";
    internal const string Failed = "failed";
}

/// <summary>
/// The updater's report to anything that wants to display it.
/// <para>
/// A file rather than an API, and deliberately so. The updater is a plugin and
/// <c>Expansions.Core</c> is a shared library in <c>UserLibs</c>; a compile-time reference between them
/// in either direction would mean one assembly loading the other, and loading is precisely what puts a
/// file beyond replacing. A few hundred bytes of JSON under <c>UserData</c> costs nothing and keeps the
/// two completely independent — the menu still renders if the plugin is missing, and the updater still
/// works if the mods are not installed at all.
/// </para>
/// </summary>
internal sealed class UpdateStatus
{
    internal const int SchemaVersion = 1;

    internal string Stage { get; set; } = UpdateStage.Checking;

    /// <summary>One sentence, written to be read by a player.</summary>
    internal string Detail { get; set; } = string.Empty;

    internal string UpdaterVersion { get; set; } = string.Empty;

    internal string LastCheckAt { get; set; } = string.Empty;

    internal string LatestVersion { get; set; } = string.Empty;

    internal string Changelog { get; set; } = string.Empty;

    internal string ReleaseNotesUrl { get; set; } = string.Empty;

    /// <summary>What the last apply did, kept so the menu can say "updated since last session".</summary>
    internal string AppliedVersion { get; set; } = string.Empty;

    internal string AppliedAt { get; set; } = string.Empty;

    internal string AppliedDetail { get; set; } = string.Empty;

    /// <summary>Files the applier is holding back for the next launch. Zero in the normal case.</summary>
    internal int PendingFiles { get; set; }

    /// <summary>One entry per file the release declares, as it stands on this machine.</summary>
    internal List<StatusFile> Files { get; } = new();

    internal List<string> Notes { get; } = new();

    internal static string Timestamp() =>
        DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>Rebuilds <see cref="Files"/> from a manifest and what is on disk right now.</summary>
    internal void DescribeInstall(UpdateManifest manifest, InstallLayout layout)
    {
        Files.Clear();

        foreach (var entry in manifest.Payload)
        {
            if (entry.Kind == PayloadKind.Document)
                continue;

            var local = InstalledFile.Resolve(entry, layout);

            Files.Add(new StatusFile
            {
                Id = entry.Id,
                Name = entry.Name,
                FileName = entry.FileName,
                InstallDir = entry.InstallDir,
                State = local.State switch
                {
                    InstalledStateKind.Enabled => "enabled",
                    InstalledStateKind.Disabled => "disabled",
                    InstalledStateKind.Absent => "absent",
                    _ => "unknown",
                },
                InstalledVersion = local.LocalVersion,
                AvailableVersion = entry.Version,
            });
        }
    }

    /// <summary>
    /// Written whole and small. No partial-write protection because a status file that fails to parse
    /// simply reads as "the updater has not said anything yet", which is a correct answer.
    /// </summary>
    internal void Write(string path)
    {
        var builder = new StringBuilder(1024);

        builder.Append("{\n");
        Member(builder, "schemaVersion", SchemaVersion);
        Member(builder, "writtenAt", Timestamp());
        Member(builder, "updater", UpdaterVersion);
        Member(builder, "stage", Stage);
        Member(builder, "detail", Detail);
        Member(builder, "lastCheckAt", LastCheckAt);
        Member(builder, "latestVersion", LatestVersion);
        Member(builder, "changelog", Changelog);
        Member(builder, "releaseNotesUrl", ReleaseNotesUrl);
        Member(builder, "appliedVersion", AppliedVersion);
        Member(builder, "appliedAt", AppliedAt);
        Member(builder, "appliedDetail", AppliedDetail);
        Member(builder, "pendingFiles", PendingFiles);

        builder.Append("  \"notes\": [");
        for (var i = 0; i < Notes.Count; i++)
        {
            builder.Append(i == 0 ? "\n" : ",\n").Append("    \"").Append(Json.Escape(Notes[i])).Append('"');
        }

        builder.Append(Notes.Count == 0 ? "],\n" : "\n  ],\n");

        builder.Append("  \"files\": [");
        for (var i = 0; i < Files.Count; i++)
        {
            var file = Files[i];
            builder.Append(i == 0 ? "\n" : ",\n");
            builder.Append("    { ");
            builder.Append("\"id\": \"").Append(Json.Escape(file.Id)).Append("\", ");
            builder.Append("\"name\": \"").Append(Json.Escape(file.Name)).Append("\", ");
            builder.Append("\"fileName\": \"").Append(Json.Escape(file.FileName)).Append("\", ");
            builder.Append("\"installDir\": \"").Append(Json.Escape(file.InstallDir)).Append("\", ");
            builder.Append("\"state\": \"").Append(Json.Escape(file.State)).Append("\", ");
            builder.Append("\"installedVersion\": \"").Append(Json.Escape(file.InstalledVersion)).Append("\", ");
            builder.Append("\"availableVersion\": \"").Append(Json.Escape(file.AvailableVersion)).Append('"');
            builder.Append(" }");
        }

        builder.Append(Files.Count == 0 ? "]\n" : "\n  ]\n");
        builder.Append("}\n");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private static void Member(StringBuilder builder, string name, string value) =>
        builder.Append("  \"").Append(name).Append("\": \"").Append(Json.Escape(value)).Append("\",\n");

    private static void Member(StringBuilder builder, string name, int value) =>
        builder.Append("  \"").Append(name).Append("\": ")
               .Append(value.ToString(CultureInfo.InvariantCulture)).Append(",\n");
}

internal sealed class StatusFile
{
    internal string Id { get; set; } = string.Empty;

    internal string Name { get; set; } = string.Empty;

    internal string FileName { get; set; } = string.Empty;

    internal string InstallDir { get; set; } = string.Empty;

    /// <summary><c>enabled</c>, <c>disabled</c>, <c>absent</c> or <c>unknown</c>.</summary>
    internal string State { get; set; } = string.Empty;

    internal string InstalledVersion { get; set; } = string.Empty;

    internal string AvailableVersion { get; set; } = string.Empty;
}
