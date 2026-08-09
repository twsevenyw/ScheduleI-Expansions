using Expansions.Core.Diagnostics;
using Expansions.Core.Logging;

namespace Expansions.Core.UI.Native;

/// <summary>
/// Puts the game into its own UI state while the Expansions screen is open, and puts it back after.
/// <para>
/// This is the fix for the recorded bug where typing into the picker's search box also walked the
/// player: the screen lives on a standalone canvas and so was not part of
/// <c>MonoState</c>/<c>StateProperties</c>, which is what tells the game that movement, camera look and
/// the crosshair are suspended. Cursor freeing comes with it, which is also why the per-frame
/// <c>Cursor.lockState</c> write is now only a fallback rather than the mechanism.
/// </para>
/// <para>
/// The transition handler is a static, so this needs no injected <c>MonoBehaviour</c> and no
/// compile-time <c>Assembly-CSharp</c> reference — the whole path is reflective and reports itself as
/// unavailable rather than throwing on a build where the type moved. Pushing a real <c>MonoState</c>
/// onto the game's state machine would be the fuller integration, but it means adding a game
/// <c>MonoBehaviour</c> to our canvas and finding the right machine per scene, for the same effect.
/// </para>
/// </summary>
internal static class GameUiState
{
    private const string PropertiesType = "Il2CppScheduleOne.State.StateProperties";
    private const string HandlerType = "Il2CppScheduleOne.State.StatePropertiesTransitionHandler";

    /// <summary>The preset the game's own UI screens use: mouse free, movement and look locked.</summary>
    private const string UiPreset = "UIDefault";

    /// <summary>The static the handler keeps the applied properties in, so they can be put back.</summary>
    private const string CurrentField = "_currentProperties";

    private static readonly ModuleLogger Log = new("UI");

    private static object? _restore;
    private static bool _entered;
    private static bool _reported;

    /// <summary>
    /// How many of our surfaces are asking for the UI state right now. The Expansions screen and the
    /// event chooser can overlap for a frame when one opens over the other, and the first one to close
    /// must not hand movement back while the other is still up.
    /// </summary>
    private static int _depth;

    /// <summary>True while the game is being held in the UI state by one of our surfaces.</summary>
    public static bool IsHeld => _entered;

    /// <summary>
    /// Why the last <see cref="Enter"/> did nothing, or empty when it worked. Surfaced so the owner
    /// gets a reason rather than a menu that silently still walks them around.
    /// </summary>
    public static string Failure { get; private set; } = string.Empty;

    /// <summary>Suspends gameplay input. Nested calls are counted; only the outermost transitions.</summary>
    public static void Enter()
    {
        _depth++;

        if (_entered)
            return;

        Failure = string.Empty;

        // Gameplay only. In the main menu there is no player to walk and no HUD to suspend, and the
        // menu's own MonoState has already freed the cursor — so there is nothing to gain and a
        // scene's worth of null state to risk.
        if (!IsInGame())
        {
            Failure = "not in a game";
            return;
        }

        var handler = GameReflection.FindType(HandlerType);
        var properties = GameReflection.FindType(PropertiesType);

        if (handler is null || properties is null)
        {
            Fail($"'{(handler is null ? HandlerType : PropertiesType)}' is not on this build");
            return;
        }

        if (!GameReflection.TryReadStatic(properties, UiPreset, out var uiDefault, out var presetFailure) ||
            uiDefault is null)
        {
            Fail($"'{PropertiesType}.{UiPreset}' could not be read ({presetFailure})");
            return;
        }

        // Read before transitioning, so Exit restores whatever the game was actually enforcing rather
        // than assuming the unenforced preset.
        _restore = GameReflection.TryReadStatic(handler, CurrentField, out var current, out _) ? current : null;

        if (!GameReflection.TryInvoke(handler, null, "Transition", new[] { uiDefault }, out _, out var failure))
        {
            _restore = null;
            Fail($"'{HandlerType}.Transition' could not be called ({failure})");
            return;
        }

        _entered = true;
        _reported = false;
    }

    /// <summary>Hands gameplay input back once the last caller has exited. Safe to call unbalanced.</summary>
    public static void Exit()
    {
        if (_depth > 0)
            _depth--;

        if (_depth > 0 || !_entered)
            return;

        _entered = false;

        var handler = GameReflection.FindType(HandlerType);
        var restore = _restore;
        _restore = null;

        if (handler is null || restore is null)
            return;

        if (!GameReflection.TryInvoke(handler, null, "Transition", new[] { restore }, out _, out var failure))
            Log.Debug($"Could not restore the game's input state ({failure}); it re-applies on the next state change.");
    }

    /// <summary>
    /// Drops the held state without transitioning back. For a scene change: the properties we saved
    /// describe the outgoing scene, and re-applying them in the incoming one could leave it enforcing
    /// gameplay movement and a locked cursor in a menu. The new scene's own state stack owns it now.
    /// </summary>
    public static void Forget()
    {
        _entered = false;
        _depth = 0;
        _restore = null;
    }

    private static bool IsInGame()
    {
        try
        {
            return GameSessionState.Capture().IsSaveLoaded;
        }
        catch
        {
            return false;
        }
    }

    private static void Fail(string reason)
    {
        Failure = reason;

        if (_reported)
            return;

        _reported = true;
        Log.Warn(
            $"The Expansions screen could not suspend gameplay input: {reason}. Typing into a picker's " +
            $"search box may also move the player; scroll the list instead.");
    }
}
