using System.Text;
using Expansions.Updater.Engine;

namespace Expansions.Updater.Tests;

/// <summary>
/// A throwaway directory shaped like a Schedule I install, plus the updater's own work folder.
/// <para>
/// The point of building this rather than mocking the file system is that every rule the applier has to
/// honour — replace in place, keep the <c>.disabled</c> suffix, never delete, roll back — is a statement
/// about files. A fake would let those statements be true of the fake and false of Windows.
/// </para>
/// </summary>
internal sealed class TestInstall : IDisposable
{
    private TestInstall(string root)
    {
        Root = root;
        GameRoot = Path.Combine(root, "game");
        Workspace = new UpdateWorkspace(Path.Combine(GameRoot, "UserData", UpdateWorkspace.FolderName));
        Layout = InstallLayout.UnderRoot(GameRoot);

        Directory.CreateDirectory(Path.Combine(GameRoot, "Mods"));
        Directory.CreateDirectory(Path.Combine(GameRoot, "Plugins"));
        Directory.CreateDirectory(Path.Combine(GameRoot, "UserLibs"));
        Directory.CreateDirectory(Workspace.Root);
    }

    internal string Root { get; }

    internal string GameRoot { get; }

    internal UpdateWorkspace Workspace { get; }

    internal InstallLayout Layout { get; }

    internal static TestInstall Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "expansions-updater-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new TestInstall(root);
    }

    internal string PathTo(string installDir, string fileName) =>
        installDir.Length == 0
            ? Path.Combine(GameRoot, fileName)
            : Path.Combine(GameRoot, installDir, fileName);

    /// <summary>Puts a file into the fake install, as if the player had it there already.</summary>
    internal void Install(string installDir, string fileName, string content)
    {
        var path = PathTo(installDir, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    internal bool Has(string installDir, string fileName) => File.Exists(PathTo(installDir, fileName));

    internal string Read(string installDir, string fileName) => File.ReadAllText(PathTo(installDir, fileName));

    /// <summary>Every file currently sitting in the updater's attic, by name.</summary>
    internal IReadOnlyList<string> AtticContents()
    {
        if (!Directory.Exists(Workspace.AtticDirectory))
            return Array.Empty<string>();

        return Directory
            .GetFiles(Workspace.AtticDirectory, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Select(static name => name!)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToList();
    }

    internal ReleaseBuilder Release(string version) => new(this, version);

    internal ApplyReport Apply() => UpdateApplier.Apply(Workspace, Layout, UpdateLog.Silent);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // A test that left a handle open should not fail the run over a temp folder.
        }
    }
}

/// <summary>Builds a staged release: the payload files on disk plus the manifest that declares them.</summary>
internal sealed class ReleaseBuilder
{
    private readonly TestInstall _install;
    private readonly string _version;
    private readonly List<Declared> _mods = new();
    private readonly List<Declared> _dependencies = new();
    private readonly List<Declared> _documents = new();

    private int _schemaVersion = 1;
    private string _suite = UpdateManifest.ExpectedSuite;
    private string _extraRootMembers = string.Empty;

    internal ReleaseBuilder(TestInstall install, string version)
    {
        _install = install;
        _version = version;
    }

    internal ReleaseBuilder Mod(string id, string installDir, string fileName, string version, string content)
    {
        _mods.Add(new Declared(id, id, version, fileName, installDir, content));
        return this;
    }

    internal ReleaseBuilder Dependency(string id, string installDir, string fileName, string content)
    {
        _dependencies.Add(new Declared(id, id, string.Empty, fileName, installDir, content));
        return this;
    }

    internal ReleaseBuilder Document(string fileName, string content)
    {
        _documents.Add(new Declared(fileName, fileName, string.Empty, fileName, string.Empty, content));
        return this;
    }

    internal ReleaseBuilder WithSchemaVersion(int schemaVersion)
    {
        _schemaVersion = schemaVersion;
        return this;
    }

    internal ReleaseBuilder WithSuite(string suite)
    {
        _suite = suite;
        return this;
    }

    /// <summary>Raw JSON spliced into the manifest root, for the "unknown fields are ignored" test.</summary>
    internal ReleaseBuilder WithExtraRootMembers(string json)
    {
        _extraRootMembers = json;
        return this;
    }

    /// <summary>
    /// Writes the payload and the manifest. <paramref name="corrupt"/> names a file whose bytes are
    /// changed after its hash was recorded, which is what a tampered or truncated download looks like.
    /// </summary>
    internal void Stage(string? corrupt = null, string? omit = null)
    {
        var payload = _install.Workspace.ResetPayload();
        var all = _mods.Concat(_dependencies).Concat(_documents).ToList();

        foreach (var declared in all)
        {
            var path = Path.Combine(payload, declared.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, declared.Content);

            declared.SizeBytes = new FileInfo(path).Length;
            declared.Sha256 = Hashing.File(path);
        }

        foreach (var declared in all)
        {
            var path = Path.Combine(payload, declared.RelativePath);

            if (corrupt is not null && declared.FileName == corrupt)
                File.WriteAllText(path, declared.Content + " tampered");

            if (omit is not null && declared.FileName == omit)
                File.Delete(path);
        }

        _install.Workspace.WriteStagedManifestJson(BuildManifest());
    }

    private string BuildManifest()
    {
        var builder = new StringBuilder();

        builder.Append("{\n");
        builder.Append($"  \"schemaVersion\": {_schemaVersion},\n");
        builder.Append($"  \"suite\": \"{_suite}\",\n");
        builder.Append($"  \"version\": \"{_version}\",\n");
        builder.Append($"  \"tag\": \"v{_version}\",\n");
        builder.Append("  \"changelog\": \"Test release.\",\n");

        if (_extraRootMembers.Length > 0)
            builder.Append("  ").Append(_extraRootMembers).Append(",\n");

        builder.Append("  \"package\": { \"fileName\": \"test.zip\", ");
        builder.Append("\"url\": \"https://example.invalid/test.zip\", ");
        builder.Append("\"sizeBytes\": 1024, ");
        builder.Append($"\"sha256\": \"{new string('a', 64)}\" }},\n");

        Array(builder, "mods", _mods, includeVersion: true);
        builder.Append(",\n");
        Array(builder, "bundledDependencies", _dependencies, includeVersion: false, thirdParty: true);
        builder.Append(",\n");
        Array(builder, "documents", _documents, includeVersion: false);
        builder.Append("\n}\n");

        return builder.ToString();
    }

    private static void Array(
        StringBuilder builder,
        string name,
        IReadOnlyList<Declared> entries,
        bool includeVersion,
        bool thirdParty = false)
    {
        builder.Append($"  \"{name}\": [");

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            builder.Append(i == 0 ? "\n" : ",\n");
            builder.Append("    { ");
            builder.Append($"\"id\": \"{entry.Id}\", ");
            builder.Append($"\"name\": \"{entry.Name}\", ");

            if (includeVersion)
                builder.Append($"\"version\": \"{entry.Version}\", ");

            builder.Append($"\"fileName\": \"{entry.FileName}\", ");
            builder.Append($"\"installDir\": \"{entry.InstallDir}\", ");
            builder.Append($"\"sizeBytes\": {entry.SizeBytes}, ");
            builder.Append($"\"sha256\": \"{entry.Sha256}\"");

            if (thirdParty)
                builder.Append(", \"thirdParty\": true, \"overwriteExisting\": false");

            builder.Append(" }");
        }

        builder.Append(entries.Count == 0 ? "]" : "\n  ]");
    }

    private sealed class Declared
    {
        internal Declared(string id, string name, string version, string fileName, string installDir, string content)
        {
            Id = id;
            Name = name;
            Version = version;
            FileName = fileName;
            InstallDir = installDir;
            Content = content;
        }

        internal string Id { get; }

        internal string Name { get; }

        internal string Version { get; }

        internal string FileName { get; }

        internal string InstallDir { get; }

        internal string Content { get; }

        internal long SizeBytes { get; set; }

        internal string Sha256 { get; set; } = string.Empty;

        internal string RelativePath =>
            InstallDir.Length == 0 ? FileName : Path.Combine(InstallDir, FileName);
    }
}
