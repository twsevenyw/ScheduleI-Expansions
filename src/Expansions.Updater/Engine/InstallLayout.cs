namespace Expansions.Updater.Engine;

/// <summary>
/// Whether the assemblies in a folder are already loaded by the time the updater gets to run.
/// <para>
/// This is not a guess. Read out of MelonLoader 0.7.3's own IL: <c>Core.Initialize()</c> calls
/// <c>MelonFolderHandler.ScanForFolders()</c> and then <c>LoadMelons(UserLibs)</c> and
/// <c>LoadMelons(Plugins)</c>; only afterwards does <c>Core.Start()</c> raise
/// <c>OnApplicationEarlyStart</c>, and <c>LoadMelons(Mods)</c> comes after that. Loading goes through
/// <c>AssemblyLoadContext.Default.LoadFromAssemblyPath</c>, which maps the file and holds it for the
/// life of the process.
/// </para>
/// </summary>
internal enum FolderState
{
    /// <summary>
    /// Nothing in here is loaded yet when the plugin runs, so a file can simply be replaced and the
    /// new one is what MelonLoader loads a moment later. <c>Mods</c> and the game root.
    /// </summary>
    Free,

    /// <summary>
    /// Already loaded and mapped. The file cannot be opened for writing, but it <em>can</em> be moved
    /// aside — the runtime maps assemblies with delete-sharing — which frees the path for the new one.
    /// The replacement takes effect on the next launch. <c>UserLibs</c> and <c>Plugins</c>.
    /// </summary>
    Loaded,
}

/// <summary>
/// Maps the manifest's <c>installDir</c> values onto real directories, and says which of them the
/// loader has already got its hands on.
/// </summary>
internal sealed class InstallLayout
{
    private readonly IReadOnlyDictionary<string, string> _directories;

    internal InstallLayout(string gameRoot, string modsDirectory, string pluginsDirectory, string userLibsDirectory)
        : this(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [""] = gameRoot,
            ["Mods"] = modsDirectory,
            ["Plugins"] = pluginsDirectory,
            ["UserLibs"] = userLibsDirectory,
        })
    {
    }

    private InstallLayout(IReadOnlyDictionary<string, string> directories) => _directories = directories;

    /// <summary>
    /// A layout where every folder sits under one root, laid out the way a real install is. Used by the
    /// tests, and by nothing else.
    /// </summary>
    internal static InstallLayout UnderRoot(string root) => new(
        root,
        Path.Combine(root, "Mods"),
        Path.Combine(root, "Plugins"),
        Path.Combine(root, "UserLibs"));

    /// <summary>
    /// Absolute destination directory for a manifest <c>installDir</c>. Returns false for anything the
    /// manifest reader has not already accepted, which is the second half of the guard against a
    /// manifest that tries to write outside the game folder.
    /// </summary>
    internal bool TryResolve(string installDir, out string absolute)
    {
        if (_directories.TryGetValue(installDir, out var found) && !string.IsNullOrEmpty(found))
        {
            absolute = found;
            return true;
        }

        absolute = string.Empty;
        return false;
    }

    internal static FolderState StateOf(string installDir) => installDir switch
    {
        "UserLibs" or "Plugins" => FolderState.Loaded,
        _ => FolderState.Free,
    };
}
