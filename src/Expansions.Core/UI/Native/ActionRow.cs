using Expansions.Core.Actions;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Expansions.Core.UI.Native;

/// <summary>
/// One action: label over a description on the left, the button on the right.
/// <para>
/// Only the button is clickable. A row-wide hit target would be friendlier, but these actions teleport
/// the player and rewrite save settings, so the control the owner aims at is the one that fires.
/// </para>
/// <para>
/// When the availability predicate says no, the button is disabled and the description is replaced by
/// the reason — which is the only actionable information at that point.
/// </para>
/// </summary>
internal sealed class ActionRow
{
    private readonly List<UiCallback> _callbacks = new();

    private readonly Image _plate;
    private readonly Image _hover;
    private readonly Button _button;
    private readonly TextMeshProUGUI _label;
    private readonly TextMeshProUGUI _detail;
    private readonly TextMeshProUGUI _buttonLabel;

    private bool _wasHovered;
    private bool _interactable = true;

    // Last availability rendered. Refresh runs every frame, and recomposing the detail string only to
    // throw it away is the kind of per-frame allocation that adds up over a session.
    private bool _lastAvailable = true;
    private string _lastReason = string.Empty;
    private bool _rendered;

    private ActionRow(
        ExpansionAction action,
        GameObject root,
        Image plate,
        Image hover,
        Button button,
        TextMeshProUGUI label,
        TextMeshProUGUI detail,
        TextMeshProUGUI buttonLabel)
    {
        Action = action;
        Root = root;
        _plate = plate;
        _hover = hover;
        _button = button;
        _label = label;
        _detail = detail;
        _buttonLabel = buttonLabel;
    }

    public ExpansionAction Action { get; }

    public GameObject Root { get; }

    public static ActionRow Build(Transform parent, ExpansionAction action, Action<ExpansionAction> onRun)
    {
        var root = UiFactory.New($"Action_{action.Id}", parent);

        var plate = UiFactory.Rounded(root, MenuStyle.CardIdle, MenuStyle.CardFillRadius);
        plate.raycastTarget = false;

        root.AddComponent<LayoutElement>().preferredHeight = MenuStyle.ActionRowHeight;

        var textRight = MenuStyle.ActionButtonWidth + (2f * MenuStyle.ActionRowPadX);

        var labelGo = UiFactory.New("Label", root.transform);
        UiFactory.AnchorTopBox(UiFactory.Rect(labelGo), MenuStyle.ActionRowPadX, textRight, 7f, 20f);
        var label = UiFactory.Text(
            labelGo,
            GameFonts.Title,
            MenuStyle.ActionLabelSize,
            MenuStyle.Text,
            TextAlignmentOptions.MidlineLeft,
            false);
        label.text = action.CurrentLabel();

        var detailGo = UiFactory.New("Detail", root.transform);
        UiFactory.AnchorTopBox(UiFactory.Rect(detailGo), MenuStyle.ActionRowPadX, textRight, 26f, 30f);
        var detail = UiFactory.Text(
            detailGo,
            GameFonts.Description,
            MenuStyle.ActionDescSize,
            MenuStyle.TextDim,
            TextAlignmentOptions.TopLeft,
            true);
        detail.overflowMode = TextOverflowModes.Ellipsis;
        detail.text = action.Description;

        var buttonGo = UiFactory.New("Run", root.transform);
        UiFactory.AnchorRightMiddle(
            UiFactory.Rect(buttonGo),
            MenuStyle.ActionRowPadX,
            new Vector2(MenuStyle.ActionButtonWidth, MenuStyle.ActionButtonHeight));

        var callbacks = new List<UiCallback>();
        var button = UiFactory.PlateButton(
            buttonGo,
            action.HasPicker ? "Choose..." : "Run",
            MenuStyle.ButtonLabelSize,
            TextAlignmentOptions.Center,
            () => onRun(action),
            callbacks,
            out var buttonLabel,
            out _,
            out var hover);

        var row = new ActionRow(action, root, plate, hover, button, label, detail, buttonLabel);
        row._callbacks.AddRange(callbacks);
        row.Refresh();
        return row;
    }

    /// <summary>
    /// Re-reads the label, the availability and the reason. Cheap enough for every frame the screen is
    /// up, which is what lets "Enable and open the console" grey itself out the moment a save unloads.
    /// </summary>
    public void Refresh()
    {
        var label = Action.CurrentLabel();
        if (!string.Equals(_label.text, label, StringComparison.Ordinal))
            _label.text = label;

        var availability = Action.GetAvailability();

        if (_rendered &&
            _lastAvailable == availability.IsAvailable &&
            string.Equals(_lastReason, availability.Reason, StringComparison.Ordinal))
        {
            return;
        }

        _rendered = true;
        _lastAvailable = availability.IsAvailable;
        _lastReason = availability.Reason;

        _detail.text = Compose(Action.Description, availability.Reason, !availability.IsAvailable);
        SetInteractable(availability.IsAvailable);
    }

    /// <summary>
    /// True only on the frame the pointer arrives. A disabled button's <c>ColorTint</c> holds the
    /// overlay at a non-zero alpha permanently, so it would otherwise read as hovered forever.
    /// </summary>
    public bool PollHover()
    {
        if (!_interactable)
        {
            _wasHovered = false;
            return false;
        }

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

    private void SetInteractable(bool interactable)
    {
        if (_interactable == interactable)
            return;

        _interactable = interactable;
        _button.interactable = interactable;
        _plate.color = interactable ? MenuStyle.CardIdle : MenuStyle.RowDisabled;
        _detail.color = interactable ? MenuStyle.TextDim : MenuStyle.TextBad;
        _buttonLabel.color = interactable ? MenuStyle.Text : MenuStyle.TextDim;
    }

    private static string Compose(string description, string note, bool unavailable)
    {
        if (note.Length == 0)
            return description;

        var prefix = unavailable ? "Unavailable: " : "Note: ";
        return description.Length == 0 ? prefix + note : $"{description}\n{prefix}{note}";
    }
}
