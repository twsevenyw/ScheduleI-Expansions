using MelonLoader.Utils;

namespace Expansions.Core.Updates;

/// <summary>
/// Where the updater keeps its work, and where the suite's own files live.
/// <para>
/// Install directories come from MelonLoader's own environment rather than from string concatenation on
/// the game root, so a non-standard layout resolves the way the loader itself resolves it.
/// </para>
/// </summary>
internal static class UpdatePaths
{
    /// <summary>Everything the updater writes lives under this one folder in UserData.</summary>
    internal const string WorkFolderName = "Expansions.Update";

    /// <summary>The plugin that does the updating. Its absence is worth reporting.</summary>
    internal const string PluginFileName = "Expansions.Updater.dll";

    internal static string GameRoot => Safe(static () => MelonEnvironment.GameRootDirectory);

    internal static string WorkDirectory =>
        Path.Combine(Safe(static () => MelonEnvironment.UserDataDirectory), WorkFolderName);

    /// <summary>The updater's report on itself. Written by the plugin, read here, never written here.</summary>
    internal static string StatusPath => Path.Combine(WorkDirectory, "status.json");

    internal static string ModsDirectory => Safe(static () => MelonEnvironment.ModsDirectory);

    internal static string PluginsDirectory => Safe(static () => MelonEnvironment.PluginsDirectory);

    internal static string UserLibsDirectory => Safe(static () => MelonEnvironment.UserLibsDirectory);

    /// <summary>True when the update plugin is installed. False means nothing updates itself.</summary>
    internal static bool PluginInstalled
    {
        get
        {
            try
            {
                var directory = PluginsDirectory;
                return directory.Length > 0 && File.Exists(Path.Combine(directory, PluginFileName));
            }
            catch
            {
                return false;
            }
        }
    }

    internal static string ResolveInstallDir(string installDir) => installDir switch
    {
        "" => GameRoot,
        "Mods" => ModsDirectory,
        "Plugins" => PluginsDirectory,
        "UserLibs" => UserLibsDirectory,
        _ => string.Empty,
    };

    /// <summary>
    /// MelonLoader's environment properties are populated during its own startup. They have never been
    /// observed empty from a mod, but reading one is not worth a throw on a path that is meant to fail
    /// quietly, so every read goes through here.
    /// </summary>
    private static string Safe(Func<string> read)
    {
        try
        {
            return read() ?? string.Empty;
        }
        catch (Exception ex)
        {
            ExpansionHost.Log.Debug($"A MelonLoader path could not be read ({ex.GetType().Name}: {ex.Message}).");
            return string.Empty;
        }
    }
}
