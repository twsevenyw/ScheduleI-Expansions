namespace Expansions.Core.Diagnostics;

/// <summary>
/// Snapshot of "how far into the game are we", taken once per probe run. Most probes are meaningless
/// in the main menu, and the harness must say so rather than reaching for a singleton that is not
/// there yet.
/// </summary>
public sealed class GameSessionState
{
    private GameSessionState(bool isSaveLoaded, string sceneName, string saveFolderPath, string detail)
    {
        IsSaveLoaded = isSaveLoaded;
        SceneName = sceneName;
        SaveFolderPath = saveFolderPath;
        Detail = detail;
    }

    public bool IsSaveLoaded { get; }

    public string SceneName { get; }

    public string SaveFolderPath { get; }

    /// <summary>Human-readable reason behind <see cref="IsSaveLoaded"/>.</summary>
    public string Detail { get; }

    public static GameSessionState Capture()
    {
        var scene = GameReflection.ActiveSceneName();

        if (!GameReflection.TryGetSingleton("Il2CppScheduleOne.Persistence.LoadManager", out var loadManager, out var failure))
        {
            // No LoadManager at all: fall back to the scene name. `Main` and `Tutorial` are the two
            // gameplay scenes; `Menu` is the front end.
            var sceneLooksLoaded = scene is "Main" or "Tutorial";
            return new GameSessionState(
                sceneLooksLoaded,
                scene,
                string.Empty,
                $"LoadManager unavailable ({failure}); judged from active scene '{Describe(scene)}'.");
        }

        var loaded = ReadFlag(loadManager, "IsGameLoaded");
        var inGameScene = ReadFlag(loadManager, "IsInGameScene");
        var loading = ReadFlag(loadManager, "IsLoading");

        GameReflection.TryRead(loadManager, "LoadedGameFolderPath", out var folder, out _);
        GameReflection.TryRead(loadManager, "LoadStatus", out var status, out _);

        var isLoaded = loaded == true && loading != true;
        var detail =
            $"LoadManager.IsGameLoaded={Describe(loaded)}, IsInGameScene={Describe(inGameScene)}, " +
            $"IsLoading={Describe(loading)}, LoadStatus={GameReflection.Format(status)}, scene='{Describe(scene)}'.";

        return new GameSessionState(isLoaded, scene, folder as string ?? string.Empty, detail);
    }

    private static bool? ReadFlag(object? instance, string member) =>
        GameReflection.TryRead(instance, member, out var value, out _) && value is bool flag ? flag : null;

    private static string Describe(bool? value) => value switch
    {
        true => "true",
        false => "false",
        _ => "unknown",
    };

    private static string Describe(string value) => string.IsNullOrEmpty(value) ? "unknown" : value;
}
