using System.Text;
using Expansions.Core.Diagnostics;
using Expansions.Core.UI.Native;

namespace Expansions.Core.Game;

/// <summary>
/// Turns the in-game developer console back on and opens it.
/// <para>
/// Two independent gates keep it shut, and they fail in different ways:
/// </para>
/// <list type="number">
/// <item>
/// A per-save flag. <c>ConsoleUI.IsConsoleEnabled</c> is computed from
/// <c>GameManager.Settings.ConsoleEnabled</c>, which is persisted in the save's <c>Game.json</c>. On a
/// save where it is false the console physically cannot open, and there is no keypress that changes
/// that. It is an ordinary settable bool — not one of the IL2CPP consts that fault when written.
/// </item>
/// <item>
/// The key binding, which goes through the new Input System rather than <c>Input.GetKeyDown</c>, so
/// nothing here can rebind it. Reading it back is the useful thing: the owner needs to be told which
/// key to press once the flag is on.
/// </item>
/// </list>
/// <para>
/// Neither gate matters in the main menu, where <c>ConsoleUI</c> does not exist at all.
/// </para>
/// </summary>
internal static class GameConsole
{
    /// <summary>What the shipped bindings are, for when the live ones cannot be read.</summary>
    private const string DocumentedBinding =
        "backquote/tilde key (`) on keyboard, or hold the left stick and click the right stick on a gamepad";

    /// <summary>
    /// Finding the instance is a <c>Resources.FindObjectsOfTypeAll</c> heap walk, which the per-frame
    /// availability check cannot afford. The handle is re-validated against Unity's own null on every
    /// read, so a destroyed one is never handed out even inside the window.
    /// </summary>
    private static UnityEngine.Object? _cached;
    private static float _cachedAt = float.NegativeInfinity;

    /// <summary>Long enough to survive a frame of UI refresh, short enough to notice a scene load.</summary>
    private const float CacheSeconds = 1f;

    /// <summary>
    /// Reading the binding walks the input asset, and the Actions page shows it under the console rows on
    /// every frame the screen is open. Re-read occasionally rather than once, so a rebind in the game's
    /// own options is picked up rather than shown wrong forever.
    /// </summary>
    private static readonly TimedCache<string> BindingText = new(30f, DescribeBinding);

    /// <summary>The composed row note. Cached so the per-frame refresh compares a reference, not a rebuild.</summary>
    private static readonly TimedCache<string> NoteText = new(0.5f, ComposeStatusNote);

    /// <summary>Why the console actions are greyed out, or empty when they are not.</summary>
    internal static string UnavailableReason()
    {
        if (GameReflection.FindType(GameTypeNames.ConsoleUi) is null)
            return $"'{GameTypeNames.ConsoleUi}' is not on this build";

        if (TryFind(out _, out _, out _))
            return string.Empty;

        var scene = GameReflection.ActiveSceneName();
        return string.Equals(scene, GameTypeNames.MainScene, StringComparison.Ordinal)
            ? "the console UI has not spawned yet - give the save a moment"
            : $"you are in the '{DescribeScene(scene)}' scene and the console only exists in '{GameTypeNames.MainScene}' - load a save first";
    }

    /// <summary>True when the loaded save has the console flag on.</summary>
    internal static bool? IsEnabled()
    {
        if (!TryFind(out var instance, out var wrapper, out _))
            return null;

        return InteropObjects.Read(instance, wrapper!, "IsConsoleEnabled") as bool?;
    }

    /// <summary>
    /// Sets the per-save flag, and says whether it had to change anything. The write lands on the live
    /// <c>GameSettings</c> instance, so the console works immediately; it reaches <c>Game.json</c> on
    /// the next save.
    /// </summary>
    internal static (bool Ok, string Message) Enable()
    {
        var already = IsEnabled();
        if (already == true)
            return (true, "The console is already enabled on this save.");

        if (!GameReflection.TryGetSingleton(GameTypeNames.GameManager, out var manager, out var managerFailure))
            return (false, $"Could not reach GameManager to set the console flag ({managerFailure}).");

        if (!GameReflection.TryRead(manager, "Settings", out var settings, out var settingsFailure) || settings is null)
            return (false, $"GameManager.Settings could not be read ({settingsFailure}).");

        if (!GameReflection.TryWrite(settings, "ConsoleEnabled", true, out var writeFailure))
            return (false, $"GameSettings.ConsoleEnabled could not be written ({writeFailure}).");

        var confirmed = IsEnabled();
        if (confirmed == false)
        {
            return (false,
                "GameSettings.ConsoleEnabled was written but ConsoleUI still reports the console as disabled. " +
                "Something else is gating it on this build.");
        }

        return (true,
            "Console enabled for this save. The flag is written to the live settings now and lands in the save's " +
            "Game.json the next time the game saves.");
    }

    /// <summary>Enables the flag if needed and opens the console window.</summary>
    internal static (bool Ok, string Message) Open()
    {
        if (!TryFind(out var instance, out var wrapper, out var findFailure))
            return (false, findFailure);

        var enabled = Enable();
        if (!enabled.Ok)
            return enabled;

        var typed = InteropObjects.Reinterpret(instance, wrapper!);
        if (typed is null)
            return (false, "The live ConsoleUI could not be re-wrapped, so it cannot be opened.");

        if (!GameReflection.TryInvokeExact(
                wrapper!, typed, "SetIsOpen", new[] { typeof(bool) }, new object?[] { true }, out _, out var openFailure))
        {
            return (false, $"{enabled.Message} Opening it failed though ({openFailure}).");
        }

        return (true, $"{enabled.Message} Console open. Reopen it later with the {Binding()}.");
    }

    /// <summary>
    /// The live toggle binding in words, e.g. <c>backquote (Keyboard)</c>. Falls back to the shipped
    /// defaults with a note, because a wrong key is worse than an admittedly generic one.
    /// </summary>
    internal static string Binding() => BindingText.Value;

    /// <summary>
    /// One line for the Actions page to show under the console rows: whether the flag is on, and which
    /// key opens it. The point of this whole surface is that the owner does not have to click something
    /// to find out.
    /// </summary>
    internal static string StatusNote() => NoteText.Value;

    private static string ComposeStatusNote()
    {
        var enabled = IsEnabled();
        var state = enabled switch
        {
            true => "console is on for this save",
            false => "console is OFF for this save",
            _ => "console state unknown",
        };

        return $"{state}; toggle key: {Binding()}";
    }

    private static string DescribeBinding()
    {
        try
        {
            var live = ReadLiveBinding();
            if (live.Length > 0)
                return live;
        }
        catch (Exception ex)
        {
            ExpansionHost.Log.Debug($"Could not read the console binding ({GameReflection.Unwrap(ex)}).");
        }

        return TryFind(out _, out _, out _)
            ? DocumentedBinding + " (shipped default; the live binding could not be read)"
            : DocumentedBinding + " (shipped default; the live binding can only be read in a loaded game)";
    }

    /// <summary>
    /// Walks <c>ConsoleUI.ToggleConsoleReference.action.bindings</c> and renders each
    /// <c>effectivePath</c>. Parts of a composite are joined with <c>+</c> so the gamepad chord reads
    /// as one chord rather than two alternatives.
    /// </summary>
    private static string ReadLiveBinding()
    {
        if (!TryFind(out var instance, out var wrapper, out _))
            return string.Empty;

        var reference = InteropObjects.Read(instance, wrapper!, "ToggleConsoleReference");
        if (reference is null)
            return string.Empty;

        if (!GameReflection.TryRead(reference, "action", out var action, out _) || action is null)
            return string.Empty;

        if (!GameReflection.TryRead(action, "bindings", out var bindings, out _) || bindings is null)
            return string.Empty;

        var alternatives = new List<string>();
        var composite = new List<string>();

        foreach (var binding in GameReflection.Enumerate(bindings, 32))
        {
            if (binding is null)
                continue;

            // A composite's own entry carries no path; only its parts do.
            if (GameReflection.TryRead(binding, "isComposite", out var isComposite, out _) && isComposite is true)
            {
                FlushComposite(composite, alternatives);
                continue;
            }

            if (!GameReflection.TryRead(binding, "effectivePath", out var path, out _) || path is not string text)
                continue;

            var pretty = Prettify(text);
            if (pretty.Length == 0)
                continue;

            var isPart = GameReflection.TryRead(binding, "isPartOfComposite", out var part, out _) && part is true;
            if (isPart)
                composite.Add(pretty);
            else
                alternatives.Add(pretty);
        }

        FlushComposite(composite, alternatives);

        return alternatives.Count == 0 ? string.Empty : string.Join(", or ", alternatives);
    }

    private static void FlushComposite(List<string> composite, List<string> alternatives)
    {
        if (composite.Count == 0)
            return;

        alternatives.Add(string.Join(" + ", composite));
        composite.Clear();
    }

    /// <summary>
    /// <c>&lt;Keyboard&gt;/backquote</c> becomes <c>backquote (Keyboard)</c>. Input System paths are
    /// stable enough to split on, and showing the raw path unmodified would be worse than nothing.
    /// </summary>
    private static string Prettify(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var device = string.Empty;
        var control = path.Trim();

        if (control.StartsWith("<", StringComparison.Ordinal))
        {
            var close = control.IndexOf('>');
            if (close > 1)
            {
                device = control[1..close];
                control = control[(close + 1)..].TrimStart('/');
            }
        }

        control = Spaced(control.Replace('/', ' '));

        if (control.Length == 0)
            return device;

        return device.Length == 0 ? control : $"{control} ({device})";
    }

    /// <summary>Splits camelCase control names, so <c>leftStickPress</c> reads as <c>left Stick Press</c>.</summary>
    private static string Spaced(string text)
    {
        var builder = new StringBuilder(text.Length + 8);

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (i > 0 && char.IsUpper(c) && !char.IsWhiteSpace(text[i - 1]))
                builder.Append(' ');

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The live <c>ConsoleUI</c>, plus the wrapper type reflection has to go through.
    /// <c>FindObjectsOfTypeAll</c> hands back elements typed as the array's element type, so members
    /// declared on <c>ConsoleUI</c> are invisible until the instance is re-wrapped.
    /// </summary>
    private static bool TryFind(out UnityEngine.Object? instance, out Type? wrapper, out string failure)
    {
        instance = null;
        wrapper = GameReflection.FindType(GameTypeNames.ConsoleUi);

        if (wrapper is null)
        {
            failure = $"'{GameTypeNames.ConsoleUi}' is not on this build.";
            return false;
        }

        if (InteropObjects.Alive(_cached) && UnscaledTime() - _cachedAt < CacheSeconds)
        {
            instance = _cached;
            failure = string.Empty;
            return true;
        }

        var found = InteropObjects.FindInScene(wrapper);
        if (found.Count == 0)
        {
            _cached = null;
            _cachedAt = float.NegativeInfinity;

            var scene = GameReflection.ActiveSceneName();
            failure =
                $"No ConsoleUI is live. It exists only in the '{GameTypeNames.MainScene}' scene and you are in " +
                $"'{DescribeScene(scene)}', so load a save first.";
            return false;
        }

        instance = found[0];
        _cached = instance;
        _cachedAt = UnscaledTime();
        failure = string.Empty;
        return true;
    }

    private static float UnscaledTime()
    {
        try
        {
            return UnityEngine.Time.unscaledTime;
        }
        catch
        {
            return float.PositiveInfinity;
        }
    }

    private static string DescribeScene(string scene) => scene.Length == 0 ? "unknown" : scene;
}
