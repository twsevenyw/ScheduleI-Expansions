using UnityEngine;
using UnityEngine.UI;

namespace Expansions.Core.UI.Native;

/// <summary>
/// A scrolling list: masked viewport, growing content, visible gutter.
/// <para>
/// The original build put the <see cref="ScrollRect"/>, the <see cref="RectMask2D"/> and the viewport
/// on one <c>GameObject</c> with <em>no</em> <c>Graphic</c> on it. Unity's input modules dispatch
/// <c>IScrollHandler</c> by walking up from whatever the pointer raycast hit, and a
/// <c>Graphic</c>-less object is not a raycast target — so over the list the raycast fell through to
/// the panel background, which is a sibling <em>outside</em> the scroll hierarchy, and
/// <c>ScrollRect.OnScroll</c> was never called at all. The transparent <c>raycastTarget</c> image on
/// the viewport below is what Unity's own Scroll View prefab has, and is the fix.
/// </para>
/// <para>
/// The wheel is nonetheless applied by hand from <see cref="Input.mouseScrollDelta"/> against the
/// viewport rect, with <c>ScrollRect.scrollSensitivity</c> pinned to zero so the component's own
/// handler is a no-op. That is one implementation whether or not the game's event system routes
/// scroll events to a mod canvas, and it cannot double-apply. Dragging and the scrollbar handle stay
/// on the component, which the working hover tints prove the event system already drives.
/// </para>
/// </summary>
internal sealed class ScrollView
{
    private readonly RectTransform _root;
    private readonly RectTransform _viewport;
    private readonly RectTransform _content;
    private readonly ScrollRect _scroll;

    private ScrollView(RectTransform root, RectTransform viewport, RectTransform content, ScrollRect scroll)
    {
        _root = root;
        _viewport = viewport;
        _content = content;
        _scroll = scroll;
    }

    /// <summary>The box the page positions. Everything else lives inside it.</summary>
    public RectTransform Root => _root;

    /// <summary>Parent for list rows, or for the single label in the case of the output pane.</summary>
    public RectTransform Content => _content;

    /// <summary>Height the content wants, which is what a page sizes itself against.</summary>
    public float ContentHeight => InteropObjects.Alive(_content) ? _content.rect.height : 0f;

    /// <summary>How far the content can travel. Zero when it already fits.</summary>
    public float Hidden => Mathf.Max(0f, ContentHeight - ViewportHeight);

    private float ViewportHeight => InteropObjects.Alive(_viewport) ? _viewport.rect.height : 0f;

    /// <summary>Distance the content has travelled from the top, in canvas pixels.</summary>
    private float Offset => InteropObjects.Alive(_content) ? _content.anchoredPosition.y : 0f;

    private bool Usable =>
        InteropObjects.Alive(_root) && InteropObjects.Alive(_content) && _root.gameObject.activeInHierarchy;

    /// <summary>
    /// Builds the container into <paramref name="hostGo"/>, which the caller has already anchored.
    /// <paramref name="layoutContent"/> false leaves the content rect without a layout group, for a
    /// caller that puts one self-sizing graphic in it rather than a list of rows.
    /// </summary>
    public static ScrollView Build(GameObject hostGo, float padX, float spacing, bool layoutContent = true)
    {
        var root = UiFactory.Rect(hostGo);

        var viewportGo = UiFactory.New("Viewport", hostGo.transform);
        var viewport = UiFactory.Rect(viewportGo);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.pivot = new Vector2(0.5f, 0.5f);
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = new Vector2(-MenuStyle.ScrollGutter, 0f);

        // Alpha 1/255 rather than 0: a graphic the canvas treats as fully transparent can be culled
        // out of the batch, and a culled graphic reports depth -1, which the raycaster skips. Over the
        // #010101 panel backdrop this is not a visible colour.
        var catcher = viewportGo.AddComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, 1f / 255f);
        catcher.raycastTarget = true;

        // Clips without a mask sprite or a stencil pass.
        viewportGo.AddComponent<RectMask2D>();

        var contentGo = UiFactory.New("List", viewportGo.transform);
        var content = UiFactory.Rect(contentGo);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;

        if (layoutContent)
        {
            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset((int)padX, (int)padX, 0, 0);
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var bar = BuildScrollbar(hostGo.transform);

        var scroll = hostGo.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        // Zero, deliberately — see the class remarks.
        scroll.scrollSensitivity = 0f;
        scroll.verticalScrollbar = bar;
        // AutoHide, not AutoHideAndExpandViewport: the latter drives the viewport's own rect, which
        // would fight the fixed gutter reserved above.
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        return new ScrollView(root, viewport, content, scroll);
    }

    /// <summary>Re-measures the content. Call after adding, removing or resizing rows.</summary>
    public void Rebuild()
    {
        if (!InteropObjects.Alive(_content))
            return;

        LayoutRebuilder.ForceRebuildLayoutImmediate(_content);

        // Re-clamps: a list that just got shorter can leave the content parked past its new end.
        SetOffset(Offset);
    }

    public void ScrollToTop() => SetOffset(0f);

    public void ScrollToBottom() => SetOffset(Hidden);

    /// <summary>
    /// Brings the slice starting <paramref name="top"/> pixels down the content into view, scrolling
    /// the shortest distance that does it. Used by keyboard selection, where the row the owner just
    /// moved to has to be on screen without the list jumping around.
    /// </summary>
    public void EnsureVisible(float top, float height)
    {
        var view = ViewportHeight;
        if (view <= 0f)
            return;

        var offset = Offset;

        if (top < offset)
            SetOffset(top);
        else if (top + height > offset + view)
            SetOffset(top + height - view);
    }

    /// <summary>
    /// Applies a frame's scroll input to this list. The wheel needs the pointer over the viewport; the
    /// keys act on whichever list the screen is showing, because the pointer is rarely near it when a
    /// key is pressed. <paramref name="allowKeys"/> is false while something else owns the keyboard,
    /// such as the picker's search filter.
    /// </summary>
    public void Tick(bool allowKeys)
    {
        if (!Usable)
            return;

        // Re-clamps first: the page resizes the viewport after measuring the content, which can leave
        // the list parked past its new end for a frame.
        SetOffset(Offset);

        if (allowKeys)
        {
            if (Input.GetKeyDown(KeyCode.Home))
            {
                ScrollToTop();
                return;
            }

            if (Input.GetKeyDown(KeyCode.End))
            {
                ScrollToBottom();
                return;
            }
        }

        var step = 0f;

        if (PointerOverViewport())
        {
            var wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f)
                step -= wheel * MenuStyle.ScrollWheelStep;
        }

        if (allowKeys)
            step += KeyboardStep();

        if (Mathf.Approximately(step, 0f))
            return;

        // A drag can leave residual velocity that would otherwise fight the step for a few frames.
        _scroll.StopMovement();
        SetOffset(Offset + step);
    }

    private static Scrollbar BuildScrollbar(Transform parent)
    {
        var barGo = UiFactory.New("Scrollbar", parent);
        var barRect = UiFactory.Rect(barGo);
        barRect.anchorMin = new Vector2(1f, 0f);
        barRect.anchorMax = Vector2.one;
        barRect.pivot = new Vector2(1f, 0.5f);
        barRect.sizeDelta = new Vector2(MenuStyle.ScrollBarWidth, 0f);
        barRect.anchoredPosition = Vector2.zero;

        UiFactory.Rounded(barGo, MenuStyle.ScrollTrack, MenuStyle.ScrollBarRadius).raycastTarget = true;

        // The handle's parent is what Scrollbar measures travel against, so it needs one of its own.
        var areaGo = UiFactory.New("Sliding Area", barGo.transform);
        UiFactory.Stretch(UiFactory.Rect(areaGo));

        var handleGo = UiFactory.New("Handle", areaGo.transform);
        // White, because ColorTint multiplies: the ColorBlock below carries the real handle colour.
        var handle = UiFactory.Rounded(handleGo, Color.white, MenuStyle.ScrollBarRadius);
        handle.raycastTarget = true;

        var handleRect = UiFactory.Rect(handleGo);
        // Scrollbar drives the anchors; zero offsets make the handle exactly fill the slice it is given.
        handleRect.offsetMin = Vector2.zero;
        handleRect.offsetMax = Vector2.zero;

        var bar = barGo.AddComponent<Scrollbar>();
        // Assigning handleRect is also what makes Scrollbar cache its container rect: the component's
        // OnEnable already ran, at which point there was nothing to cache.
        bar.handleRect = handleRect;
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.size = 1f;
        bar.value = 1f;

        ApplyHandleTint(bar, handle);
        return bar;
    }

    /// <summary>
    /// The handle's own colour states. Unlike a card, the tint here <em>is</em> the colour rather than
    /// a darkening overlay, because there is nothing behind the handle to darken.
    /// </summary>
    private static void ApplyHandleTint(Scrollbar bar, Image handle)
    {
        bar.targetGraphic = handle;
        bar.transition = Selectable.Transition.ColorTint;

        var colors = bar.colors;
        colors.normalColor = MenuStyle.ScrollHandle;
        colors.highlightedColor = MenuStyle.ScrollHandleHover;
        colors.pressedColor = MenuStyle.ScrollHandleHover;
        colors.selectedColor = MenuStyle.ScrollHandle;
        colors.disabledColor = MenuStyle.ScrollTrack;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = MenuStyle.FadeDuration;
        bar.colors = colors;

        // Assigning colors starts a fade from Unity's default opaque-white block; snap past it.
        handle.canvasRenderer.SetColor(colors.normalColor);

        var navigation = bar.navigation;
        navigation.mode = Navigation.Mode.None;
        bar.navigation = navigation;
    }

    private float KeyboardStep()
    {
        var page = Mathf.Max(MenuStyle.ScrollKeyStep, ViewportHeight * MenuStyle.ScrollPageFraction);
        var step = 0f;

        if (Input.GetKeyDown(KeyCode.DownArrow))
            step += MenuStyle.ScrollKeyStep;

        if (Input.GetKeyDown(KeyCode.UpArrow))
            step -= MenuStyle.ScrollKeyStep;

        if (Input.GetKeyDown(KeyCode.PageDown))
            step += page;

        if (Input.GetKeyDown(KeyCode.PageUp))
            step -= page;

        return step;
    }

    private bool PointerOverViewport()
    {
        if (!InteropObjects.Alive(_viewport))
            return false;

        try
        {
            // Null camera: this is a ScreenSpaceOverlay canvas, so screen space is canvas space.
            return RectTransformUtility.RectangleContainsScreenPoint(_viewport, Input.mousePosition, null);
        }
        catch
        {
            return false;
        }
    }

    private void SetOffset(float offset)
    {
        if (!InteropObjects.Alive(_content))
            return;

        var position = _content.anchoredPosition;
        // Top-pivot content: 0 is the first row, Hidden is the last.
        var clamped = Mathf.Clamp(offset, 0f, Hidden);

        // Guarded so the per-frame re-clamp does not dirty the layout when nothing moved.
        if (Mathf.Approximately(position.y, clamped))
            return;

        position.y = clamped;
        _content.anchoredPosition = position;
    }
}
