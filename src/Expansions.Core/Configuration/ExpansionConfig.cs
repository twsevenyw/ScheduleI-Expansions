using System.Text;
using MelonLoader.Utils;
using UnityEngine;

namespace Expansions.Core.Configuration;

/// <summary>
/// Settings shared by every expansion. These live in MelonPreferences rather than in a save file:
/// module toggles are driven from the main menu, before any save exists, so they are machine-global.
/// </summary>
public static class ExpansionConfig
{
    public const string CategoryId = "Expansions";
    public const string FileName = "Expansions.cfg";

    /// <summary>
    /// Suffix every per-module category carries. The Schedule I community settled on
    /// <c>&lt;ModName&gt;_01_Main</c>, and the third-party settings phone apps detect mods by it, so
    /// following the convention gets our toggles an in-game UI for free.
    /// </summary>
    public const string MainCategorySuffix = "_01_Main";

    public static readonly KeyCode DefaultMenuHotkey = KeyCode.F7;

    /// <summary>Opens the event chooser in-game. Up arrow, per the owner's request.</summary>
    public static readonly KeyCode DefaultEventHotkey = KeyCode.UpArrow;

    /// <summary>Runs the diagnostics suite while the toggle menu is open, whichever menu is live. Same
    /// suite as the <c>expprobe</c> console command.</summary>
    public const KeyCode ProbeHotkey = KeyCode.P;

    /// <summary>Config key that unlocks the probes which can crash the game. Quoted in skip messages.</summary>
    public const string AllowMutatingProbesKey = "allow_mutating_probes";

    /// <summary>Channel used when <c>update_channel</c> is blank or unreadable.</summary>
    public const string DefaultUpdateChannel = "stable";

    /// <summary>
    /// The publisher's stable manifest asset. GitHub redirects it to the newest non-draft,
    /// non-prerelease release, so it needs no API call and has no rate limit. It answers 404 until the
    /// first such release exists, which the updater treats as "nothing published yet".
    /// </summary>
    public const string DefaultUpdateManifestUrl =
        "https://github.com/twsevenyw/ScheduleI-Expansions/releases/latest/download/update-manifest.json";

    private static ModuleConfig? _config;
    private static ConfigValue<bool>? _verboseLogging;
    private static ConfigValue<string>? _menuHotkey;
    private static ConfigValue<string>? _eventHotkey;
    private static ConfigValue<bool>? _allowMutatingProbes;
    private static ConfigValue<bool>? _outputPaneOpen;
    private static ConfigValue<string>? _disabledTutorialChapters;
    private static ConfigValue<bool>? _autoUpdate;
    private static ConfigValue<string>? _updateChannel;
    private static ConfigValue<string>? _updateManifestUrl;

    // Both are read every frame, so the parsed/unboxed values are cached and refreshed on change
    // rather than round-tripping through MelonPreferences.
    private static bool _verboseCache;
    private static KeyCode _menuHotkeyCache = KeyCode.F7;
    private static KeyCode _eventHotkeyCache = KeyCode.UpArrow;
    private static bool _allowMutatingProbesCache;
    private static bool _outputPaneOpenCache = true;
    private static string _disabledTutorialChaptersCache = string.Empty;
    private static bool _autoUpdateCache = true;
    private static string _updateChannelCache = DefaultUpdateChannel;
    private static string _updateManifestUrlCache = DefaultUpdateManifestUrl;

    public static string FilePath { get; private set; } = string.Empty;

    public static bool IsInitialized => _config is not null;

    public static bool VerboseLogging
    {
        get => _verboseCache;
        set
        {
            _verboseCache = value;
            if (_verboseLogging is not null)
                _verboseLogging.Value = value;
        }
    }

    /// <summary>Opens the toggle menu. <c>KeyCode.None</c> disables the hotkey entirely.</summary>
    public static KeyCode MenuHotkey
    {
        get => _menuHotkeyCache;
        set
        {
            _menuHotkeyCache = value;
            if (_menuHotkey is not null)
                _menuHotkey.Value = value.ToString();
        }
    }

    /// <summary>
    /// Opens the in-world event chooser during gameplay. Holding shift and pressing it re-fires the
    /// last event instead. <c>KeyCode.None</c> disables it entirely.
    /// </summary>
    public static KeyCode EventHotkey
    {
        get => _eventHotkeyCache;
        set
        {
            _eventHotkeyCache = value;
            if (_eventHotkey is not null)
                _eventHotkey.Value = value.ToString();
        }
    }

    /// <summary>
    /// Whether the suite keeps itself up to date. Read by the <c>Expansions.Updater</c> plugin, which is
    /// what actually does the work — it checks on a background task that cannot delay a launch, and
    /// installs what it found at the start of the next one, in the window before MelonLoader loads the
    /// mod assemblies. Off means the updater touches nothing at all, including anything already
    /// downloaded.
    /// </summary>
    public static bool AutoUpdate
    {
        get => _autoUpdateCache;
        set
        {
            _autoUpdateCache = value;
            if (_autoUpdate is not null)
                _autoUpdate.Value = value;
        }
    }

    /// <summary>
    /// Which releases to follow. <c>stable</c> reads the publisher's <c>releases/latest</c> asset, which
    /// GitHub already filters to non-prerelease, non-draft releases. Any other value means "include
    /// prereleases", which requires listing the repository's releases instead — the manifest itself carries
    /// no channel field, because the publisher expresses that with GitHub's prerelease flag.
    /// </summary>
    public static string UpdateChannel
    {
        get => _updateChannelCache;
        set
        {
            var text = string.IsNullOrWhiteSpace(value) ? DefaultUpdateChannel : value.Trim();
            _updateChannelCache = text;
            if (_updateChannel is not null)
                _updateChannel.Value = text;
        }
    }

    /// <summary>
    /// Where the update manifest lives. Accepts a GitHub <c>owner/repo</c> shorthand or a direct https URL
    /// to an <c>update-manifest.json</c>. Empty switches the updater off entirely.
    /// </summary>
    public static string UpdateManifestUrl
    {
        get => _updateManifestUrlCache;
        set
        {
            var text = value?.Trim() ?? string.Empty;
            _updateManifestUrlCache = text;
            if (_updateManifestUrl is not null)
                _updateManifestUrl.Value = text;
        }
    }

    /// <summary>
    /// Off by default, and it should stay off. A handful of diagnostics probes write native game
    /// memory to answer a question no read can; one of those writes can end the process outright,
    /// with no managed exception and no Unity crash log. Turning this on is a deliberate act on a
    /// save you are willing to lose.
    /// </summary>
    public static bool AllowMutatingProbes
    {
        get => _allowMutatingProbesCache;
        set
        {
            _allowMutatingProbesCache = value;
            if (_allowMutatingProbes is not null)
                _allowMutatingProbes.Value = value;
        }
    }

    /// <summary>
    /// Whether the Actions tab's output pane is expanded. Persisted because the owner's preference
    /// here is about how much of the screen they want the action list to have, which does not change
    /// between sessions.
    /// </summary>
    public static bool OutputPaneOpen
    {
        get => _outputPaneOpenCache;
        set
        {
            _outputPaneOpenCache = value;
            if (_outputPaneOpen is not null)
                _outputPaneOpen.Value = value;
        }
    }

    /// <summary>
    /// Comma-separated tutorial chapter ids the owner has switched off. Stored as one list rather than
    /// an entry per chapter because chapters arrive at runtime from whichever mods are installed, and
    /// an id belonging to an uninstalled mod has to survive a round trip rather than be dropped.
    /// </summary>
    public static string DisabledTutorialChapters
    {
        get => _disabledTutorialChaptersCache;
        set
        {
            var text = value ?? string.Empty;
            _disabledTutorialChaptersCache = text;
            if (_disabledTutorialChapters is not null)
                _disabledTutorialChapters.Value = text;
        }
    }

    /// <summary>Fires when <see cref="DisabledTutorialChapters"/> changes, including from a file edit.</summary>
    public static event Action? DisabledTutorialChaptersChanged;

    /// <summary>Category identifier for a module that presents itself as <paramref name="modName"/>.</summary>
    public static string CategoryFor(string modName) => modName + MainCategorySuffix;

    /// <summary>
    /// Default category identifier for a module id: <c>hireable_drivers</c> becomes
    /// <c>HireableDrivers_01_Main</c>. Never put a dot in a category — MelonPreferences hands the
    /// identifier to Tomlet unquoted, which reads the dot as a table path, nests the module inside
    /// the parent table and then silently drops every entry it tried to write.
    /// </summary>
    public static string CategoryForId(string moduleId) => CategoryFor(ToPascalCase(moduleId));

    internal static void Initialize()
    {
        if (_config is not null)
            return;

        FilePath = Path.Combine(MelonEnvironment.UserDataDirectory, FileName);

        var config = new ModuleConfig(CategoryId, "Expansions (shared)");

        _verboseLogging = config.Bind("verbose_logging", false, "Verbose logging",
            "Write module debug output to the MelonLoader console.");
        _verboseCache = _verboseLogging.Value;
        _verboseLogging.Changed += static (_, current) => _verboseCache = current;

        _menuHotkey = config.Bind("menu_hotkey", DefaultMenuHotkey.ToString(), "Menu hotkey",
            "UnityEngine.KeyCode name that opens the expansions menu. Use None to disable.");
        _menuHotkeyCache = ParseHotkey(_menuHotkey.Value, DefaultMenuHotkey);
        _menuHotkey.Changed += static (_, current) => _menuHotkeyCache = ParseHotkey(current, DefaultMenuHotkey);

        _eventHotkey = config.Bind("event_hotkey", DefaultEventHotkey.ToString(), "Event hotkey",
            "UnityEngine.KeyCode name that opens the in-world event chooser during gameplay. Hold " +
            "shift and press it to re-fire the last event without the chooser. Use None to disable.");
        _eventHotkeyCache = ParseHotkey(_eventHotkey.Value, DefaultEventHotkey);
        _eventHotkey.Changed += static (_, current) => _eventHotkeyCache = ParseHotkey(current, DefaultEventHotkey);

        _autoUpdate = config.Bind("auto_update", true, "Keep the suite up to date",
            "Check for a newer release in the background and install it at the next launch, before the " +
            "mods load. Nothing is ever deleted and a mod you switched off stays off. Off means the " +
            "updater does nothing at all - there is no manual alternative, by design.");
        _autoUpdateCache = _autoUpdate.Value;
        _autoUpdate.Changed += static (_, current) => _autoUpdateCache = current;

        _updateChannel = config.Bind("update_channel", DefaultUpdateChannel, "Update channel",
            "'stable' follows the newest published release, which is what almost everyone wants. Any " +
            "other value also picks up pre-releases, which the publisher uses for release candidates.");
        _updateChannelCache = Normalize(_updateChannel.Value, DefaultUpdateChannel);
        _updateChannel.Changed += static (_, current) => _updateChannelCache = Normalize(current, DefaultUpdateChannel);

        _updateManifestUrl = config.Bind("update_manifest_url", DefaultUpdateManifestUrl, "Update manifest URL",
            "Where to look for releases. A GitHub 'owner/repo' shorthand or a direct https URL to an " +
            "update-manifest.json. Leave empty to disable update checking entirely.");
        // Trimmed but not defaulted: an empty value is a deliberate "never check", and defaulting it back
        // would override the one switch that turns the network off completely.
        _updateManifestUrlCache = _updateManifestUrl.Value?.Trim() ?? DefaultUpdateManifestUrl;
        _updateManifestUrl.Changed += static (_, current) => _updateManifestUrlCache = current?.Trim() ?? string.Empty;

        _allowMutatingProbes = config.Bind(AllowMutatingProbesKey, false, "Allow mutating probes",
            "Lets the diagnostics suite write game memory to answer questions a read cannot. " +
            "One of those writes can kill the game process outright. Leave this off unless you are " +
            "deliberately running a probe by name on a throwaway save.");
        _allowMutatingProbesCache = _allowMutatingProbes.Value;
        _allowMutatingProbes.Changed += static (_, current) => _allowMutatingProbesCache = current;

        _outputPaneOpen = config.Bind("output_pane_open", true, "Show the output pane",
            "Whether the Actions tab shows its output pane expanded. Collapsing it gives the whole " +
            "page to the action list; the pane keeps recording either way.");
        _outputPaneOpenCache = _outputPaneOpen.Value;
        _outputPaneOpen.Changed += static (_, current) => _outputPaneOpenCache = current;

        _disabledTutorialChapters = config.Bind("tutorial_disabled_chapters", string.Empty,
            "Disabled tutorial chapters",
            "Comma-separated chapter ids the tutorial line skips. Edit from the Tutorial tab on the " +
            "Expansions screen. Ids belonging to mods that are not installed are kept as written.");
        _disabledTutorialChaptersCache = _disabledTutorialChapters.Value ?? string.Empty;
        _disabledTutorialChapters.Changed += static (_, current) =>
        {
            _disabledTutorialChaptersCache = current ?? string.Empty;
            DisabledTutorialChaptersChanged?.Invoke();
        };

        _config = config;

        // Announced even though nothing has changed: a listener that asked for the value before this
        // ran would otherwise be stuck with the empty default for the session.
        DisabledTutorialChaptersChanged?.Invoke();
    }

    private static KeyCode ParseHotkey(string? name, KeyCode fallback) =>
        Enum.TryParse<KeyCode>(name, ignoreCase: true, out var key) ? key : fallback;

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string ToPascalCase(string id)
    {
        var builder = new StringBuilder(id.Length);
        var capitalize = true;

        foreach (var c in id)
        {
            if (c is '_' or '-' or ' ')
            {
                capitalize = true;
                continue;
            }

            builder.Append(capitalize ? char.ToUpperInvariant(c) : c);
            capitalize = false;
        }

        return builder.ToString();
    }
}
