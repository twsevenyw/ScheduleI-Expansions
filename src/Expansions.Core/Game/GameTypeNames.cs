namespace Expansions.Core.Game;

/// <summary>
/// Every game type Core's actions reach for, in one place.
/// <para>
/// All of them are resolved by name at runtime. Core is loaded from <c>UserLibs</c> by all three mods,
/// so a compile-time reference to a type a game patch renames would surface as a
/// <c>TypeLoadException</c> and take every mod down with it — a renamed type has to cost one action
/// and a logged reason instead.
/// </para>
/// </summary>
internal static class GameTypeNames
{
    internal const string ConsoleUi = "Il2CppScheduleOne.UI.ConsoleUI";
    internal const string GameManager = "Il2CppScheduleOne.DevUtilities.GameManager";
    internal const string NpcManager = "Il2CppScheduleOne.NPCs.NPCManager";
    internal const string PlayerMovement = "Il2CppScheduleOne.PlayerScripts.PlayerMovement";
    internal const string Player = "Il2CppScheduleOne.PlayerScripts.Player";
    internal const string LoadManager = "Il2CppScheduleOne.Persistence.LoadManager";

    /// <summary>The gameplay scene. <c>ConsoleUI</c> exists nowhere else.</summary>
    internal const string MainScene = "Main";

    internal const string TutorialScene = "Tutorial";
}
