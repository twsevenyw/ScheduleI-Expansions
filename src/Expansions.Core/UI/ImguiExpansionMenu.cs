using Expansions.Core.Actions;
using Expansions.Core.Configuration;
using Expansions.Core.Diagnostics;
using Expansions.Core.Tutorial;
using UnityEngine;

namespace Expansions.Core.UI;

/// <summary>
/// Degradation path for <see cref="NativeExpansionMenu"/>: keeps the modules switchable and the actions
/// runnable on a build where the main menu cannot be found. Deliberately plain — it exists to be
/// correct, not pretty.
/// <para>
/// It carries the whole Actions surface rather than just the toggles, because this is the path taken
/// when the native screen is unavailable, and the console may be unavailable too. Falling back to a UI
/// with no way to re-enable the console would leave nothing at all.
/// </para>
/// <para>
/// Absolute <c>GUI</c> rects only: <c>GUILayout.TextField</c> and <c>GUI.DrawTexture</c> are stripped on
/// this IL2CPP build and abort the rest of the draw when called. That also rules out a search box, so
/// the picker pages instead of filtering.
/// </para>
/// </summary>
public sealed class ImguiExpansionMenu : IExpansionMenu
{
    private const int WindowId = 0x0E4A11;
    private const int ModulePageSize = 5;
    private const int ActionPageSize = 6;
    private const int ChoicePageSize = 10;
    private const int OutputLines = 6;
    private const float ModuleRowHeight = 58f;
    private const float ActionRowHeight = 46f;
    private const float ChoiceRowHeight = 26f;

    private static Texture2D? _panel;
    private static GUIStyle? _header;
    private static GUIStyle? _title;
    private static GUIStyle? _body;
    private static GUIStyle? _dim;
    private static GUIStyle? _bad;
    private static GUIStyle? _button;

    private GUI.WindowFunction? _draw;
    private Rect _window = new(72f, 72f, 700f, 560f);
    private CursorLockMode _previousLock = CursorLockMode.Locked;
    private bool _previousCursorVisible;
    private bool _open;
    private bool _showActions = true;
    private int _modulePage;
    private int _actionPage;
    private int _choicePage;
    private bool _probeRequested;

    private ExpansionAction? _pendingAction;
    private ActionChoice? _pendingChoice;
    private ExpansionAction? _picker;
    private IReadOnlyList<ActionChoice> _choices = Array.Empty<ActionChoice>();

    public string Name => "IMGUI placeholder";

    public bool IsOpen => _open;

    public void Open()
    {
        if (_open)
            return;

        _previousLock = Cursor.lockState;
        _previousCursorVisible = Cursor.visible;
        _open = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Close()
    {
        if (!_open)
            return;

        _open = false;
        ClosePicker();
        Cursor.lockState = _previousLock == CursorLockMode.None ? CursorLockMode.Locked : _previousLock;
        Cursor.visible = _previousCursorVisible;
    }

    public void OnAttached()
    {
    }

    public void OnDetached() => Close();

    public void OnUpdate()
    {
        if (!_open)
            return;

        // The game re-locks the cursor every frame during gameplay.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_picker is not null)
                ClosePicker();
            else
                Close();
        }

        if (Input.GetKeyDown(ExpansionConfig.ProbeHotkey))
            _probeRequested = true;

        // Deferred out of OnGui: these run long enough that starting one mid-draw would leave the
        // IMGUI event half-processed.
        if (_probeRequested)
        {
            _probeRequested = false;
            ActionRegistry.Invoke("core.probes.run_all");
        }

        var action = _pendingAction;
        var choice = _pendingChoice;
        _pendingAction = null;
        _pendingChoice = null;

        if (action is null)
            return;

        if (choice is null && action.HasPicker)
        {
            _picker = action;
            _choices = ActionRegistry.ChoicesFor(action);
            _choicePage = 0;
            return;
        }

        if (choice is null)
            ActionRegistry.Invoke(action);
        else
            ActionRegistry.Invoke(action, choice);
    }

    public void OnGui()
    {
        if (!_open)
            return;

        // Two surfaces drawing at once would put an IMGUI window over the native screen, which is
        // exactly the kind of thing that reads as "something is covering the menu". ExpansionMenu only
        // pumps the current implementation, so this is belt and braces against a third party holding a
        // reference to a detached menu and pumping it itself.
        if (!ReferenceEquals(ExpansionMenu.Current, this))
        {
            Close();
            return;
        }

        EnsureStyles();

        // Cached: the interop delegate has to stay alive, and rebuilding it every frame churns
        // Il2CppInterop handles.
        _draw ??= (GUI.WindowFunction)(Action<int>)DrawWindow;
        _window = GUI.Window(WindowId, _window, _draw, "Schedule I Expansions");
    }

    public void OnSceneChanged(int buildIndex, string sceneName) => ClosePicker();

    private void DrawWindow(int id)
    {
        var tabWidth = 110f;

        if (GUI.Button(new Rect(14f, 26f, tabWidth, 24f), _showActions ? "[Actions]" : "Actions", _button))
            _showActions = true;

        if (GUI.Button(new Rect(14f + tabWidth + 6f, 26f, tabWidth, 24f), _showActions ? "Mods" : "[Mods]", _button))
        {
            _showActions = false;
            ClosePicker();
        }

        if (GUI.Button(new Rect(_window.width - 158f, 26f, 144f, 24f), "Close", _button))
            Close();

        if (_picker is not null)
            DrawPicker();
        else if (_showActions)
            DrawActions();
        else
            DrawModules();

        DrawFooter();
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
    }

    private void DrawModules()
    {
        var contexts = ExpansionRegistry.Contexts;
        var pages = Mathf.Max(1, Mathf.CeilToInt(contexts.Count / (float)ModulePageSize));
        _modulePage = Mathf.Clamp(_modulePage, 0, pages - 1);

        GUI.Label(
            new Rect(14f, 58f, _window.width - 28f, 22f),
            contexts.Count == 0
                ? "No expansion modules registered."
                : $"{contexts.Count} module(s). Changes apply immediately and persist.",
            _header);

        var first = _modulePage * ModulePageSize;
        var last = Mathf.Min(first + ModulePageSize, contexts.Count);
        var y = 86f;

        for (var i = first; i < last; i++)
        {
            var context = contexts[i];
            var enabled = context.IsActive;

            GUI.Label(new Rect(14f, y, _window.width - 200f, 20f), $"{context.Module.DisplayName}   v{context.Module.Version}", _title);
            GUI.Label(new Rect(14f, y + 20f, _window.width - 200f, 34f), context.Module.Description, _body);

            var previousColor = GUI.backgroundColor;
            GUI.backgroundColor = enabled ? new Color(0.30f, 0.75f, 0.45f) : new Color(0.55f, 0.30f, 0.30f);
            if (GUI.Button(new Rect(_window.width - 172f, y + 10f, 158f, 32f), enabled ? "Enabled" : "Disabled", _button))
                ExpansionRegistry.Toggle(context.Module.Id);
            GUI.backgroundColor = previousColor;

            y += ModuleRowHeight;
        }

        DrawPager(pages, ref _modulePage);
    }

    private void DrawActions()
    {
        var actions = ActionRegistry.Actions;
        var pages = Mathf.Max(1, Mathf.CeilToInt(actions.Count / (float)ActionPageSize));
        _actionPage = Mathf.Clamp(_actionPage, 0, pages - 1);

        GUI.Label(
            new Rect(14f, 58f, _window.width - 28f, 22f),
            actions.Count == 0
                ? "No actions registered."
                : $"{actions.Count} action(s). Nothing here needs the in-game console.",
            _header);

        var first = _actionPage * ActionPageSize;
        var last = Mathf.Min(first + ActionPageSize, actions.Count);
        var y = 86f;

        for (var i = first; i < last; i++)
        {
            var action = actions[i];
            var availability = action.GetAvailability();

            GUI.Label(new Rect(14f, y, _window.width - 190f, 18f), action.CurrentLabel(), _title);
            GUI.Label(
                new Rect(14f, y + 17f, _window.width - 190f, 28f),
                availability.IsAvailable ? action.Description : $"Unavailable: {availability.Reason}",
                availability.IsAvailable ? _body : _bad);

            var wasEnabled = GUI.enabled;
            GUI.enabled = availability.IsAvailable;
            if (GUI.Button(new Rect(_window.width - 162f, y + 8f, 148f, 26f), action.HasPicker ? "Choose..." : "Run", _button))
                _pendingAction = action;
            GUI.enabled = wasEnabled;

            y += ActionRowHeight;
        }

        DrawPager(pages, ref _actionPage);
    }

    private void DrawPicker()
    {
        var action = _picker;
        if (action is null)
            return;

        var pages = Mathf.Max(1, Mathf.CeilToInt(_choices.Count / (float)ChoicePageSize));
        _choicePage = Mathf.Clamp(_choicePage, 0, pages - 1);

        GUI.Label(new Rect(14f, 58f, _window.width - 28f, 22f), action.CurrentLabel(), _header);

        var first = _choicePage * ChoicePageSize;
        var last = Mathf.Min(first + ChoicePageSize, _choices.Count);
        var y = 86f;

        if (_choices.Count == 0)
            GUI.Label(new Rect(14f, y, _window.width - 28f, 22f), "Nothing to choose from.", _body);

        for (var i = first; i < last; i++)
        {
            var choice = _choices[i];

            if (GUI.Button(new Rect(14f, y, 260f, 22f), choice.Label, _button))
            {
                _pendingAction = action;
                _pendingChoice = choice;
                ClosePicker();
                return;
            }

            GUI.Label(new Rect(282f, y, _window.width - 296f, 22f), choice.Detail, _dim);
            y += ChoiceRowHeight;
        }

        DrawPager(pages, ref _choicePage);

        if (GUI.Button(new Rect(_window.width - 162f, _window.height - 176f, 148f, 26f), "Cancel", _button))
            ClosePicker();
    }

    private void DrawPager(int pages, ref int page)
    {
        if (pages <= 1)
            return;

        var y = _window.height - 176f;

        if (GUI.Button(new Rect(14f, y, 72f, 26f), "Prev", _button))
            page = Mathf.Max(0, page - 1);

        if (GUI.Button(new Rect(92f, y, 72f, 26f), "Next", _button))
            page = Mathf.Min(pages - 1, page + 1);

        GUI.Label(new Rect(174f, y, 120f, 26f), $"Page {page + 1}/{pages}", _body);
    }

    /// <summary>
    /// The last few output lines, so an action's answer is visible here too. Newest last, matching the
    /// native pane and the console this replaces.
    /// </summary>
    private void DrawFooter()
    {
        var lines = ActionLog.Snapshot;
        var y = _window.height - 142f;

        GUI.Label(new Rect(14f, y, _window.width - 28f, 18f), "Output", _header);
        y += 18f;

        var start = Mathf.Max(0, lines.Count - OutputLines);
        for (var i = start; i < lines.Count; i++)
        {
            var line = lines[i];
            GUI.Label(
                new Rect(14f, y, _window.width - 28f, 16f),
                $"{line.Stamp}  {line.Message}",
                line.Outcome == ActionOutcome.Failed ? _bad : _dim);
            y += 16f;
        }

        if (lines.Count == 0)
        {
            GUI.Label(
                new Rect(14f, y, _window.width - 28f, 16f),
                "Action results and file paths appear here.",
                _dim);
        }

        GUI.Label(
            new Rect(14f, _window.height - 26f, _window.width - 28f, 20f),
            $"Fallback UI - the native screen was unavailable. {ExpansionConfig.MenuHotkey} or Escape closes it. " +
            $"Tutorial: {TutorialDirector.Status}",
            _dim);
    }

    private void ClosePicker()
    {
        _picker = null;
        _choices = Array.Empty<ActionChoice>();
        _choicePage = 0;
    }

    private static void EnsureStyles()
    {
        if (_button is not null)
            return;

        _panel = SolidTexture(new Color(0.07f, 0.09f, 0.11f, 0.96f));

        _header = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.65f, 1f, 0.78f) }
        };
        _title = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        _body = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            wordWrap = true,
            normal = { textColor = new Color(0.78f, 0.82f, 0.88f) }
        };
        _dim = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            wordWrap = false,
            clipping = TextClipping.Clip,
            normal = { textColor = new Color(0.66f, 0.70f, 0.75f) }
        };
        _bad = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            wordWrap = true,
            normal = { textColor = new Color(0.91f, 0.45f, 0.42f) }
        };
        _button = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter
        };

        GUI.skin.window.normal.background = _panel;
        GUI.skin.window.onNormal.background = _panel;
    }

    private static Texture2D SolidTexture(Color color)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.SetPixels(new[] { color, color, color, color });
        texture.Apply();
        return texture;
    }
}
