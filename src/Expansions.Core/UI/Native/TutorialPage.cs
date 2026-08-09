using Expansions.Core.Actions;
using Expansions.Core.Tutorial;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Expansions.Core.UI.Native;

/// <summary>
/// The Tutorial page: what the quest line is, where it stands, the whole-line controls, then one
/// switchable row per chapter.
/// <para>
/// It is its own tab rather than a section of the Actions list because with 46 actions, three module
/// cards and seven chapters, one scrolling column would bury all three. Start / Restart and Reset stay
/// registered as actions as well, so nothing that used to work by id stops working.
/// </para>
/// <para>
/// The intro paragraph and the progress line are here rather than in a manual because this is the only
/// screen that knows which chapters exist: they are contributed by whichever mods are installed, so
/// anything written ahead of time would be wrong on most installs.
/// </para>
/// </summary>
internal sealed class TutorialPage
{
    /// <summary>Everything above the toolbar: the paragraph explaining the line, then where it stands.</summary>
    private const float HeaderHeight =
        MenuStyle.TutorialIntroHeight + MenuStyle.TutorialIntroGap +
        MenuStyle.TutorialProgressHeight + MenuStyle.TutorialProgressGap;

    private const float ListTop = HeaderHeight + MenuStyle.TutorialToolbarHeight + MenuStyle.TutorialToolbarGap;

    private readonly List<TutorialRow> _rows = new();
    private readonly List<UiCallback> _callbacks = new();
    private readonly List<HoverWatcher> _toolbarHovers = new();

    private readonly RectTransform _root;
    private readonly RectTransform _listFrame;
    private readonly ScrollView _scroll;
    private readonly TextMeshProUGUI _empty;
    private readonly TextMeshProUGUI _startLabel;
    private readonly TextMeshProUGUI _progress;

    private float _budget = MenuStyle.MaxListHeight;
    private bool _startedLabel;
    private bool _wasActive;
    private string _renderedProgress = string.Empty;

    private TutorialPage(
        RectTransform root,
        RectTransform listFrame,
        ScrollView scroll,
        TextMeshProUGUI empty,
        TextMeshProUGUI startLabel,
        TextMeshProUGUI progress)
    {
        _root = root;
        _listFrame = listFrame;
        _scroll = scroll;
        _empty = empty;
        _startLabel = startLabel;
        _progress = progress;
    }

    /// <summary>Height the panel has to give this page: the header, the toolbar, and the list it wants.</summary>
    public float PreferredHeight { get; private set; } = ListTop + MenuStyle.TutorialRowHeight;

    /// <summary><paramref name="onChanged"/> fires after anything that resizes or restates the page.</summary>
    public static TutorialPage Build(Transform parent, Action onChanged)
    {
        var pageGo = UiFactory.New("TutorialPage", parent);
        var page = UiFactory.Rect(pageGo);
        UiFactory.Stretch(page);

        // Captured by the toolbar handlers, none of which can run before the assignment below.
        TutorialPage? built = null;
        var callbacks = new List<UiCallback>();
        var hovers = new List<HoverWatcher>();

        var introGo = UiFactory.New("Intro", pageGo.transform);
        UiFactory.AnchorTop(
            UiFactory.Rect(introGo), MenuStyle.ListPadX, 0f, MenuStyle.TutorialIntroHeight);

        var intro = UiFactory.Text(
            introGo,
            GameFonts.Description,
            MenuStyle.TutorialIntroSize,
            MenuStyle.TextDim,
            TextAlignmentOptions.TopLeft,
            true);
        intro.text = IntroText;

        var progressGo = UiFactory.New("Progress", pageGo.transform);
        UiFactory.AnchorTop(
            UiFactory.Rect(progressGo),
            MenuStyle.ListPadX,
            MenuStyle.TutorialIntroHeight + MenuStyle.TutorialIntroGap,
            MenuStyle.TutorialProgressHeight);

        var progress = UiFactory.Text(
            progressGo,
            GameFonts.Description,
            MenuStyle.TutorialProgressSize,
            MenuStyle.Text,
            TextAlignmentOptions.Left,
            true);

        var toolbarGo = UiFactory.New("Toolbar", pageGo.transform);
        UiFactory.AnchorTop(UiFactory.Rect(toolbarGo), 0f, HeaderHeight, MenuStyle.TutorialToolbarHeight);

        var startLabel = AddToolbarButton(
            toolbarGo.transform, 0, "Start", () => built?.StartOrRestart(onChanged), callbacks, hovers);
        AddToolbarButton(
            toolbarGo.transform, 1, "Reset", () => built?.Reset(onChanged), callbacks, hovers);
        AddToolbarButton(
            toolbarGo.transform, 2, "Enable all", () => built?.SetAll(true, onChanged), callbacks, hovers);
        AddToolbarButton(
            toolbarGo.transform, 3, "Disable all", () => built?.SetAll(false, onChanged), callbacks, hovers);

        var listGo = UiFactory.New("ChapterList", pageGo.transform);
        var listFrame = UiFactory.Rect(listGo);
        UiFactory.AnchorTopBox(listFrame, 0f, 0f, ListTop, MenuStyle.TutorialRowHeight);

        var scroll = ScrollView.Build(listGo, MenuStyle.ListPadX, MenuStyle.TutorialRowGap);

        var emptyGo = UiFactory.New("Empty", scroll.Content.transform);
        var empty = UiFactory.Text(
            emptyGo,
            GameFonts.Description,
            MenuStyle.DescSize,
            MenuStyle.TextDim,
            TextAlignmentOptions.Center,
            true);
        empty.text = "No tutorial chapters are registered.";
        emptyGo.AddComponent<LayoutElement>().preferredHeight = MenuStyle.TutorialRowHeight;
        emptyGo.SetActive(false);

        built = new TutorialPage(page, listFrame, scroll, empty, startLabel, progress);
        built._callbacks.AddRange(callbacks);
        built._toolbarHovers.AddRange(hovers);
        built.RefreshProgress();
        return built;
    }

    public void SetActive(bool active)
    {
        if (InteropObjects.Alive(_root))
            _root.gameObject.SetActive(active);

        // Counted on the edge only: the guide chapter asks the player to look at this tab once, and a page
        // that is re-shown every frame the screen is open would otherwise count thousands of visits.
        if (active && !_wasActive)
            TutorialActivity.TutorialTabViewed();

        _wasActive = active;
    }

    /// <summary>How much page height the screen is willing to give this list.</summary>
    public void SetBudget(float budget) => _budget = Mathf.Max(MenuStyle.TutorialRowHeight, budget);

    /// <summary>Rebuilds the rows when the chapter set changed, then remeasures.</summary>
    public void Sync()
    {
        var chapters = TutorialRegistry.Chapters;

        if (!Matches(chapters))
            Rebuild(chapters);

        _empty.gameObject.SetActive(chapters.Count == 0);
        RefreshRows(chapters);
        RefreshProgress();

        var listBudget = Mathf.Max(MenuStyle.TutorialRowHeight, _budget - ListTop);

        _scroll.Rebuild();
        var listHeight = Mathf.Clamp(_scroll.ContentHeight, MenuStyle.TutorialRowHeight, listBudget);

        _listFrame.offsetMin = new Vector2(_listFrame.offsetMin.x, -(ListTop + listHeight));
        PreferredHeight = ListTop + listHeight;
    }

    /// <summary>Per-frame: a chapter's state moves with the game, not with the registry.</summary>
    public void Tick()
    {
        RefreshRows(TutorialRegistry.Chapters);
        RefreshProgress();

        var started = TutorialDirector.IsStarted;
        if (started == _startedLabel)
            return;

        _startedLabel = started;
        _startLabel.text = started ? "Restart" : "Start";
    }

    public void TickScroll(bool allowKeys) => _scroll.Tick(allowKeys);

    public void PollHover()
    {
        foreach (var row in _rows)
        {
            if (row.PollHover())
                GameSounds.PlayHover();
        }

        foreach (var watcher in _toolbarHovers)
        {
            if (watcher.Entered())
                GameSounds.PlayHover();
        }
    }

    public void ScrollToTop() => _scroll.ScrollToTop();

    public void Destroy()
    {
        foreach (var row in _rows)
            row.Destroy();

        _rows.Clear();
        _callbacks.Clear();
        _toolbarHovers.Clear();
    }

    private static TextMeshProUGUI AddToolbarButton(
        Transform parent,
        int index,
        string label,
        Action onClick,
        List<UiCallback> callbacks,
        List<HoverWatcher> hovers)
    {
        var go = UiFactory.New($"Toolbar_{label}", parent);
        var rect = UiFactory.Rect(go);
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = new Vector2(MenuStyle.TutorialToolbarButtonWidth, 0f);
        rect.anchoredPosition = new Vector2(
            MenuStyle.ListPadX +
            (index * (MenuStyle.TutorialToolbarButtonWidth + MenuStyle.TutorialToolbarGapX)),
            0f);

        UiFactory.PlateButton(
            go,
            label,
            MenuStyle.ButtonLabelSize,
            TextAlignmentOptions.Center,
            onClick,
            callbacks,
            out var text,
            out _,
            out var hover);

        hovers.Add(new HoverWatcher(hover));
        return text;
    }

    /// <summary>
    /// What the line is, in the two facts a player needs before pressing Start: it is played in the phone's
    /// journal like any other quest, and it is assembled from whatever mods are installed.
    /// </summary>
    private static string IntroText =>
        "A quest line that teaches the suite, played in your phone's journal like any other quest. Each " +
        "installed mod contributes its own chapter, so a mod you add later simply appears here. Press Start " +
        "with a game loaded; click any row to switch that chapter off and the line will skip it.";

    /// <summary>
    /// The one line that answers "am I doing this, and how far in am I". Sourced from the director rather
    /// than counted here, so it says the same thing as the footer and the menu status.
    /// </summary>
    private void RefreshProgress()
    {
        var line = TutorialDirector.Status;
        if (line.Length == 0)
            line = "Tutorial status unavailable.";

        if (string.Equals(_renderedProgress, line, StringComparison.Ordinal))
            return;

        _renderedProgress = line;
        _progress.text = line;
    }

    private bool Matches(IReadOnlyList<ITutorialChapter> chapters)
    {
        if (_rows.Count != chapters.Count)
            return false;

        for (var i = 0; i < chapters.Count; i++)
        {
            if (!string.Equals(_rows[i].ChapterId, chapters[i].Id, StringComparison.Ordinal))
                return false;

            if (!InteropObjects.Alive(_rows[i].Root))
                return false;
        }

        return true;
    }

    private void Rebuild(IReadOnlyList<ITutorialChapter> chapters)
    {
        foreach (var row in _rows)
            row.Destroy();

        _rows.Clear();

        foreach (var chapter in chapters)
            _rows.Add(TutorialRow.Build(_scroll.Content.transform, chapter, Toggle));

        // The empty-state label is a layout child too, so keep it last in the list.
        _empty.transform.SetAsLastSibling();
    }

    private void RefreshRows(IReadOnlyList<ITutorialChapter> chapters)
    {
        var count = Mathf.Min(_rows.Count, chapters.Count);
        for (var i = 0; i < count; i++)
            _rows[i].Refresh(chapters[i]);
    }

    private void Toggle(string chapterId)
    {
        var chapter = TutorialRegistry.Find(chapterId);
        var title = chapter?.Title ?? chapterId;
        var enabled = !TutorialSettings.IsEnabled(chapterId);

        if (!TutorialSettings.SetEnabled(chapterId, enabled))
            return;

        ActionLog.Ok(enabled
            ? $"Tutorial chapter '{title}' is on."
            : $"Tutorial chapter '{title}' is off - the line will skip it.");
    }

    private void StartOrRestart(Action onChanged)
    {
        ActionLog.Ok(TutorialDirector.IsStarted ? TutorialDirector.Restart() : TutorialDirector.Start());
        onChanged();
    }

    private void Reset(Action onChanged)
    {
        ActionLog.Ok(TutorialDirector.Reset());
        onChanged();
    }

    private void SetAll(bool enabled, Action onChanged)
    {
        var moved = TutorialSettings.SetAll(enabled);

        ActionLog.Ok(moved == 0
            ? $"Every chapter was already {(enabled ? "on" : "off")}."
            : $"Switched {moved} chapter(s) {(enabled ? "on" : "off")}.");

        onChanged();
    }
}
