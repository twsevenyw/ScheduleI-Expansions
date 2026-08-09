using System.Reflection;
using Il2CppInterop.Runtime;

namespace Expansions.HireableDrivers.Game;

/// <summary>
/// The management clipboard's own option-list screen, used for the one pick its worldspace picker
/// cannot make.
/// <para>
/// <c>ManagementInterface.ItemSelectorScreen</c> is a shipped <c>ClipboardScreen</c>: it slides in over
/// the clipboard, uses the clipboard's fonts, sounds and gamepad handling, and closes with it. The game
/// drives it from <c>ItemFieldUI.Clicked()</c> in exactly this way. Borrowing it is what lets a driver
/// choose a warehouse on the other side of town without the mod drawing a single pixel of its own.
/// </para>
/// <para>
/// Every failure path returns false so the caller can fall through to the game's normal behaviour.
/// </para>
/// </summary>
internal static class SelectorApi
{
    /// <summary>
    /// Rooted for the rest of the process, deliberately. Il2CppInterop wraps a managed delegate in a
    /// native object, and a collected interop delegate silently never fires — the one failure mode this
    /// module refuses to ship. The screen keeps its own reference to whatever was last handed to it, so
    /// these are only ever replaced, never cleared.
    /// </summary>
    private static Delegate? _managedCallback;

    private static object? _nativeCallback;

    private static Dictionary<string, string> _keysByTitle = new(StringComparer.Ordinal);

    private static Action<string>? _onPicked;

    /// <summary>
    /// Set if the screen ever refuses to open. One bad attempt is a glitch; repeating it every time the
    /// player clicks a drop-off is a broken feature, so the mod stands down for the session and the
    /// menu's repair path takes over carrying this reason.
    /// </summary>
    private static string _standDownReason = string.Empty;

    /// <summary>True when every symbol the list screen needs is on this build.</summary>
    internal static bool IsAvailable(out string reason)
    {
        if (_standDownReason.Length > 0)
        {
            reason = _standDownReason;
            return false;
        }

        if (Gx.Type(GameTypes.ItemSelector) is null)
        {
            reason = "the clipboard's option-list screen (ItemSelector) is not on this build";
            return false;
        }

        if (Gx.Type(GameTypes.ItemSelectorOption) is null)
        {
            reason = "ItemSelector.Option is not on this build";
            return false;
        }

        if (Screen() is null)
        {
            reason = "the management clipboard has not spawned its screens yet";
            return false;
        }

        if (ConvertDelegate is null)
        {
            reason = "Il2CppInterop.DelegateSupport.ConvertDelegate could not be resolved";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Shows <paramref name="options"/> on the clipboard and calls <paramref name="onPicked"/> with the
    /// chosen option's key. Returns false if anything could not be reached, having changed nothing.
    /// </summary>
    internal static bool Open(
        string title,
        IReadOnlyList<(string Key, string Label)> options,
        Action<string> onPicked)
    {
        if (!IsAvailable(out var reason))
        {
            DriverLog.Debug($"Native list screen unavailable: {reason}.");
            return false;
        }

        var screen = Screen();
        var optionType = Gx.Type(GameTypes.ItemSelectorOption);
        if (screen is null || optionType is null)
            return false;

        var list = Gx.NewList(GameTypes.ItemSelectorOption);
        if (list is null)
            return false;

        var keysByTitle = new Dictionary<string, string>(StringComparer.Ordinal);
        var built = 0;

        foreach (var (key, label) in options)
        {
            // The callback hands back an Option whose managed wrapper is not the one we built, so the
            // title is the only identity that survives the round trip. Titles are made unique here
            // rather than trusted to be.
            var optionTitle = Unique(keysByTitle, label);

            // Item is deliberately null: the screen falls back to its own EmptyOptionSprite, which is
            // the same path the shipped "None" option takes.
            var option = Gx.New(GameTypes.ItemSelectorOption, optionTitle, null);
            if (option is null)
                continue;

            keysByTitle[optionTitle] = key;
            Gx.Call(list, "Add", new[] { Gx.Any }, option);
            built++;
        }

        if (built == 0)
            return false;

        if (!Bind(optionType, onPicked))
            return false;

        _keysByTitle = keysByTitle;

        if (!Gx.TryCall(screen, "Initialize", new[] { "String", Gx.Any, Gx.Any, Gx.Any },
                title, list, null, _nativeCallback))
        {
            StandDown("the clipboard's list screen would not accept a set of options");
            return false;
        }

        if (!Gx.TryCall(screen, "Open", Array.Empty<string>()))
        {
            StandDown("the clipboard's list screen threw while opening");
            return false;
        }

        return true;
    }

    private static void StandDown(string reason)
    {
        Forget();

        if (_standDownReason.Length > 0)
            return;

        _standDownReason = reason;
        DriverLog.Warn(
            $"Driver drop-offs will use the game's worldspace picker for the rest of this session: {reason}. " +
            "The Expansions menu's repair path can still reach a destination across town or a dealer.");
    }

    private static object? Screen() =>
        Gx.GetAlive(Gx.Singleton(GameTypes.ManagementInterface), "ItemSelectorScreen");

    /// <summary>
    /// Resolved once. <see cref="IsAvailable"/> is read from a per-frame availability predicate, so it
    /// must not allocate a reflection lookup every frame.
    /// </summary>
    private static readonly MethodInfo? ConvertDelegate =
        typeof(DelegateSupport).GetMethod("ConvertDelegate", BindingFlags.Public | BindingFlags.Static);

    /// <summary>
    /// Builds the <c>Il2CppSystem.Action&lt;ItemSelector.Option&gt;</c> the screen wants. The closed
    /// generic cannot be named at compile time — <c>ItemSelector.Option</c> lives in
    /// <c>Assembly-CSharp</c> and this assembly deliberately does not reference it — so the conversion
    /// goes through <c>DelegateSupport.ConvertDelegate</c> by reflection.
    /// </summary>
    private static bool Bind(Type optionType, Action<string> onPicked)
    {
        var converter = ConvertDelegate;
        if (converter is null)
            return false;

        try
        {
            _onPicked = onPicked;

            var handlerType = typeof(Action<>).MakeGenericType(optionType);
            var handler = Delegate.CreateDelegate(
                handlerType,
                typeof(SelectorApi).GetMethod(nameof(Picked), BindingFlags.NonPublic | BindingFlags.Static)!);

            var il2CppActionType = typeof(Il2CppSystem.Action<>).MakeGenericType(optionType);
            var native = converter.MakeGenericMethod(il2CppActionType).Invoke(null, new object?[] { handler });
            if (native is null)
                return false;

            _managedCallback = handler;
            _nativeCallback = native;
            return true;
        }
        catch (Exception ex)
        {
            DriverLog.Warn($"Could not attach a callback to the clipboard's list screen ({Gx.Explain(ex)}).");
            Forget();
            return false;
        }
    }

    /// <summary>
    /// Static and non-generic on purpose: <see cref="Delegate.CreateDelegate(Type, MethodInfo)"/> binds
    /// it to <c>Action&lt;Option&gt;</c> whatever the runtime type of <c>Option</c> turns out to be.
    /// </summary>
    private static void Picked(object? option)
    {
        var handler = _onPicked;
        var titles = _keysByTitle;
        Forget();

        if (handler is null || option is null)
            return;

        try
        {
            var title = Gx.Get<string>(option, "Title", string.Empty);
            if (title.Length > 0 && titles.TryGetValue(title, out var key))
                handler(key);
        }
        catch (Exception ex)
        {
            DriverLog.Error("A clipboard list selection could not be applied.", ex);
        }
    }

    /// <summary>
    /// Drops what this pick meant, not the delegates behind it: the screen may still be holding the
    /// converted callback, and letting that be collected is how an interop callback stops firing.
    /// </summary>
    private static void Forget()
    {
        _onPicked = null;
        _keysByTitle = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static string Unique(Dictionary<string, string> taken, string label)
    {
        var candidate = string.IsNullOrWhiteSpace(label) ? "(unnamed)" : label;
        if (!taken.ContainsKey(candidate))
            return candidate;

        for (var n = 2; n < 1000; n++)
        {
            var next = $"{candidate} ({n})";
            if (!taken.ContainsKey(next))
                return next;
        }

        return candidate + " " + Guid.NewGuid().ToString("N")[..4];
    }
}
