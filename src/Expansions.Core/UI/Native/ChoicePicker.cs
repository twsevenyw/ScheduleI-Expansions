using Expansions.Core.Actions;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Expansions.Core.UI.Native;

/// <summary>
/// The searchable list an action opens when it needs a target — which NPC to teleport to, which probe
/// area to run.
/// <para>
/// Search is fed from <c>Input.inputString</c> rather than a <c>TMP_InputField</c>. Building a working
/// input field at runtime means a caret, a text area and a selection mesh, all wired by the inspector on
/// the game's own fields; reading keystrokes and drawing them into a label is the approach that already
/// works on this build.
/// </para>
/// </summary>
internal sealed class ChoicePicker
{
    private readonly List<Row> _rows = new();

    private readonly RectTransform _root;
    private readonly ScrollView _scroll;
    private readonly TextMeshProUGUI _title;
    private readonly TextMeshProUGUI _search;
    private readonly TextMeshProUGUI _footer;
    private readonly Action<ExpansionAction, ActionChoice> _onPick;

    private ExpansionAction? _action;
    private IReadOnlyList<ActionChoice> _all = Array.Empty<ActionChoice>();
    private string _query = string.Empty;
    private bool _dirty;

    private ChoicePicker(
        RectTransform root,
        ScrollView scroll,
        TextMeshProUGUI title,
        TextMeshProUGUI search,
        TextMeshProUGUI footer,
        Action<ExpansionAction, ActionChoice> onPick)
    {
        _root = root;
        _scroll = scroll;
        _title = title;
        _search = search;
        _footer = footer;
        _onPick = onPick;
    }

    public bool IsOpen { get; private set; }

    public static ChoicePicker Build(
        Transform parent,
        Action<ExpansionAction, ActionChoice> onPick,
        Action onCancel,
        List<UiCallback> keepAlive)
    {
        var rootGo = UiFactory.New("Picker", parent);
        var root = UiFactory.Rect(rootGo);
        UiFactory.Stretch(root);

        // Opaque and raycast-blocking: it covers the action rows it was opened from, and a click that
        // fell through to one of them would run an action the owner never aimed at.
        UiFactory.Rounded(rootGo, MenuStyle.PickerBackdrop, MenuStyle.OutputRadius).raycastTarget = true;

        var titleGo = UiFactory.New("Title", rootGo.transform);
        UiFactory.AnchorTop(UiFactory.Rect(titleGo), MenuStyle.ListPadX, 0f, MenuStyle.PickerTitleHeight);
        var title = UiFactory.Text(
            titleGo,
            GameFonts.Title,
            MenuStyle.PickerTitleSize,
            MenuStyle.Text,
            TextAlignmentOptions.MidlineLeft,
            false);

        var searchGo = UiFactory.New("Search", rootGo.transform);
        UiFactory.AnchorTop(
            UiFactory.Rect(searchGo),
            MenuStyle.ListPadX,
            MenuStyle.PickerTitleHeight,
            MenuStyle.PickerSearchHeight);
        var search = UiFactory.Text(
            searchGo,
            GameFonts.Description,
            MenuStyle.PickerSearchSize,
            MenuStyle.TextDim,
            TextAlignmentOptions.MidlineLeft,
            false);

        var listTop = MenuStyle.PickerTitleHeight + MenuStyle.PickerSearchHeight + MenuStyle.PickerGap;
        var listBottom = MenuStyle.ButtonRowHeight + MenuStyle.PickerGap;

        var listGo = UiFactory.New("ChoiceList", rootGo.transform);
        UiFactory.AnchorMiddle(UiFactory.Rect(listGo), 0f, listTop, listBottom);
        var scroll = ScrollView.Build(listGo, MenuStyle.ListPadX, MenuStyle.PickerRowGap);

        var footerGo = UiFactory.New("Footer", rootGo.transform);
        UiFactory.AnchorBottom(
            UiFactory.Rect(footerGo), MenuStyle.ListPadX, 0f, MenuStyle.ButtonRowHeight);
        var footer = UiFactory.Text(
            footerGo,
            GameFonts.Description,
            MenuStyle.PickerRowDetailSize,
            MenuStyle.TextDim,
            TextAlignmentOptions.MidlineLeft,
            false);

        var cancelGo = UiFactory.New("Cancel", rootGo.transform);
        var cancelRect = UiFactory.Rect(cancelGo);
        cancelRect.anchorMin = new Vector2(1f, 0f);
        cancelRect.anchorMax = new Vector2(1f, 0f);
        cancelRect.pivot = new Vector2(1f, 0f);
        cancelRect.sizeDelta = new Vector2(MenuStyle.PickerCancelWidth, MenuStyle.ButtonRowHeight);
        cancelRect.anchoredPosition = new Vector2(-MenuStyle.ListPadX, 0f);
        UiFactory.SmallButton(cancelGo, "Cancel", onCancel, keepAlive);

        var picker = new ChoicePicker(root, scroll, title, search, footer, onPick);
        rootGo.SetActive(false);
        return picker;
    }

    /// <summary>Opens the picker for <paramref name="action"/>, listing whatever it offers right now.</summary>
    public void Open(ExpansionAction action)
    {
        _action = action;
        _all = ActionRegistry.ChoicesFor(action);
        _query = string.Empty;
        IsOpen = true;
        _dirty = true;

        _title.text = action.CurrentLabel();

        if (InteropObjects.Alive(_root))
            _root.gameObject.SetActive(true);
    }

    public void Close()
    {
        IsOpen = false;
        _action = null;
        _all = Array.Empty<ActionChoice>();
        _query = string.Empty;

        if (InteropObjects.Alive(_root))
            _root.gameObject.SetActive(false);

        Clear();
    }

    /// <summary>
    /// Feeds a frame's keystrokes into the search box. Returns true when the picker consumed input, so
    /// the caller knows not to treat the same keys as menu hotkeys.
    /// </summary>
    public bool ConsumeTyping()
    {
        if (!IsOpen)
            return false;

        var changed = false;

        if (Input.GetKeyDown(KeyCode.Backspace) && _query.Length > 0)
        {
            _query = _query[..^1];
            changed = true;
        }

        var typed = Input.inputString;
        for (var i = 0; i < typed.Length; i++)
        {
            var c = typed[i];

            // Backspace and Return arrive in inputString as control characters; the former is handled
            // above and the latter would only add noise to a filter.
            if (c is '\b' or '\n' or '\r')
                continue;

            if (char.IsControl(c) || _query.Length >= 48)
                continue;

            _query += c;
            changed = true;
        }

        if (changed)
            _dirty = true;

        return true;
    }

    /// <summary>Rebuilds the rows when the filter moved. Called every frame the picker is open.</summary>
    public void Tick()
    {
        if (!IsOpen)
            return;

        // Arrow and page keys are the picker's own; the search box only takes printable characters.
        _scroll.Tick(allowKeys: true);

        if (!_dirty)
            return;

        _dirty = false;
        Populate();
    }

    public void PollHover()
    {
        if (!IsOpen)
            return;

        foreach (var row in _rows)
        {
            if (row.Hover.Entered())
                GameSounds.PlayHover();
        }
    }

    public void Destroy() => Clear();

    private void Populate()
    {
        Clear();

        var matches = Filter();

        _search.text = _query.Length == 0
            ? "Type to filter. Escape closes."
            : $"Filter: {_query}";

        var shown = Mathf.Min(matches.Count, MenuStyle.PickerMaxRows);
        for (var i = 0; i < shown; i++)
            _rows.Add(Row.Build(_scroll.Content.transform, matches[i], Pick));

        _footer.text = matches.Count switch
        {
            0 when _all.Count == 0 => "Nothing to choose from.",
            0 => $"No match in {_all.Count}. Backspace to widen.",
            _ when matches.Count > shown => $"{shown} of {matches.Count} shown - keep typing.",
            _ => $"{matches.Count} of {_all.Count}.",
        };

        _scroll.Rebuild();
        _scroll.ScrollToTop();
    }

    private List<ActionChoice> Filter()
    {
        if (_query.Length == 0)
            return _all.ToList();

        var needle = _query.ToLowerInvariant();
        var matched = new List<ActionChoice>();

        foreach (var choice in _all)
        {
            if (choice.SearchText.Contains(needle, StringComparison.Ordinal))
                matched.Add(choice);
        }

        return matched;
    }

    private void Pick(ActionChoice choice)
    {
        var action = _action;
        Close();

        if (action is not null)
            _onPick(action, choice);
    }

    private void Clear()
    {
        foreach (var row in _rows)
            row.Destroy();

        _rows.Clear();
    }

    /// <summary>One choice: label over a dim detail line, the whole plate clickable.</summary>
    private sealed class Row
    {
        private readonly List<UiCallback> _callbacks = new();

        private Row(GameObject root, HoverWatcher hover)
        {
            Root = root;
            Hover = hover;
        }

        internal GameObject Root { get; }

        internal HoverWatcher Hover { get; }

        internal static Row Build(Transform parent, ActionChoice choice, Action<ActionChoice> onPick)
        {
            var go = UiFactory.New($"Choice_{choice.Id}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = MenuStyle.PickerRowHeight;

            var callbacks = new List<UiCallback>();
            UiFactory.PlateButton(
                go,
                string.Empty,
                MenuStyle.PickerRowLabelSize,
                TextAlignmentOptions.MidlineLeft,
                () => onPick(choice),
                callbacks,
                out var labelText,
                out _,
                out var hover);

            // PlateButton's label stretches the whole plate; re-anchor it to the top half so the detail
            // line has somewhere to go, and inset it so the text is not against the rounded corner.
            UiFactory.AnchorTop(
                UiFactory.Rect(labelText.gameObject), MenuStyle.ActionRowPadX, 2f, 16f);
            labelText.text = choice.Label;

            var detailGo = UiFactory.New("Detail", go.transform);
            UiFactory.AnchorTop(UiFactory.Rect(detailGo), MenuStyle.ActionRowPadX, 17f, 15f);
            UiFactory.Text(
                    detailGo,
                    GameFonts.Description,
                    MenuStyle.PickerRowDetailSize,
                    MenuStyle.TextDim,
                    TextAlignmentOptions.TopLeft,
                    false)
                .text = choice.Detail;

            var row = new Row(go, new HoverWatcher(hover));
            row._callbacks.AddRange(callbacks);
            return row;
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
    }
}
