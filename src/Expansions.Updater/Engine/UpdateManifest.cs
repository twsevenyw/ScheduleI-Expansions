namespace Expansions.Updater.Engine;

/// <summary>How a manifest read ended. The three cases want three different responses.</summary>
internal enum ManifestOutcome
{
    /// <summary>Parsed and usable.</summary>
    Ok,

    /// <summary>Not JSON, or missing something the contract requires. A publishing bug or a bad proxy.</summary>
    Malformed,

    /// <summary>
    /// A <c>schemaVersion</c> this build does not know. The correct response is to do nothing, because a
    /// newer schema may mean the fields that are present no longer mean what this code thinks they mean.
    /// </summary>
    UnsupportedSchema,
}

/// <summary>The zip itself.</summary>
internal sealed class PackageInfo
{
    internal PackageInfo(string fileName, string url, long sizeBytes, string sha256)
    {
        FileName = fileName;
        Url = url;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
    }

    internal string FileName { get; }

    internal string Url { get; }

    internal long SizeBytes { get; }

    /// <summary>Verified after download and before extracting. A mismatch aborts; it never extracts anyway.</summary>
    internal string Sha256 { get; }
}

/// <summary>
/// A parsed <c>update-manifest.json</c>, the contract between <c>scripts/publish.ps1</c> and this
/// updater. The prose contract is <c>docs/UPDATE-MANIFEST.md</c>; the JSON Schema is
/// <c>docs/update-manifest.schema.json</c>.
/// <para>
/// Two rules govern how strictly this reads. Unknown <em>fields</em> are ignored, because the publisher
/// ships additive changes without bumping <see cref="SchemaVersion"/> and an updater that choked on a new
/// field would strand every client in the wild. An unknown <em>schema version</em> is refused outright,
/// because at that point the fields this code does understand may no longer mean what it assumes.
/// </para>
/// </summary>
internal sealed class UpdateManifest
{
    /// <summary>The one schema version this build understands.</summary>
    internal const int SupportedSchemaVersion = 1;

    /// <summary>Constant identifier the publisher stamps into every manifest.</summary>
    internal const string ExpectedSuite = "ScheduleI-Expansions";

    private UpdateManifest(
        int schemaVersion,
        string suite,
        string version,
        string tag,
        string releasedAt,
        string changelog,
        string releaseNotesUrl,
        string melonLoaderMinimum,
        string gameVersionTested,
        PackageInfo package,
        IReadOnlyList<ManifestEntry> mods,
        IReadOnlyList<ManifestEntry> bundledDependencies,
        IReadOnlyList<ManifestEntry> documents)
    {
        SchemaVersion = schemaVersion;
        Suite = suite;
        Version = version;
        Tag = tag;
        ReleasedAt = releasedAt;
        Changelog = changelog;
        ReleaseNotesUrl = releaseNotesUrl;
        MelonLoaderMinimum = melonLoaderMinimum;
        GameVersionTested = gameVersionTested;
        Package = package;
        Mods = mods;
        BundledDependencies = bundledDependencies;
        Documents = documents;
    }

    internal int SchemaVersion { get; }

    internal string Suite { get; }

    /// <summary>Suite version, SemVer, no leading <c>v</c>.</summary>
    internal string Version { get; }

    internal string Tag { get; }

    internal string ReleasedAt { get; }

    /// <summary>One plain-text paragraph, written to be read in-game. Never markdown. May be empty.</summary>
    internal string Changelog { get; }

    internal string ReleaseNotesUrl { get; }

    /// <summary>Oldest MelonLoader the release is known to load under.</summary>
    internal string MelonLoaderMinimum { get; }

    /// <summary>
    /// Game build the release was compiled and checked against. Advisory only — the player may
    /// legitimately be on a newer patch, so this is reported and never used to block anything.
    /// </summary>
    internal string GameVersionTested { get; }

    internal PackageInfo Package { get; }

    /// <summary>
    /// The suite's own assemblies. Iterated, never matched against a hardcoded list: a fifth mod can
    /// appear here without a <see cref="SchemaVersion"/> bump.
    /// </summary>
    internal IReadOnlyList<ManifestEntry> Mods { get; }

    /// <summary>Third-party assemblies in the zip. Installed only where absent.</summary>
    internal IReadOnlyList<ManifestEntry> BundledDependencies { get; }

    internal IReadOnlyList<ManifestEntry> Documents { get; }

    /// <summary>Every declared file, in install order: mods, then dependencies, then documents.</summary>
    internal IEnumerable<ManifestEntry> Payload => Mods.Concat(BundledDependencies).Concat(Documents);

    /// <summary>
    /// Reads a manifest. <paramref name="failure"/> is always populated on anything but
    /// <see cref="ManifestOutcome.Ok"/> and is written for a player to read, not a developer.
    /// </summary>
    internal static ManifestOutcome TryRead(string? json, out UpdateManifest? manifest, out string failure)
    {
        manifest = null;

        if (!Json.TryParse(json, out var root, out var parseFailure))
        {
            failure = $"the manifest is not readable JSON ({parseFailure})";
            return ManifestOutcome.Malformed;
        }

        if (!root.IsObject)
        {
            failure = "the manifest is not a JSON object";
            return ManifestOutcome.Malformed;
        }

        // Checked before anything else, and on its own: if the shape has changed under us, no other
        // field can be trusted to mean what this code assumes.
        var schemaVersion = (int)root.IntegerOr("schemaVersion", 0);
        if (schemaVersion != SupportedSchemaVersion)
        {
            failure = schemaVersion <= 0
                ? "the manifest does not declare a schema version"
                : $"the manifest uses schema version {schemaVersion} and this build only understands " +
                  $"{SupportedSchemaVersion}, so it cannot be applied safely";
            return ManifestOutcome.UnsupportedSchema;
        }

        var suite = root.StringOr("suite");
        if (!string.Equals(suite, ExpectedSuite, StringComparison.Ordinal))
        {
            failure = suite.Length == 0
                ? "the manifest does not say which suite it is for"
                : $"the manifest is for '{suite}', not '{ExpectedSuite}'";
            return ManifestOutcome.Malformed;
        }

        var version = root.StringOr("version");
        if (!SemVer.TryParse(version, out _))
        {
            failure = $"the manifest's suite version '{version}' is not SemVer";
            return ManifestOutcome.Malformed;
        }

        if (!TryReadPackage(root.Member("package"), out var package, out var packageFailure))
        {
            failure = packageFailure;
            return ManifestOutcome.Malformed;
        }

        if (!TryReadEntries(root, "mods", PayloadKind.Mod, out var mods, out failure) ||
            !TryReadEntries(root, "bundledDependencies", PayloadKind.Dependency, out var dependencies, out failure) ||
            !TryReadEntries(root, "documents", PayloadKind.Document, out var documents, out failure))
        {
            return ManifestOutcome.Malformed;
        }

        if (mods.Count == 0)
        {
            failure = "the manifest declares no mods, so there is nothing it could install";
            return ManifestOutcome.Malformed;
        }

        var requires = root.Member("requires") ?? JsonValue.Null;

        manifest = new UpdateManifest(
            schemaVersion,
            suite,
            version,
            root.StringOr("tag", "v" + version),
            root.StringOr("releasedAt"),
            root.StringOr("changelog"),
            root.StringOr("releaseNotesUrl"),
            requires.StringOr("melonLoaderMinimum"),
            requires.StringOr("gameVersionTested"),
            package!,
            mods,
            dependencies,
            documents);

        failure = string.Empty;
        return ManifestOutcome.Ok;
    }

    private static bool TryReadPackage(JsonValue? json, out PackageInfo? package, out string failure)
    {
        package = null;

        if (json is null || !json.IsObject)
        {
            failure = "the manifest does not describe its package";
            return false;
        }

        var url = json.StringOr("url");
        if (!IsHttps(url))
        {
            failure = url.Length == 0
                ? "the manifest's package has no download URL"
                : $"the package URL '{url}' is not https";
            return false;
        }

        var sha256 = json.StringOr("sha256");
        if (sha256.Length != 64)
        {
            failure = "the manifest's package does not carry a SHA-256";
            return false;
        }

        var sizeBytes = json.IntegerOr("sizeBytes", 0);
        if (sizeBytes <= 0)
        {
            failure = $"the manifest's package declares a size of {sizeBytes} bytes";
            return false;
        }

        var fileName = json.StringOr("fileName");
        if (fileName.Length == 0 || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            failure = $"the package file name '{fileName}' cannot be written to disk";
            return false;
        }

        package = new PackageInfo(fileName, url, sizeBytes, sha256);
        failure = string.Empty;
        return true;
    }

    private static bool TryReadEntries(
        JsonValue root,
        string member,
        PayloadKind kind,
        out IReadOnlyList<ManifestEntry> entries,
        out string failure)
    {
        var list = new List<ManifestEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in root.ArrayOr(member))
        {
            if (!ManifestEntry.TryRead(element, kind, out var entry, out var entryFailure))
            {
                entries = Array.Empty<ManifestEntry>();
                failure = $"{member}: {entryFailure}";
                return false;
            }

            // A duplicate id would make "which file is this?" ambiguous at install time, and the plan
            // would then contain two writes to one path.
            if (!seen.Add(entry!.Id))
            {
                entries = Array.Empty<ManifestEntry>();
                failure = $"{member}: '{entry.Id}' is declared twice";
                return false;
            }

            list.Add(entry);
        }

        entries = list;
        failure = string.Empty;
        return true;
    }

    private static bool IsHttps(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
}
