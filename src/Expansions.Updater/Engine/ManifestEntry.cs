namespace Expansions.Updater.Engine;

/// <summary>Which of the manifest's three payload arrays an entry came from.</summary>
internal enum PayloadKind
{
    /// <summary>The suite's own assemblies. Replaced in place on update.</summary>
    Mod,

    /// <summary>A third-party assembly redistributed inside the zip, such as S1API.</summary>
    Dependency,

    /// <summary>A plain-text file that sits in the game root.</summary>
    Document,
}

/// <summary>
/// One file the release declares: an entry of <c>mods[]</c>, <c>bundledDependencies[]</c> or
/// <c>documents[]</c>. All three share a shape, so all three parse into this.
/// <para>
/// <see cref="InstallDir"/> is load-bearing twice over. It is the destination directory relative to the
/// game root, <em>and</em> it is the file's path inside the zip — so the archive layout and the install
/// layout are the same thing and the updater never has to guess where an extracted file belongs.
/// </para>
/// </summary>
internal sealed class ManifestEntry
{
    /// <summary>
    /// The only destinations the contract allows. Anything else is refused rather than sanitised: the
    /// manifest arrives over the network, and a path this code has not been told about is either a
    /// publisher bug or an attempt to write outside the game folder. Neither deserves a best effort.
    /// </summary>
    private static readonly string[] AllowedInstallDirs = { string.Empty, "Mods", "Plugins", "UserLibs" };

    private ManifestEntry(
        PayloadKind kind,
        string id,
        string name,
        string version,
        string fileName,
        string installDir,
        long sizeBytes,
        string sha256,
        bool thirdParty,
        bool overwriteExisting)
    {
        Kind = kind;
        Id = id;
        Name = name;
        Version = version;
        FileName = fileName;
        InstallDir = installDir;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        ThirdParty = thirdParty;
        OverwriteExisting = overwriteExisting;
    }

    internal PayloadKind Kind { get; }

    /// <summary>
    /// Stable identifier, lowercase word characters. Everything keys off this and never off
    /// <see cref="Name"/>: <c>police_overhaul</c> is displayed as "Police Improvements", so the display
    /// name is not an identifier.
    /// </summary>
    internal string Id { get; }

    /// <summary>Display name. Shown to the player, never matched on.</summary>
    internal string Name { get; }

    /// <summary>
    /// SemVer, read out of the compiled assembly when the release was packaged — so it cannot disagree
    /// with what actually shipped. Empty for dependencies and documents, which carry no version.
    /// </summary>
    internal string Version { get; }

    /// <summary>Bare file name, no directory separators.</summary>
    internal string FileName { get; }

    /// <summary>One of <c>""</c>, <c>Mods</c>, <c>Plugins</c>, <c>UserLibs</c>. Empty means the game root.</summary>
    internal string InstallDir { get; }

    internal long SizeBytes { get; }

    /// <summary>Lowercase hex SHA-256 of the file as it appears inside the zip.</summary>
    internal string Sha256 { get; }

    internal bool ThirdParty { get; }

    /// <summary>
    /// False for every dependency the publisher currently ships, and it means what it says: install only
    /// if the destination is absent. A player may be running a newer S1API on purpose, and other mods on
    /// their machine depend on the copy they have.
    /// </summary>
    internal bool OverwriteExisting { get; }

    /// <summary>The entry's path inside the zip. Forward slashes, because that is what zip entries use.</summary>
    internal string ArchivePath =>
        InstallDir.Length == 0 ? FileName : InstallDir + "/" + FileName;

    /// <summary>The same path with the platform separator, for use under the staging folder.</summary>
    internal string RelativePath =>
        InstallDir.Length == 0 ? FileName : Path.Combine(InstallDir, FileName);

    /// <summary>
    /// Reads one entry. <paramref name="failure"/> explains the refusal; unknown members are ignored so
    /// the publisher can add fields without a schema bump.
    /// </summary>
    internal static bool TryRead(JsonValue json, PayloadKind kind, out ManifestEntry? entry, out string failure)
    {
        entry = null;

        if (!json.IsObject)
        {
            failure = "an entry is not a JSON object";
            return false;
        }

        // Documents carry no id, so they are named after their file. Nothing keys off a document id.
        var id = json.StringOr("id");
        if (id.Length == 0 && kind == PayloadKind.Document)
            id = json.StringOr("fileName");

        if (!IsIdentifier(id))
        {
            failure = $"'{id}' is not a usable entry id";
            return false;
        }

        var fileName = json.StringOr("fileName");
        if (!IsSafeFileName(fileName))
        {
            failure = $"'{id}' declares the unusable file name '{fileName}'";
            return false;
        }

        var installDir = json.StringOr("installDir");
        if (!TryCanonicalInstallDir(installDir, out var canonicalInstallDir))
        {
            failure = $"'{id}' installs to '{installDir}', which is not one of \"\", Mods, Plugins or UserLibs";
            return false;
        }

        var sha256 = json.StringOr("sha256");
        if (!IsSha256(sha256))
        {
            failure = $"'{id}' does not carry a 64-character lowercase hex SHA-256";
            return false;
        }

        var sizeBytes = json.IntegerOr("sizeBytes", 0);
        if (sizeBytes <= 0)
        {
            failure = $"'{id}' declares a size of {sizeBytes} bytes";
            return false;
        }

        var version = json.StringOr("version");
        if (kind == PayloadKind.Mod && !SemVer.TryParse(version, out _))
        {
            failure = $"'{id}' declares the version '{version}', which is not SemVer";
            return false;
        }

        var name = json.StringOr("name");
        if (name.Length == 0)
            name = fileName;

        entry = new ManifestEntry(
            kind,
            id,
            name,
            version,
            fileName,
            canonicalInstallDir,
            sizeBytes,
            sha256,
            json.BooleanOr("thirdParty", kind == PayloadKind.Dependency),
            json.BooleanOr("overwriteExisting", kind != PayloadKind.Dependency));

        failure = string.Empty;
        return true;
    }

    private static bool IsIdentifier(string value) =>
        value.Length is > 0 and <= 64 &&
        value.All(static c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.');

    private static bool IsSafeFileName(string value) =>
        value.Length is > 0 and <= 128 &&
        value.IndexOf('/') < 0 &&
        value.IndexOf('\\') < 0 &&
        !value.Contains("..", StringComparison.Ordinal) &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !string.Equals(value, ".", StringComparison.Ordinal);

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool TryCanonicalInstallDir(string value, out string canonical)
    {
        foreach (var allowed in AllowedInstallDirs)
        {
            if (string.Equals(value, allowed, StringComparison.OrdinalIgnoreCase))
            {
                canonical = allowed;
                return true;
            }
        }

        canonical = string.Empty;
        return false;
    }
}
