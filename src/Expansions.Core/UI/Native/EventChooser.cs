using Expansions.Core.Actions;
using Expansions.Core.Configuration;
using Expansions.Core.Events;
using Expansions.Core.Logging;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Expansions.Core.UI.Native;

/// <summary>
/// The compact overlay the event hotkey raises in-world: one row per registered event, arrow keys to
/// move, Enter or a number key to fire.
/// <para>
/// A chooser rather than a cycle-with-indicator. With three feature mods each contributing events, a
/// cycle needs up to N presses to reach the one you want, shows one label at a time, and can never
/// explain why an entry is unavailable. The chooser shows every event with its description and its
/// refusal reason at once, numbers the first nine for direct access, and still reaches the common
/// case in two keys — the last-fired event is pre-selected, so hotkey then Enter repeats it. The
/// genuinely-one-key path is shift plus the hotkey, which skips the overlay entirely.
/// </para>
/// <para>
/// It has two presentations on one panel. <em>Choosing</em> blocks raycasts and holds the game in its
/// own UI state, because the arrow keys have to mean the list rather than the player. <em>Toast</em>
/// does neither: after firing, control goes straight back to the player and the panel lingers for a
/// couple of seconds carrying the result, which is what makes the shift fast path usable mid-play.
/// </para>
/// </summary>
internal sealed class EventChooser
{
    private readonly ModuleLogger _log;
    private readonly List<Row> _rows = new();
    private readonly List<ExpansionEvent> _shown = new();

    private GameObject? _root;
    private Canvas? _canvas;
    private CanvasScaler? _scaler;
    private GraphicRaycaster? _raycaster;
    private CanvasGroup? _group;
    private RectTransform? _panel;
    private RectTransform? _listFrame;
    private ScrollView? _scroll;
    private TextMeshProUGUI? _heading;
    private TextMeshProUGUI? _footer;
    private TextMeshProUGUI? _status;
    private GameObject? _statusGo;

    private int _selected;
    private float _toastUntil;
    private ExpansionEvent? _pendingPick;

    public EventChooser(ModuleLogger log) => _log = log;

    /// <summary>What the overlay is doing. Only <see cref="Mode.Choosing"/> owns the keyboard.</summary>
    public enum Mode
    {
        Hidden,
        Choosing,
        Toast,
    }

    public Mode Current { get; private set; } = Mode.Hidden;

    public bool IsBuilt => InteropObjects.Alive(_root);

    public bool IsChoosing => Current == Mode.Choosing;

    public bool IsVisible => Current != Mode.Hidden;

    /// <summary>The event a click or a key selected, cleared by the caller. See <see cref="TakePick"/>.</summary>
    public ExpansionEvent? TakePick()
    {
        var pick = _pendingPick;
        _pendingPick = null;
        return pick;
    }

    /// <summary>Builds the canvas. Throws on genuine construction failure so the caller can degrade.</summary>
    public void EnsureBuilt()
    {
        if (IsBuilt)
            return;

        Discard();

        _root = UiFactory.New("Expansions_EventCanvas", null);
        UnityEngine.Object.DontDestroyOnLoad(_root);

        _canvas = _root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = MenuStyle.EventSortingOrder;
        _canvas.overrideSorting = true;
        _canvas.referencePixelsPerUnit = MenuStyle.ReferencePixelsPerUnit;
        _canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 |
                                           AdditionalCanvasShaderChannels.Normal |
                                           AdditionalCanvasShaderChannels.Tangent;

        _scaler = _root.AddComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        _scaler.referenceResolution = MenuStyle.ReferenceResolution;
        _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        _scaler.matchWidthOrHeight = MenuStyle.MatchWidthOrHeight;
        _scaler.referencePixelsPerUnit = MenuStyle.ReferencePixelsPerUnit;

        _raycaster = _root.AddComponent<GraphicRaycaster>();
        _raycaster.ignoreReversedGraphics = true;
        _raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

        _group = _root.AddComponent<CanvasGroup>();
        _group.alpha = 1f;
        _group.blocksRaycasts = false;

        BuildPanel(_root.transform);
        SetRenderingEnabled(false);
    }

    /// <summary>
    /// Opens the list. The last-fired event is pre-selected, so the common case — run the same thing
    /// again — is the hotkey followed by Enter.
    /// </summary>
    public void OpenChooser()
    {
        EnsureBuilt();
        Populate();

        Current = Mode.Choosing;
        _toastUntil = 0f;

        if (_statusGo != null)
            _statusGo.SetActive(false);

        if (_listFrame != null)
            _listFrame.gameObject.SetActive(true);

        if (_group != null)
            _group.blocksRaycasts = true;

        SetRenderingEnabled(true);
        Layout();
        ScrollToSelection();
    }

    /// <summary>
    /// Shows one line for a couple of seconds without taking the keyboard or the cursor. This is the
    /// whole of the shift-fast-path's UI, and it is also where the chooser goes after firing.
    /// </summary>
    public void ShowToast(string message, ActionOutcome outcome)
    {
        try
        {
            EnsureBuilt();
        }
        catch (Exception ex)
        {
            _log.Debug($"The event toast could not be built ({ex.GetType().Name}: {ex.Message}).");
            return;
        }

        Current = Mode.Toast;
        _toastUntil = Now() + MenuStyle.EventToastSeconds;

        if (_listFrame != null)
            _listFrame.gameObject.SetActive(false);

        if (_statusGo != null)
            _statusGo.SetActive(true);

        if (_status != null)
        {
            _status.text = message;
            _status.color = outcome == ActionOutcome.Failed ? MenuStyle.TextBad : MenuStyle.Text;
        }

        if (_heading != null)
            _heading.text = "EXPANSION EVENT";

        if (_footer != null)
            _footer.text = FooterHint();

        if (_group != null)
            _group.blocksRaycasts = false;

        SetRenderingEnabled(true);
        Layout();
    }

    public void Hide()
    {
        Current = Mode.Hidden;
        _toastUntil = 0f;
        _pendingPick = null;

        if (_group != null)
            _group.blocksRaycasts = false;

        SetRenderingEnabled(false);
        ClearRows();
    }

    /// <summary>Per-frame housekeeping: the toast's own expiry and the rows' availability.</summary>
    public void Tick()
    {
        switch (Current)
        {
            case Mode.Toast when Now() >= _toastUntil:
                Hide();
                return;

            case Mode.Choosing:
                RefreshRows();
                return;
        }
    }

    /// <summary>Moves the selection by <paramref name="delta"/>, wrapping at both ends.</summary>
    public void Move(int delta)
    {
        if (_shown.Count == 0)
            return;

        _selected = ((_selected + delta) % _shown.Count + _shown.Count) % _shown.Count;
        ApplySelection();
        ScrollToSelection();
    }

    /// <summary>Queues the highlighted event. Null when the list is empty.</summary>
    public void PickSelected()
    {
        if (_selected >= 0 && _selected < _shown.Count)
            _pendingPick = _shown[_selected];
    }

    /// <summary>Queues the <paramref name="index"/>-th row, for the 1-9 shortcuts. Out of range is ignored.</summary>
    public void PickIndex(int index)
    {
        if (index < 0 || index >= _shown.Count)
            return;

        _selected = index;
        ApplySelection();
        _pendingPick = _shown[index];
    }

    /// <summary>Wheel and page keys over the list, so a long event set is reachable with the mouse.</summary>
    public void TickScroll()
    {
        if (Current == Mode.Choosing)
            _scroll?.Tick(allowKeys: false);
    }

    public void PollHover()
    {
        if (Current != Mode.Choosing)
            return;

        foreach (var row in _rows)
        {
            if (row.Hover.Entered())
                GameSounds.PlayHover();
        }
    }

    public void Destroy()
    {
        ClearRows();

        if (InteropObjects.Alive(_root))
            UnityEngine.Object.Destroy(_root);

        Discard();
    }

    private static float Now()
    {
        try
        {
            return Time.unscaledTime;
        }
        catch
        {
            return 0f;
        }
    }

    private void Discard()
    {
        _root = null;
        _canvas = null;
        _scaler = null;
        _raycaster = null;
        _group = null;
        _panel = null;
        _listFrame = null;
        _scroll = null;
        _heading = null;
        _footer = null;
        _status = null;
        _statusGo = null;
        _shown.Clear();
        _selected = 0;
        Current = Mode.Hidden;
    }

    private void SetRenderingEnabled(bool enabled)
    {
        if (_canvas != null)
            _canvas.enabled = enabled;

        if (_raycaster != null)
            _raycaster.enabled = enabled;
    }

    private void BuildPanel(Transform parent)
    {
        var panelGo = UiFactory.New("Panel", parent);
        _panel = UiFactory.Rect(panelGo);
        _panel.anchorMin = new Vector2(0.5f, MenuStyle.EventPanelScreenY);
        _panel.anchorMax = new Vector2(0.5f, MenuStyle.EventPanelScreenY);
        _panel.pivot = new Vector2(0.5f, 0.5f);
        _panel.anchoredPosition = Vector2.zero;
        _panel.sizeDelta = new Vector2(MenuStyle.EventPanelWidth, 200f);

        var backgroundGo = UiFactory.New("Background", panelGo.transform);
        UiFactory.Stretch(UiFactory.Rect(backgroundGo));
        UiFactory.Rounded(backgroundGo, MenuStyle.Backdrop, MenuStyle.PanelRadius).raycastTarget = true;

        var headingGo = UiFactory.New("Heading", panelGo.transform);
        UiFactory.AnchorTop(
            UiFactory.Rect(headingGo), MenuStyle.EventPad, MenuStyle.EventPad, MenuStyle.EventHeaderHeight);
        _heading = UiFactory.Text(
            headingGo,
            GameFonts.Title,
            MenuStyle.EventHeaderSize,
            MenuStyle.Text,
            TextAlignmentOptions.MidlineLeft,
            false);
        _heading.text = "EXPANSION EVENTS";

        var listGo = UiFactory.New("EventList", panelGo.transform);
        _listFrame = UiFactory.Rect(listGo);
        UiFactory.AnchorTopBox(
            _listFrame,
            MenuStyle.EventPad,
            MenuStyle.EventPad,
            MenuStyle.EventPad + MenuStyle.EventHeaderHeight + MenuStyle.EventHeaderGap,
            MenuStyle.EventRowHeight);

        _scroll = ScrollView.Build(listGo, 0f, MenuStyle.EventRowGap);

        _statusGo = UiFactory.New("Status", panelGo.transform);
        UiFactory.AnchorTopBox(
            UiFactory.Rect(_statusGo),
            MenuStyle.EventPad,
            MenuStyle.EventPad,
            MenuStyle.EventPad + MenuStyle.EventHeaderHeight + MenuStyle.EventHeaderGap,
            MenuStyle.EventRowHeight);
        _status = UiFactory.Text(
            _statusGo,
            GameFonts.Title,
            MenuStyle.EventTitleSize,
            MenuStyle.Text,
            TextAlignmentOptions.TopLeft,
            true);
        _status.overflowMode = TextOverflowModes.Ellipsis;
        _statusGo.SetActive(false);

        var footerGo = UiFactory.New("Footer", panelGo.transform);
        UiFactory.AnchorBottom(
            UiFactory.Rect(footerGo), MenuStyle.EventPad, MenuStyle.EventPad, MenuStyle.EventFooterHeight);
        _footer = UiFactory.Text(
            footerGo,
            GameFonts.Description,
            MenuStyle.EventFooterSize,
            MenuStyle.TextDim,
            TextAlignmentOptions.MidlineLeft,
            false);
        _footer.overflowMode = TextOverflowModes.Ellipsis;
        _footer.text = FooterHint();
    }

    private void Populate()
    {
        ClearRows();

        var events = EventRegistry.Events;
        _shown.AddRange(events);

        if (_scroll is not null)
        {
            for (var i = 0; i < _shown.Count; i++)
                _rows.Add(Row.Build(_scroll.Content.transform, i, _shown[i], Pick));
        }

        _selected = IndexOfLastFired();
        ApplySelection();

        if (_heading != null)
        {
            _heading.text = _shown.Count == 0
                ? "EXPANSION EVENTS - NONE REGISTERED"
                : $"EXPANSION EVENTS ({_shown.Count})";
        }

        if (_footer != null)
            _footer.text = FooterHint();

        _scroll?.Rebuild();
        _scroll?.ScrollToTop();
    }

    private int IndexOfLastFired()
    {
        var last = EventRegistry.LastFiredId;
        if (last.Length == 0)
            return 0;

        for (var i = 0; i < _shown.Count; i++)
        {
            if (string.Equals(_shown[i].Id, last, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }

    private string FooterHint()
    {
        var key = ExpansionConfig.EventHotkey;

        if (_shown.Count == 0 && Current != Mode.Toast)
        {
            return "Feature mods register their events here. Nothing has registered one yet - " +
                   "Escape closes.";
        }

        var last = EventRegistry.LastFired;
        var repeat = last is null
            ? $"Shift+{key} repeats the last one"
            : $"Shift+{key} repeats '{last.CurrentLabel()}'";

        return Current == Mode.Toast
            ? $"{key} opens the list. {repeat}."
            : $"Up/Down select - Enter fires - 1-9 fire directly - Escape closes. {repeat}.";
    }

    private void ApplySelection()
    {
        for (var i = 0; i < _rows.Count; i++)
            _rows[i].SetSelected(i == _selected);
    }

    private void RefreshRows()
    {
        foreach (var row in _rows)
            row.Refresh();
    }

    private void ScrollToSelection()
    {
        if (_scroll is null || _rows.Count == 0)
            return;

        var pitch = MenuStyle.EventRowHeight + MenuStyle.EventRowGap;
        _scroll.EnsureVisible(_selected * pitch, MenuStyle.EventRowHeight);
    }

    /// <summary>
    /// Queued rather than fired here: running an event from inside <c>Button.onClick</c> would tear
    /// down the button that is still dispatching the event.
    /// </summary>
    private void Pick(ExpansionEvent picked) => _pendingPick = picked;

    /// <summary>Resizes the panel around whichever presentation is showing.</summary>
    private void Layout()
    {
        if (_panel is null)
            return;

        float body;

        if (Current == Mode.Choosing && _scroll is not null)
        {
            _scroll.Rebuild();

            var cap = (MenuStyle.EventMaxVisibleRows * MenuStyle.EventRowHeight) +
                      ((MenuStyle.EventMaxVisibleRows - 1) * MenuStyle.EventRowGap);
            body = Mathf.Clamp(_scroll.ContentHeight, MenuStyle.EventRowHeight, cap);

            if (_listFrame is not null)
                _listFrame.offsetMin = new Vector2(_listFrame.offsetMin.x, -(Top() + body));
        }
        else
        {
            body = MenuStyle.EventToastHeight;

            if (_statusGo is not null)
            {
                UiFactory.AnchorTopBox(
                    UiFactory.Rect(_statusGo), MenuStyle.EventPad, MenuStyle.EventPad, Top(), body);
            }
        }

        _panel.sizeDelta = new Vector2(
            MenuStyle.EventPanelWidth,
            Top() + body + MenuStyle.EventFooterGap + MenuStyle.EventFooterHeight + MenuStyle.EventPad);
    }

    private static float Top() =>
        MenuStyle.EventPad + MenuStyle.EventHeaderHeight + MenuStyle.EventHeaderGap;

    private void ClearRows()
    {
        foreach (var row in _rows)
            row.Destroy();

        _rows.Clear();
        _shown.Clear();
    }

    /// <summary>One event: an index, its label, and either its description or why it cannot run.</summary>
    private sealed class Row
    {
        private readonly List<UiCallback> _callbacks = new();
        private readonly ExpansionEvent _event;
        private readonly Image _plate;
        private readonly TextMeshProUGUI _label;
        private readonly TextMeshProUGUI _detail;

        private bool _selected;
        private bool _rendered;
        private bool _lastAvailable = true;
        private string _lastReason = string.Empty;

        private Row(
            ExpansionEvent expansionEvent,
            GameObject root,
            Image plate,
            HoverWatcher hover,
            TextMeshProUGUI label,
            TextMeshProUGUI detail)
        {
            _event = expansionEvent;
            Root = root;
            _plate = plate;
            Hover = hover;
            _label = label;
            _detail = detail;
        }

        internal GameObject Root { get; }

        internal HoverWatcher Hover { get; }

        internal static Row Build(
            Transform parent,
            int index,
            ExpansionEvent expansionEvent,
            Action<ExpansionEvent> onPick)
        {
            var go = UiFactory.New($"Event_{expansionEvent.Id}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = MenuStyle.EventRowHeight;

            var callbacks = new List<UiCallback>();
            UiFactory.PlateButton(
                go,
                string.Empty,
                MenuStyle.EventTitleSize,
                TextAlignmentOptions.MidlineLeft,
                () => onPick(expansionEvent),
                callbacks,
                out var labelText,
                out var plate,
                out var hover);

            // Only the first nine get a key; the rest are mouse or arrow keys, and a blank column
            // keeps every label on the same left edge.
            var prefix = index < 9 ? $"{index + 1}" : string.Empty;

            var indexGo = UiFactory.New("Index", go.transform);
            UiFactory.AnchorTopBox(
                UiFactory.Rect(indexGo),
                MenuStyle.EventRowPadX,
                MenuStyle.EventRowWidth - MenuStyle.EventRowPadX - MenuStyle.EventRowIndexWidth,
                4f,
                18f);
            UiFactory.Text(
                    indexGo,
                    GameFonts.Title,
                    MenuStyle.EventTitleSize,
                    MenuStyle.TextDim,
                    TextAlignmentOptions.MidlineLeft,
                    false)
                .text = prefix;

            var textLeft = MenuStyle.EventRowPadX + MenuStyle.EventRowIndexWidth;

            UiFactory.AnchorTopBox(
                UiFactory.Rect(labelText.gameObject), textLeft, MenuStyle.EventRowPadX, 4f, 18f);
            labelText.text = expansionEvent.CurrentLabel();
            labelText.overflowMode = TextOverflowModes.Ellipsis;

            var detailGo = UiFactory.New("Detail", go.transform);
            UiFactory.AnchorTopBox(
                UiFactory.Rect(detailGo), textLeft, MenuStyle.EventRowPadX, 21f, 18f);
            var detail = UiFactory.Text(
                detailGo,
                GameFonts.Description,
                MenuStyle.EventDescSize,
                MenuStyle.TextDim,
                TextAlignmentOptions.TopLeft,
                false);
            detail.overflowMode = TextOverflowModes.Ellipsis;
            detail.text = expansionEvent.Description;

            var row = new Row(expansionEvent, go, plate, new HoverWatcher(hover), labelText, detail);
            row._callbacks.AddRange(callbacks);
            row.Refresh();
            return row;
        }

        internal void SetSelected(bool selected)
        {
            if (_selected == selected)
                return;

            _selected = selected;
            ApplyPlate();
        }

        /// <summary>Re-reads the label and the availability. Cheap enough for every frame it is up.</summary>
        internal void Refresh()
        {
            var label = _event.CurrentLabel();
            if (!string.Equals(_label.text, label, StringComparison.Ordinal))
                _label.text = label;

            var availability = _event.GetAvailability();

            if (_rendered &&
                _lastAvailable == availability.IsAvailable &&
                string.Equals(_lastReason, availability.Reason, StringComparison.Ordinal))
            {
                return;
            }

            _rendered = true;
            _lastAvailable = availability.IsAvailable;
            _lastReason = availability.Reason;

            _detail.text = availability.IsAvailable || availability.Reason.Length == 0
                ? _event.Description
                : $"Unavailable: {availability.Reason}";
            _detail.color = availability.IsAvailable ? MenuStyle.TextDim : MenuStyle.TextBad;
            _label.color = availability.IsAvailable ? MenuStyle.Text : MenuStyle.TextDim;
            ApplyPlate();
        }

        internal void Destroy()
        {
            if (InteropObjects.Alive(Root))
            {
                Root.transform.SetParent(null, false);
                UnityEngine.Object.Destroy(Root);
            }

            _callbacks.Clear();
        }

        private void ApplyPlate() =>
            _plate.color = _selected
                ? MenuStyle.CardOn
                : _lastAvailable ? MenuStyle.CardIdle : MenuStyle.RowDisabled;
    }
}
