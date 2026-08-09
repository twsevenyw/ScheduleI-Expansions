using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Expansions.Core.UI.Native;

/// <summary>
/// One module's toggle card: bold title over an italic description, dark grey when off, the game's
/// green with a white border and a gold trophy when on.
/// <para>
/// Anatomy is <c>research-ext/UI-STYLE.md</c> §3 and §5.4. The border and fill are two stacked
/// 9-sliced graphics rather than an outer container, because the measured selected card is the same
/// box as an unselected one with the same content — the border overlays the rect instead of growing
/// it, so switching state changes two colours and never the layout.
/// </para>
/// </summary>
internal sealed class ModuleCard
{
    private readonly List<UiCallback> _callbacks = new();
    private readonly Image _border;
    private readonly Image _fill;
    private readonly Image? _trophy;

    private ModuleCard(string moduleId, GameObject root, Image border, Image fill, Image hover, Image? trophy)
    {
        ModuleId = moduleId;
        Root = root;
        Hover = new HoverWatcher(hover);
        _border = border;
        _fill = fill;
        _trophy = trophy;
    }

    public string ModuleId { get; }

    public GameObject Root { get; }

    public HoverWatcher Hover { get; }

    public bool IsOn { get; private set; }

    public static ModuleCard Build(Transform parent, string moduleId, string title, string description, bool startOn)
    {
        var root = UiFactory.New($"Card_{moduleId}", parent);

        var border = UiFactory.Rounded(root, MenuStyle.CardIdle, MenuStyle.CardRadius);
        border.raycastTarget = true;

        var layout = root.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(MenuStyle.CardPadX, MenuStyle.CardPadX, MenuStyle.CardPadY, MenuStyle.CardPadY);
        layout.spacing = MenuStyle.CardSpacing;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = root.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        root.AddComponent<LayoutElement>().minHeight = MenuStyle.CardMinHeight;

        var fillGo = UiFactory.New("Fill", root.transform);
        var fill = UiFactory.Rounded(fillGo, MenuStyle.CardIdle, MenuStyle.CardFillRadius);
        fill.raycastTarget = false;
        UiFactory.Stretch(UiFactory.Rect(fillGo), MenuStyle.CardBorder);
        UiFactory.IgnoreLayout(fillGo);

        // Sits under the labels deliberately: the game darkens the plate on hover, not the text.
        var hoverGo = UiFactory.New("Hover", root.transform);
        var hover = UiFactory.Rounded(hoverGo, Color.white, MenuStyle.CardRadius);
        hover.raycastTarget = false;
        UiFactory.Stretch(UiFactory.Rect(hoverGo));
        UiFactory.IgnoreLayout(hoverGo);

        var trophy = BuildTrophy(root.transform);

        var titleGo = UiFactory.New("Title", root.transform);
        UiFactory.Text(titleGo, GameFonts.Title, MenuStyle.TitleSize, MenuStyle.Text, TextAlignmentOptions.Center, false)
            .text = title;
        titleGo.AddComponent<LayoutElement>().preferredHeight = MenuStyle.TitleLineHeight;

        var descriptionGo = UiFactory.New("Description", root.transform);
        UiFactory.Text(
            descriptionGo,
            GameFonts.Description,
            MenuStyle.DescSize,
            MenuStyle.Text,
            TextAlignmentOptions.Center,
            true).text = description;

        var card = new ModuleCard(moduleId, root, border, fill, hover, trophy);

        var button = root.AddComponent<Button>();
        UiFactory.ApplyTint(button, hover);
        UiFactory.OnClick(button, card.Clicked, card._callbacks);

        card.SetOn(startOn);
        return card;
    }

    /// <summary>Reads the registry back rather than assuming the click took: a module whose
    /// <c>OnEnabled</c> throws is rolled back to disabled and the card has to show that.</summary>
    public void SyncFromRegistry() => SetOn(ExpansionRegistry.IsEnabled(ModuleId));

    public void SetOn(bool on)
    {
        IsOn = on;

        // Both layers stay enabled in either state so the box, and therefore the layout result, is
        // identical: when off the fill matches the border colour and the 2px ring is simply invisible.
        _border.color = on ? MenuStyle.BorderOn : MenuStyle.CardIdle;
        _fill.color = on ? MenuStyle.CardOn : MenuStyle.CardIdle;

        if (_trophy != null)
            _trophy.gameObject.SetActive(on);
    }

    public void Destroy()
    {
        if (InteropObjects.Alive(Root))
        {
            // Object.Destroy is deferred to the end of the frame, so the card would still be a child of
            // the list when the layout is rebuilt and the panel would be sized for the old cards plus
            // the new ones. Detaching first takes it out of the layout immediately.
            Root.transform.SetParent(null, false);
            UnityEngine.Object.Destroy(Root);
        }

        _callbacks.Clear();
    }

    private static Image? BuildTrophy(Transform parent)
    {
        var sprite = GameSprites.TrophyIcon();
        if (sprite == null)
            return null;

        var go = UiFactory.New("Trophy", parent);
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        // trophy.png is a white silhouette with black interior shading and a soft black halo, so a
        // gold tint multiplies the white to gold and leaves the shading dark — the reference look.
        image.color = MenuStyle.TrophyGold;
        image.preserveAspect = true;
        image.raycastTarget = false;

        var rect = UiFactory.Rect(go);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(MenuStyle.TrophyBox, MenuStyle.TrophyBox);
        rect.anchoredPosition = MenuStyle.TrophyOffset;
        UiFactory.IgnoreLayout(go);

        return image;
    }

    private void Clicked()
    {
        GameSounds.PlayClick();
        ExpansionRegistry.SetEnabled(ModuleId, !IsOn);
        SyncFromRegistry();
    }
}
