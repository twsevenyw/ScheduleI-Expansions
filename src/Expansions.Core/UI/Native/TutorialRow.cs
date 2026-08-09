using Expansions.Core.Tutorial;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Expansions.Core.UI.Native;

/// <summary>
/// One tutorial chapter: title over its pitch on the left, its state and an on/off switch on the
/// right. Same anatomy as an action row, so the two tabs read as one screen.
/// </summary>
internal sealed class TutorialRow
{
    private readonly List<UiCallback> _callbacks = new();

    private readonly Image _plate;
    private readonly Image _hover;
    private readonly TextMeshProUGUI _title;
    private readonly TextMeshProUGUI _detail;
    private readonly TextMeshProUGUI _state;
    private readonly TextMeshProUGUI _toggleLabel;

    private bool _wasHovered;
    private string _renderedState = string.Empty;
    private bool _renderedEnabled = true;

    private TutorialRow(
        string chapterId,
        GameObject root,
        Image plate,
        Image hover,
        TextMeshProUGUI title,
        TextMeshProUGUI detail,
        TextMeshProUGUI state,
        TextMeshProUGUI toggleLabel)
    {
        ChapterId = chapterId;
        Root = root;
        _plate = plate;
        _hover = hover;
        _title = title;
        _detail = detail;
        _state = state;
        _toggleLabel = toggleLabel;
    }

    public string ChapterId { get; }

    public GameObject Root { get; }

    public static TutorialRow Build(Transform parent, ITutorialChapter chapter, Action<string> onToggle)
    {
        var id = chapter.Id;
        var root = UiFactory.New($"Chapter_{id}", parent);

        var plate = UiFactory.Rounded(root, MenuStyle.CardIdle, MenuStyle.CardFillRadius);
        plate.raycastTarget = false;

        root.AddComponent<LayoutElement>().preferredHeight = MenuStyle.TutorialRowHeight;

        var textRight = MenuStyle.TutorialToggleWidth + (2f * MenuStyle.TutorialRowPadX);

        var titleGo = UiFactory.New("Title", root.transform);
        UiFactory.AnchorTopBox(UiFactory.Rect(titleGo), MenuStyle.TutorialRowPadX, textRight, 7f, 20f);
        var title = UiFactory.Text(
            titleGo,
            GameFonts.Title,
            MenuStyle.TutorialTitleSize,
            MenuStyle.Text,
            TextAlignmentOptions.MidlineLeft,
            false);
        title.text = chapter.Title;
        title.overflowMode = TextOverflowModes.Ellipsis;

        var detailGo = UiFactory.New("Detail", root.transform);
        UiFactory.AnchorTopBox(UiFactory.Rect(detailGo), MenuStyle.TutorialRowPadX, textRight, 26f, 22f);
        var detail = UiFactory.Text(
            detailGo,
            GameFonts.Description,
            MenuStyle.TutorialDescSize,
            MenuStyle.TextDim,
            TextAlignmentOptions.TopLeft,
            true);
        detail.overflowMode = TextOverflowModes.Ellipsis;
        detail.text = chapter.Description;

        var stateGo = UiFactory.New("State", root.transform);
        UiFactory.AnchorTopBox(UiFactory.Rect(stateGo), MenuStyle.TutorialRowPadX, textRight, 47f, 15f);
        var state = UiFactory.Text(
            stateGo,
            GameFonts.Title,
            MenuStyle.TutorialStateSize,
            MenuStyle.TextDim,
            TextAlignmentOptions.MidlineLeft,
            false);
        state.overflowMode = TextOverflowModes.Ellipsis;

        var toggleGo = UiFactory.New("Toggle", root.transform);
        UiFactory.AnchorRightMiddle(
            UiFactory.Rect(toggleGo),
            MenuStyle.TutorialRowPadX,
            new Vector2(MenuStyle.TutorialToggleWidth, MenuStyle.TutorialToggleHeight));

        var callbacks = new List<UiCallback>();
        UiFactory.PlateButton(
            toggleGo,
            "On",
            MenuStyle.ButtonLabelSize,
            TextAlignmentOptions.Center,
            () => onToggle(id),
            callbacks,
            out var toggleLabel,
            out _,
            out var hover);

        var row = new TutorialRow(id, root, plate, hover, title, detail, state, toggleLabel);
        row._callbacks.AddRange(callbacks);
        row.Refresh(chapter);
        return row;
    }

    /// <summary>Re-reads the chapter's state. Cheap enough for every frame the tab is showing.</summary>
    public void Refresh(ITutorialChapter chapter)
    {
        var enabled = TutorialSettings.IsEnabled(ChapterId);
        var state = TutorialDirector.StateOf(ChapterId);
        var line = Describe(state);

        if (_renderedEnabled == enabled && string.Equals(_renderedState, line, StringComparison.Ordinal))
            return;

        _renderedEnabled = enabled;
        _renderedState = line;

        if (!string.Equals(_title.text, chapter.Title, StringComparison.Ordinal))
            _title.text = chapter.Title;

        _state.text = line;
        _state.color = ColourFor(state);
        _toggleLabel.text = enabled ? "On" : "Off";
        _toggleLabel.color = enabled ? MenuStyle.Text : MenuStyle.TextDim;
        _plate.color = enabled ? MenuStyle.CardIdle : MenuStyle.RowDisabled;
        _title.color = enabled ? MenuStyle.Text : MenuStyle.TextDim;
        _detail.color = enabled ? MenuStyle.TextDim : MenuStyle.RowMuted;
    }

    /// <summary>True only on the frame the pointer arrives, so the sound fires once per entry.</summary>
    public bool PollHover()
    {
        var hovered = UiFactory.IsHovered(_hover);
        var entered = hovered && !_wasHovered;
        _wasHovered = hovered;
        return entered;
    }

    public void Destroy()
    {
        if (InteropObjects.Alive(Root))
        {
            // Object.Destroy is deferred to the end of the frame, so the row would still be a layout
            // child when the list is remeasured. Detaching takes it out immediately.
            Root.transform.SetParent(null, false);
            UnityEngine.Object.Destroy(Root);
        }

        _callbacks.Clear();
    }

    private static Color ColourFor(TutorialChapterState state) => state switch
    {
        TutorialChapterState.Complete => MenuStyle.TextGood,
        TutorialChapterState.Playing => MenuStyle.Text,
        TutorialChapterState.Unavailable => MenuStyle.TextWarn,
        _ => MenuStyle.TextDim,
    };

    private string Describe(TutorialChapterState state)
    {
        switch (state)
        {
            case TutorialChapterState.Skipped:
                return "Skipped - switched off";

            case TutorialChapterState.Complete:
                return "Completed on this save";

            case TutorialChapterState.Playing:
                var objective = TutorialDirector.CurrentObjectiveTitle(ChapterId);
                return objective.Length > 0 ? $"In progress - {objective}" : "In progress";

            // The one state with something to say. A blocked chapter is stepped over rather than faked, so
            // this sentence is all the player gets to explain why the line moved past it — and it is the
            // chapter's own words, not ours.
            case TutorialChapterState.Unavailable:
                var reason = TutorialDirector.ReasonFor(ChapterId);
                return reason.Length > 0 ? $"Blocked - {reason}" : "Blocked - not ready yet";

            default:
                return TutorialDirector.IsStarted ? "Waiting its turn" : "Not started";
        }
    }
}
