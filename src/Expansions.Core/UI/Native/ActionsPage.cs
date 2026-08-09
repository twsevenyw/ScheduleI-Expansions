using Expansions.Core.Actions;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Expansions.Core.UI.Native;

/// <summary>
/// The Actions page: every registered action grouped by owning module, over the output pane that shows
/// what they said.
/// <para>
/// The list and the output are one page rather than two screens because the two are read together —
/// clicking "Run all diagnostics probes" is only useful if the report path lands somewhere visible
/// without navigating away. The list takes whatever the pane is not using, and the pane can be
/// collapsed to a single strip, so 46 actions are reachable rather than the first six.
/// </para>
/// </summary>
internal sealed class ActionsPage
{
    private readonly List<ActionRow> _rows = new();
    private readonly List<GameObject> _headers = new();

    private readonly RectTransform _root;
    private readonly RectTransform _listFrame;
    private readonly ScrollView _scroll;
    private readonly TextMeshProUGUI _empty;
    private readonly OutputPane _output;
    private readonly Action<ExpansionAction> _onRun;

    private float _budget = MenuStyle.MaxActionListHeight;
    private int _renderedCount = -1;

    private ActionsPage(
        RectTransform root,
        RectTransform listFrame,
        ScrollView scroll,
        TextMeshProUGUI empty,
        OutputPane output,
        Action<ExpansionAction> onRun)
    {
        _root = root;
        _listFrame = listFrame;
        _scroll = scroll;
        _empty = empty;
        _output = output;
        _onRun = onRun;
    }

    /// <summary>Height the panel has to give this page: the list it wants, plus the output pane.</summary>
    public float PreferredHeight { get; private set; } =
        MenuStyle.ActionRowHeight + MenuStyle.OutputGap + MenuStyle.OutputHeight;

    public static ActionsPage Build(Transform parent, Action<ExpansionAction> onRun, Action onLayoutChanged)
    {
        var pageGo = UiFactory.New("ActionsPage", parent);
        var page = UiFactory.Rect(pageGo);
        UiFactory.Stretch(page);

        var listGo = UiFactory.New("ActionList", pageGo.transform);
        var listFrame = UiFactory.Rect(listGo);
        UiFactory.AnchorTopBox(listFrame, 0f, 0f, 0f, MenuStyle.ActionRowHeight);

        var scroll = ScrollView.Build(listGo, MenuStyle.ListPadX, MenuStyle.ActionRowGap);

        var emptyGo = UiFactory.New("Empty", scroll.Content.transform);
        var empty = UiFactory.Text(
            emptyGo,
            GameFonts.Description,
            MenuStyle.DescSize,
            MenuStyle.TextDim,
            TextAlignmentOptions.Center,
            true);
        empty.text = "No actions are registered.";
        emptyGo.AddComponent<LayoutElement>().preferredHeight = MenuStyle.ActionRowHeight;
        emptyGo.SetActive(false);

        var output = OutputPane.Build(pageGo.transform, onLayoutChanged);

        return new ActionsPage(page, listFrame, scroll, empty, output, onRun);
    }

    public void SetActive(bool active)
    {
        if (InteropObjects.Alive(_root))
            _root.gameObject.SetActive(active);
    }

    /// <summary>
    /// How much page height is available. Set by the screen before <see cref="Sync"/> so the list can
    /// take everything the display allows instead of a constant that was right on one monitor.
    /// </summary>
    public void SetBudget(float budget) => _budget = Mathf.Max(MenuStyle.ActionRowHeight, budget);

    /// <summary>
    /// Rebuilds the rows when the registered set changed, then remeasures. Call while the canvas is
    /// enabled: TMP reports no preferred height until its text has been laid out once.
    /// </summary>
    public void Sync()
    {
        var groups = ActionRegistry.Groups;
        var count = ActionRegistry.Count;

        if (count != _renderedCount || !RowsMatch(groups))
            Rebuild(groups);

        _empty.gameObject.SetActive(count == 0);

        var outputHeight = _output.HeightFor(_budget);
        UiFactory.AnchorBottom(_output.Frame, 0f, 0f, outputHeight);

        var maxList = Mathf.Max(MenuStyle.ActionRowHeight, _budget - MenuStyle.OutputGap - outputHeight);

        _scroll.Rebuild();
        var listHeight = Mathf.Clamp(_scroll.ContentHeight, MenuStyle.ActionRowHeight, maxList);

        // The list frame is anchored from the page's top edge, so its height is what leaves room for
        // the output pane pinned to the bottom.
        _listFrame.offsetMin = new Vector2(_listFrame.offsetMin.x, -listHeight);

        PreferredHeight = listHeight + MenuStyle.OutputGap + outputHeight;

        // Painted here as well as in Tick so the pane is already correct on the frame the screen appears.
        _output.Sync();
    }

    /// <summary>Per-frame: labels and availability change with the game, not with the registry.</summary>
    public void Tick()
    {
        foreach (var row in _rows)
            row.Refresh();

        _output.Sync();
    }

    /// <summary>Wheel over either scrolling region; keys go to the action list.</summary>
    public void TickScroll(bool allowKeys)
    {
        _scroll.Tick(allowKeys);
        _output.TickScroll();
    }

    public void PollHover()
    {
        foreach (var row in _rows)
        {
            if (row.PollHover())
                GameSounds.PlayHover();
        }
    }

    public void ScrollToTop() => _scroll.ScrollToTop();

    public void Destroy()
    {
        foreach (var row in _rows)
            row.Destroy();

        _rows.Clear();
        _headers.Clear();
        _output.Destroy();
    }

    private bool RowsMatch(IReadOnlyList<ActionGroup> groups)
    {
        var index = 0;

        foreach (var group in groups)
        {
            foreach (var action in group.Actions)
            {
                if (index >= _rows.Count)
                    return false;

                if (!ReferenceEquals(_rows[index].Action, action) || !InteropObjects.Alive(_rows[index].Root))
                    return false;

                index++;
            }
        }

        return index == _rows.Count;
    }

    private void Rebuild(IReadOnlyList<ActionGroup> groups)
    {
        foreach (var row in _rows)
            row.Destroy();

        foreach (var header in _headers)
        {
            if (!InteropObjects.Alive(header))
                continue;

            header.transform.SetParent(null, false);
            UnityEngine.Object.Destroy(header);
        }

        _rows.Clear();
        _headers.Clear();

        foreach (var group in groups)
        {
            _headers.Add(BuildHeader(group.Title));

            foreach (var action in group.Actions)
                _rows.Add(ActionRow.Build(_scroll.Content.transform, action, _onRun));
        }

        // The empty-state label is a layout child too, so keep it last in the list.
        _empty.transform.SetAsLastSibling();
        _renderedCount = _rows.Count;
    }

    private GameObject BuildHeader(string title)
    {
        var go = UiFactory.New($"Group_{title}", _scroll.Content.transform);
        UiFactory.Text(
                go,
                GameFonts.Title,
                MenuStyle.GroupHeaderSize,
                MenuStyle.TextDim,
                TextAlignmentOptions.BottomLeft,
                false)
            .text = title.ToUpperInvariant();

        go.AddComponent<LayoutElement>().preferredHeight = MenuStyle.GroupHeaderHeight;
        return go;
    }
}
