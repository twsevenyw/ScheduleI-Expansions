using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Expansions.Core.UI.Native;

/// <summary>
/// The uGUI/TMP construction primitives, straight from <c>research-ext/UI-STYLE.md</c> §5.
/// <para>
/// No injected <c>MonoBehaviour</c>s anywhere: <c>ClassInjector.RegisterTypeInIl2Cpp</c> is
/// process-global and irreversible, which would break the reversible-module guarantee. Everything is
/// driven from the frame pump plus <c>Button.onClick</c>.
/// </para>
/// </summary>
internal static class UiFactory
{
    public static GameObject New(string name, Transform? parent)
    {
        var go = new GameObject(name) { layer = MenuStyle.UiLayer };
        var rect = go.AddComponent<RectTransform>();
        // Always false for UI, or the child inherits world scale.
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return go;
    }

    public static RectTransform Rect(GameObject go) => go.GetComponent<RectTransform>();

    public static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    /// <summary>Full width, pinned <paramref name="fromTop"/> below the parent's top edge.</summary>
    public static void AnchorTop(RectTransform rect, float padX, float fromTop, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(padX, -(fromTop + height));
        rect.offsetMax = new Vector2(-padX, -fromTop);
    }

    /// <summary>Full width, pinned <paramref name="fromBottom"/> above the parent's bottom edge.</summary>
    public static void AnchorBottom(RectTransform rect, float padX, float fromBottom, float height)
    {
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.offsetMin = new Vector2(padX, fromBottom);
        rect.offsetMax = new Vector2(-padX, fromBottom + height);
    }

    /// <summary>Fills the parent horizontally, inset from the top and bottom edges.</summary>
    public static void AnchorMiddle(RectTransform rect, float padX, float fromTop, float fromBottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(padX, fromBottom);
        rect.offsetMax = new Vector2(-padX, -fromTop);
    }

    /// <summary>Fixed-size box, positioned from the parent's bottom edge at a horizontal centre offset.</summary>
    public static void AnchorBottomCentre(RectTransform rect, float centreOffsetX, float fromBottom, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(centreOffsetX, fromBottom);
    }

    /// <summary>As <see cref="AnchorTop"/>, with the left and right insets set independently.</summary>
    public static void AnchorTopBox(RectTransform rect, float padLeft, float padRight, float fromTop, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(padLeft, -(fromTop + height));
        rect.offsetMax = new Vector2(-padRight, -fromTop);
    }

    /// <summary>Fixed-size box pinned to the parent's right edge, vertically centred.</summary>
    public static void AnchorRightMiddle(RectTransform rect, float fromRight, Vector2 size)
    {
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(-fromRight, 0f);
    }

    /// <summary>9-sliced rounded rect at an exact canvas-pixel corner radius.</summary>
    public static Image Rounded(GameObject go, Color color, float radiusPx)
    {
        var sprite = GameSprites.RoundedRect();

        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.fillCenter = true;
        image.pixelsPerUnitMultiplier = GameSprites.MultiplierFor(sprite, radiusPx);
        image.color = color;
        return image;
    }

    public static TextMeshProUGUI Text(
        GameObject go,
        string fontName,
        float size,
        Color color,
        TextAlignmentOptions alignment,
        bool wrap)
    {
        var text = go.AddComponent<TextMeshProUGUI>();
        text.font = GameFonts.Get(fontName);
        GameFonts.ApplyMaterial(text);
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.enableWordWrapping = wrap;
        text.richText = true;
        // Labels must never eat clicks meant for the card behind them.
        text.raycastTarget = false;
        // The game never uses synthetic bold/italic (1034 of 1037 components) or non-zero
        // character/word/line spacing; Open Sans' own 1.3618em line height is already correct.
        text.fontStyle = FontStyles.Normal;
        text.characterSpacing = 0f;
        text.wordSpacing = 0f;
        text.lineSpacing = 0f;
        text.margin = Vector4.zero;

        if (!wrap)
            text.overflowMode = TextOverflowModes.Overflow;

        return text;
    }

    /// <summary>Excludes a child from its parent's layout group so anchors decide where it sits.</summary>
    public static void IgnoreLayout(GameObject go) => go.AddComponent<LayoutElement>().ignoreLayout = true;

    /// <summary>
    /// Wires a <see cref="Button"/> to tint <paramref name="overlay"/> with the game's own
    /// black-darkening <c>ColorBlock</c>.
    /// <para>
    /// <paramref name="overlay"/> must already be opaque white: <c>ColorTint</c> multiplies
    /// <c>Image.color</c> by the tint rather than replacing it, so a transparent overlay multiplies to
    /// nothing in every state. Assigning <c>colors</c> also starts a fade from Unity's default block
    /// (opaque white), which flashes the whole control white for one fade unless it is snapped.
    /// </para>
    /// </summary>
    public static void ApplyTint(Button button, Image overlay)
    {
        button.targetGraphic = overlay;
        button.transition = Selectable.Transition.ColorTint;

        var colors = button.colors;
        colors.normalColor = MenuStyle.TintNormal;
        colors.highlightedColor = MenuStyle.TintHover;
        colors.pressedColor = MenuStyle.TintPressed;
        colors.selectedColor = MenuStyle.TintHover;
        colors.disabledColor = MenuStyle.TintDisabled;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = MenuStyle.FadeDuration;
        button.colors = colors;

        overlay.canvasRenderer.SetColor(colors.normalColor);

        // The game drives gamepad/keyboard focus through its own UISelectable/UIPanel stack, which a
        // standalone canvas is not part of. Unity's automatic navigation would fight it.
        var navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
    }

    /// <summary>Adds a click handler and roots it in <paramref name="keepAlive"/>. See <see cref="UiCallback"/>.</summary>
    public static void OnClick(Button button, Action handler, List<UiCallback> keepAlive)
    {
        var callback = new UiCallback(handler);
        keepAlive.Add(callback);
        button.onClick.AddListener(callback.Listener);
    }

    /// <summary>
    /// A small rounded text button in the game's idle-card grey, sharing the card's hover/press
    /// feedback and click sound.
    /// </summary>
    public static Button SmallButton(GameObject go, string label, Action onClick, List<UiCallback> keepAlive) =>
        SmallButton(go, label, onClick, keepAlive, out _);

    /// <summary>
    /// As <see cref="SmallButton(GameObject,string,Action,List{UiCallback})"/>, handing back the label
    /// so a caller whose text changes with state does not have to search the hierarchy for it.
    /// </summary>
    public static Button SmallButton(
        GameObject go,
        string label,
        Action onClick,
        List<UiCallback> keepAlive,
        out TextMeshProUGUI labelText) =>
        PlateButton(
            go,
            label,
            MenuStyle.ButtonLabelSize,
            TextAlignmentOptions.Center,
            onClick,
            keepAlive,
            out labelText,
            out _,
            out _);

    /// <summary>
    /// The one button implementation: a rounded plate, an opaque-white hover overlay under the label,
    /// and the game's click sound. Hands back the plate and the overlay so a caller that recolours with
    /// state (a selected tab, a greyed-out row) does not have to search the hierarchy for them.
    /// </summary>
    public static Button PlateButton(
        GameObject go,
        string label,
        float labelSize,
        TextAlignmentOptions alignment,
        Action onClick,
        List<UiCallback> keepAlive,
        out TextMeshProUGUI labelText,
        out Image plate,
        out Image hover)
    {
        plate = Rounded(go, MenuStyle.CardIdle, MenuStyle.CardFillRadius);
        plate.raycastTarget = true;

        var overlay = New("Hover", go.transform);
        hover = Rounded(overlay, Color.white, MenuStyle.CardFillRadius);
        hover.raycastTarget = false;
        Stretch(Rect(overlay));

        var text = New("Label", go.transform);
        Stretch(Rect(text));
        labelText = Text(text, GameFonts.Title, labelSize, MenuStyle.Text, alignment, false);
        labelText.text = label;

        var button = go.AddComponent<Button>();
        ApplyTint(button, hover);

        OnClick(
            button,
            () =>
            {
                GameSounds.PlayClick();
                onClick();
            },
            keepAlive);

        return button;
    }

    /// <summary>Hover detection without an injected event handler: <c>ColorTint</c> is already driving
    /// the overlay's <c>CanvasRenderer</c> colour, so a non-zero alpha means the pointer is over it.</summary>
    public static bool IsHovered(Image? overlay)
    {
        if (!InteropObjects.Alive(overlay))
            return false;

        try
        {
            return overlay!.canvasRenderer.GetColor().a > 0.02f;
        }
        catch
        {
            return false;
        }
    }
}
