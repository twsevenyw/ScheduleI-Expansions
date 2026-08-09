using Expansions.Core.Configuration;

namespace Expansions.Core.UI;

/// <summary>
/// Swap point for the toggle UI. Starts on the native uGUI/TMP screen; <see cref="ImguiExpansionMenu"/>
/// is kept as the degradation path for a build where the main menu cannot be found, and a third party
/// can install its own implementation with a single <see cref="Use"/> call.
/// </summary>
public static class ExpansionMenu
{
    // Attached here rather than left bare: Use() is what normally calls OnAttached, and the default
    // implementation subscribes to registry events there like any other.
    private static IExpansionMenu _current = Attach(new NativeExpansionMenu());

    public static IExpansionMenu Current => _current;

    public static event Action<IExpansionMenu>? Changed;

    /// <summary>True once the native screen has degraded to the IMGUI fallback.</summary>
    public static bool IsPlaceholder => _current is ImguiExpansionMenu;

    /// <summary>
    /// Installs a menu implementation, closing and detaching the previous one. Pass a
    /// <see cref="NullExpansionMenu"/> to suppress the built-in UI entirely.
    /// <para>
    /// Exactly one surface is ever live: <c>ExpansionHost</c> pumps <see cref="Current"/> and nothing
    /// else, and the outgoing menu is closed and detached here — which, for the native screen, destroys
    /// its canvas. The IMGUI fallback additionally refuses to draw if it is asked to while not current.
    /// </para>
    /// </summary>
    public static void Use(IExpansionMenu menu)
    {
        if (menu is null)
            throw new ArgumentNullException(nameof(menu));

        if (ReferenceEquals(menu, _current))
            return;

        var previous = _current;

        // Cleared before detaching, so a handler that runs during teardown cannot see two live menus.
        _current = NullExpansionMenu.Instance;

        try
        {
            previous.Close();
            previous.OnDetached();
        }
        catch (Exception ex)
        {
            ExpansionHost.Log.Error($"Menu '{previous.Name}' failed to detach.", ex);
        }

        _current = Attach(menu);

        ExpansionHost.Log.Msg($"Toggle UI is now '{menu.Name}'.");
        Changed?.Invoke(menu);
    }

    /// <summary>
    /// Hands over to the proven IMGUI menu. Called when the native screen cannot be built or the main
    /// menu cannot be found: the toggles have to stay reachable, and a build whose menu hierarchy has
    /// changed shape that much makes every other assumption about its UI suspect too.
    /// </summary>
    internal static void FallBackToPlaceholder(string reason)
    {
        if (_current is ImguiExpansionMenu)
            return;

        ExpansionHost.Log.Warn(
            $"The native toggle screen is unavailable because {reason}. Falling back to the built-in " +
            $"menu — press {ExpansionConfig.MenuHotkey} to open it.");

        Use(new ImguiExpansionMenu());
    }

    private static IExpansionMenu Attach(IExpansionMenu menu)
    {
        try
        {
            menu.OnAttached();
        }
        catch (Exception ex)
        {
            ExpansionHost.Log.Error($"Menu '{menu.Name}' failed to attach.", ex);
        }

        return menu;
    }

    public static void Open() => _current.Open();

    public static void Close() => _current.Close();

    public static void Toggle()
    {
        if (_current.IsOpen)
            _current.Close();
        else
            _current.Open();
    }
}
