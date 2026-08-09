using System.Text;
using Expansions.Core.Actions;
using Expansions.Core.Configuration;
using Il2CppTMPro;
using UnityEngine;

namespace Expansions.Core.UI.Native;

/// <summary>
/// The on-screen replacement for the console output.
/// <para>
/// One wrapping TMP label inside a scroll view rather than a row per line: a probe report path is a
/// hundred characters that must wrap rather than truncate, and rebuilding forty GameObjects every time
/// a line arrives would be the expensive way to get the same picture. Per-line colour comes from rich
/// text, which the game's own SDF material already supports.
/// </para>
/// <para>
/// The pane is bounded and collapsible. It used to be a fixed 168px slice of the panel that the action
/// list could never reclaim, which — with the list not scrolling — read as a box at the bottom of the
/// screen covering the actions. Collapsed it is a single header strip and the list gets the rest;
/// either way it keeps recording, and the choice is persisted in <c>Expansions.cfg</c>.
/// </para>
/// </summary>
internal sealed class OutputPane
{
    private const string Placeholder =
        "Output from actions, probe runs and file paths appears here. Nothing needs the in-game console.";

    private readonly List<UiCallback> _callbacks = new();

    private readonly RectTransform _body;
    private readonly TextMeshProUGUI _text;
    private readonly TextMeshProUGUI _title;
    private readonly TextMeshProUGUI _toggleLabel;
    private readonly ScrollView _scroll;
    private readonly Action _onToggled;

    private int _renderedRevision = -1;

    private OutputPane(
        RectTransform frame,
        RectTransform body,
        TextMeshProUGUI text,
        TextMeshProUGUI title,
        TextMeshProUGUI toggleLabel,
        ScrollView scroll,
        Action onToggled)
    {
        Frame = frame;
        _body = body;
        _text = text;
        _title = title;
        _toggleLabel = toggleLabel;
        _scroll = scroll;
        _onToggled = onToggled;
    }

    /// <summary>The pane's outer box. The page owns where it sits and how tall it is.</summary>
    public RectTransform Frame { get; }

    /// <summary>Follows <see cref="ExpansionConfig.OutputPaneOpen"/>; the page sizes itself off this.</summary>
    public bool IsExpanded => ExpansionConfig.OutputPaneOpen;

    /// <summary>
    /// Builds the pane. <paramref name="onToggled"/> fires after a collapse or expand so the page can
    /// re-measure — the pane does not know what it is inside.
    /// </summary>
    public static OutputPane Build(Transform parent, Action onToggled)
    {
        var frameGo = UiFactory.New("Output", parent);
        UiFactory.Rounded(frameGo, MenuStyle.Sunken, MenuStyle.OutputRadius).raycastTarget = true;

        // Captured by the toggle's click handler, which cannot run before the assignment below.
        OutputPane? pane = null;

        var headerGo = UiFactory.New("Header", frameGo.transform);
        UiFactory.AnchorTop(
            UiFactory.Rect(headerGo), MenuStyle.OutputPadX, 0f, MenuStyle.OutputBarHeight);

        var title = UiFactory.Text(
            headerGo,
            GameFonts.Title,
            MenuStyle.OutputTitleSize,
            MenuStyle.TextDim,
            TextAlignmentOptions.MidlineLeft,
            false);

        var toggleGo = UiFactory.New("Toggle", headerGo.transform);
        UiFactory.AnchorRightMiddle(
            UiFactory.Rect(toggleGo),
            0f,
            new Vector2(MenuStyle.OutputToggleWidth, MenuStyle.OutputBarHeight - 4f));

        var callbacks = new List<UiCallback>();
        UiFactory.SmallButton(
            toggleGo,
            "Hide",
            () => pane?.Toggle(),
            callbacks,
            out var toggleLabel);

        var bodyGo = UiFactory.New("Body", frameGo.transform);
        var body = UiFactory.Rect(bodyGo);
        body.anchorMin = Vector2.zero;
        body.anchorMax = Vector2.one;
        body.pivot = new Vector2(0.5f, 0.5f);
        body.offsetMin = new Vector2(MenuStyle.OutputPadX, MenuStyle.OutputPadY);
        body.offsetMax = new Vector2(-MenuStyle.OutputPadX, -MenuStyle.OutputBarHeight);

        var scroll = ScrollView.Build(bodyGo, 0f, 0f, layoutContent: false);

        var text = UiFactory.Text(
            scroll.Content.gameObject,
            GameFonts.Title,
            MenuStyle.OutputLineSize,
            MenuStyle.TextDim,
            TextAlignmentOptions.TopLeft,
            true);
        text.text = Placeholder;

        pane = new OutputPane(UiFactory.Rect(frameGo), body, text, title, toggleLabel, scroll, onToggled);
        pane._callbacks.AddRange(callbacks);
        pane.ApplyExpansion();
        return pane;
    }

    /// <summary>
    /// The height the pane wants, given how much of the page it is allowed. Collapsed that is the
    /// header strip alone; expanded it is the measured height capped so a tall screen grows the action
    /// list rather than the log.
    /// </summary>
    public float HeightFor(float pageBudget)
    {
        if (!IsExpanded)
            return MenuStyle.OutputBarHeight;

        var floor = MenuStyle.OutputBarHeight + MenuStyle.OutputMinBodyHeight;
        var ceiling = Mathf.Max(floor, pageBudget * MenuStyle.OutputMaxPageFraction);
        return Mathf.Min(MenuStyle.OutputHeight, ceiling);
    }

    /// <summary>
    /// Repaints only when <see cref="ActionLog.Revision"/> moved, so this is a single int compare on
    /// every frame the screen is open.
    /// </summary>
    public void Sync()
    {
        var revision = ActionLog.Revision;
        if (revision == _renderedRevision)
            return;

        _renderedRevision = revision;

        var lines = ActionLog.Snapshot;
        _text.text = lines.Count == 0 ? Placeholder : Render(lines);
        _title.text = lines.Count == 0 ? "OUTPUT" : $"OUTPUT ({lines.Count})";

        if (!IsExpanded)
            return;

        _scroll.Rebuild();
        // Newest line at the bottom, so the pane behaves like the console it replaces.
        _scroll.ScrollToBottom();
    }

    /// <summary>Wheel and keys, when the Actions tab is showing and the pointer is over the pane.</summary>
    public void TickScroll()
    {
        if (IsExpanded)
            _scroll.Tick(allowKeys: false);
    }

    public void Destroy() => _callbacks.Clear();

    private static string Render(IReadOnlyList<ActionLogLine> lines)
    {
        var builder = new StringBuilder(lines.Count * 96);

        foreach (var line in lines)
        {
            if (builder.Length > 0)
                builder.Append('\n');

            builder.Append("<color=#")
                .Append(Hex(MenuStyle.TextDim))
                .Append('>')
                .Append(line.Stamp)
                .Append("</color>  <color=#")
                .Append(Hex(ColourFor(line.Outcome)))
                .Append('>')
                .Append(Escape(line.Message))
                .Append("</color>");
        }

        return builder.ToString();
    }

    private static Color ColourFor(ActionOutcome outcome) => outcome switch
    {
        ActionOutcome.Failed => MenuStyle.TextBad,
        ActionOutcome.NoChange => MenuStyle.TextDim,
        _ => MenuStyle.Text,
    };

    /// <summary>
    /// A Windows path can contain characters TMP reads as markup, and a stray <c>&lt;</c> silently
    /// swallows the rest of the line. Escaping the angle brackets keeps the path readable.
    /// </summary>
    private static string Escape(string text) =>
        text.IndexOf('<') < 0 ? text : text.Replace("<", "<noparse><</noparse>", StringComparison.Ordinal);

    private static string Hex(Color color) =>
        $"{(byte)(color.r * 255f):X2}{(byte)(color.g * 255f):X2}{(byte)(color.b * 255f):X2}";

    private void Toggle()
    {
        ExpansionConfig.OutputPaneOpen = !ExpansionConfig.OutputPaneOpen;
        ApplyExpansion();
        _onToggled();
    }

    private void ApplyExpansion()
    {
        var expanded = IsExpanded;

        if (InteropObjects.Alive(_body))
            _body.gameObject.SetActive(expanded);

        _toggleLabel.text = expanded ? "Hide" : "Show";

        // Forces the next Sync to repaint: the label was not being kept current while collapsed.
        _renderedRevision = -1;
    }
}
