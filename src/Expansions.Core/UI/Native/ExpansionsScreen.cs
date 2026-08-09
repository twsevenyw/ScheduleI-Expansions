using Expansions.Core.Actions;
using Expansions.Core.Logging;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Expansions.Core.UI.Native;

/// <summary>
/// The Expansions screen: a titled panel with two pages — the module toggles and the actions — over a
/// dark scrim.
/// <para>
/// It lives on its own <c>DontDestroyOnLoad</c> overlay canvas rather than inside a cloned
/// <c>MenuScreen</c>, which is what lets one screen serve both surfaces — the main-menu nav entry and
/// the in-game hotkey — with one set of pages and one code path. Cloning a whole <c>MenuScreen</c>
/// would also clone its <c>MonoState</c> and navigation components, and would only exist in the menu.
/// </para>
/// <para>
/// The Actions page is the wider of the two, so the panel resizes with the tab. That is deliberate: the
/// module cards are pixel-measured against the shipped UI and would stretch if the panel that holds
/// them grew to fit a probe report path.
/// </para>
/// </summary>
internal sealed class ExpansionsScreen
{
    private const string Heading = "EXPANSIONS";

    private readonly ModuleLogger _log;
    private readonly List<UiCallback> _callbacks = new();
    private readonly List<HoverWatcher> _footerHovers = new();

    private GameObject? _root;
    private Canvas? _canvas;
    private CanvasScaler? _scaler;
    private GraphicRaycaster? _raycaster;
    private CanvasGroup? _group;
    private RectTransform? _panel;
    private ModulesPage? _modules;
    private ActionsPage? _actions;
    private TutorialPage? _tutorial;
    private ChoicePicker? _picker;
    private TextMeshProUGUI? _hint;
    private Image? _actionsTabPlate;
    private Image? _tutorialTabPlate;
    private Image? _modulesTabPlate;

    private ScreenTab _tab = ScreenTab.Actions;
    private float _alpha;
    private float _targetAlpha;

    public ExpansionsScreen(ModuleLogger log) => _log = log;

    /// <summary>Which page is showing. Actions leads, because it is what the screen is for.</summary>
    private enum ScreenTab
    {
        Actions,
        Tutorial,
        Modules,
    }

    public bool IsBuilt => InteropObjects.Alive(_root);

    /// <summary>True while a picker overlay is up, which changes what Escape and typing mean.</summary>
    public bool IsPickerOpen => _picker is { IsOpen: true };

    /// <summary>Fires when the user closes the screen from the footer.</summary>
    public event Action? CloseRequested;

    /// <summary>Fires with the action the user clicked. Deferred by the caller; see <see cref="Tick"/>.</summary>
    public event Action<ExpansionAction>? ActionRequested;

    /// <summary>Fires with an action and the picker entry chosen for it.</summary>
    public event Action<ExpansionAction, ActionChoice>? ChoiceRequested;

    /// <summary>
    /// Builds the canvas if needed. Throws on genuine construction failure so the caller can degrade
    /// to the IMGUI menu instead of leaving the user with no way to do anything.
    /// </summary>
    public void EnsureBuilt()
    {
        if (IsBuilt)
            return;

        Discard();

        // Left visible in the hierarchy on purpose: this is UI that has to be eyeballed, and hiding it
        // would put it out of reach of the community object browsers used to check layout.
        _root = UiFactory.New("Expansions_Canvas", null);
        UnityEngine.Object.DontDestroyOnLoad(_root);

        _canvas = _root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = MenuStyle.SortingOrder;
        _canvas.overrideSorting = true;
        _canvas.referencePixelsPerUnit = MenuStyle.ReferencePixelsPerUnit;
        // TMP's SDF shader reads these extra channels; without them outlines and dilate render wrong.
        _canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 |
                                           AdditionalCanvasShaderChannels.Normal |
                                           AdditionalCanvasShaderChannels.Tangent;

        _scaler = _root.AddComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        _scaler.referenceResolution = MenuStyle.ReferenceResolution;
        _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        // Width-only, matching every scaler in the game that scales with screen size. Using 0.5 here
        // is the mistake that makes modded UI drift from the game's on non-16:9 displays.
        _scaler.matchWidthOrHeight = MenuStyle.MatchWidthOrHeight;
        _scaler.referencePixelsPerUnit = MenuStyle.ReferencePixelsPerUnit;

        _raycaster = _root.AddComponent<GraphicRaycaster>();
        _raycaster.ignoreReversedGraphics = true;
        _raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

        _group = _root.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;

        BuildScrim(_root.transform);
        BuildPanel(_root.transform);

        SetRenderingEnabled(false);
        _log.Debug(
            $"Native screen built. Fonts cached: {GameFonts.Count}. Sprites cached: {GameSprites.Count}. " +
            $"Menu sounds: {(GameSounds.HasClips ? "bound" : "unavailable")}.");
    }

    /// <summary>
    /// Rebuilds both pages from their registries, resizes the panel to fit and returns the active list
    /// to the top. Call after <see cref="SetVisible"/>, so the canvas is enabled while the layout pass
    /// runs — TMP reports no preferred height until its text has been laid out once.
    /// </summary>
    public void SyncFromRegistry(bool resetScroll = true)
    {
        if (_modules is null || _actions is null || _tutorial is null || _panel is null)
            return;

        CopyScalerFromGame();
        ApplyBudget();

        // Every page rebuilds its contents, not just the visible one, so a tab switch shows current
        // cards and rows without waiting a frame. Only the active page's measurement is trusted; see
        // ApplyTab.
        _modules.SetActive(true);
        _actions.SetActive(true);
        _tutorial.SetActive(true);

        _modules.Sync();
        _actions.Sync();
        _tutorial.Sync();

        ApplyTab(resetScroll);
    }

    public void SetVisible(bool visible)
    {
        _targetAlpha = visible ? 1f : 0f;

        if (visible)
            SetRenderingEnabled(true);

        if (_group != null)
            _group.blocksRaycasts = visible;

        if (!visible)
            _picker?.Close();
    }

    /// <summary>Advances the open/close fade, the per-row state and the hover sounds. Unscaled: the
    /// screen is usable while the game is paused or time-scaled.</summary>
    public void Tick(bool visible)
    {
        if (_group == null)
            return;

        if (!Mathf.Approximately(_alpha, _targetAlpha))
        {
            var step = Time.unscaledDeltaTime / MenuStyle.PanelFadeDuration;
            _alpha = Mathf.MoveTowards(_alpha, _targetAlpha, step);
            _group.alpha = _alpha;

            if (!visible && Mathf.Approximately(_alpha, 0f))
                SetRenderingEnabled(false);
        }

        if (!visible)
            return;

        if (_tab == ScreenTab.Actions)
        {
            _actions?.Tick();
            _picker?.Tick();
        }
        else if (_tab == ScreenTab.Tutorial)
        {
            _tutorial?.Tick();
        }

        _picker?.PollHover();

        if (!IsPickerOpen)
        {
            // Only the visible list takes scroll input, so the wheel and the page keys always mean the
            // list the owner is looking at.
            TickScroll();

            switch (_tab)
            {
                case ScreenTab.Modules:
                    _modules?.PollHover();
                    break;
                case ScreenTab.Tutorial:
                    _tutorial?.PollHover();
                    break;
                default:
                    _actions?.PollHover();
                    break;
            }
        }

        foreach (var watcher in _footerHovers)
        {
            if (watcher.Entered())
                GameSounds.PlayHover();
        }
    }

    /// <summary>Pumps the visible list's wheel and keyboard scrolling. See <see cref="ScrollView"/>.</summary>
    private void TickScroll()
    {
        switch (_tab)
        {
            case ScreenTab.Modules:
                _modules?.TickScroll(allowKeys: true);
                break;
            case ScreenTab.Tutorial:
                _tutorial?.TickScroll(allowKeys: true);
                break;
            default:
                _actions?.TickScroll(allowKeys: true);
                break;
        }
    }

    /// <summary>Feeds keystrokes to the picker's filter. True when the picker took them.</summary>
    public bool ConsumeTyping() => _picker?.ConsumeTyping() ?? false;

    /// <summary>Opens the picker for <paramref name="action"/>. The caller has checked it has one.</summary>
    public void OpenPicker(ExpansionAction action) => _picker?.Open(action);

    /// <summary>Closes the picker without closing the screen. True if there was one to close.</summary>
    public bool ClosePicker()
    {
        if (!IsPickerOpen)
            return false;

        _picker!.Close();
        return true;
    }

    public void SetHint(string text)
    {
        if (_hint != null)
            _hint.text = text;
    }

    /// <summary>Destroys the canvas and everything under it. Nothing survives a scene transition.</summary>
    public void Destroy()
    {
        _picker?.Destroy();
        _modules?.Destroy();
        _actions?.Destroy();
        _tutorial?.Destroy();

        if (InteropObjects.Alive(_root))
            UnityEngine.Object.Destroy(_root);

        // Cleared only after the GameObjects are gone: these root the interop delegates the native
        // buttons call into.
        _callbacks.Clear();
        _footerHovers.Clear();

        Discard();
    }

    /// <summary>
    /// Takes the scaler settings off one of the game's own, rather than trusting the measured
    /// constants forever. This is also how the in-game Interface Scale slider reaches us: it drives
    /// the game's <c>CanvasScaler</c> wrapper, which rewrites the reference resolution on the real
    /// scalers. Re-read on every open so a mid-session change is picked up.
    /// </summary>
    private void CopyScalerFromGame()
    {
        if (_scaler == null)
            return;

        try
        {
            var all = Resources.FindObjectsOfTypeAll<CanvasScaler>();
            for (var i = 0; i < all.Length; i++)
            {
                var candidate = all[i];
                if (candidate == null || ReferenceEquals(candidate, _scaler))
                    continue;

                if (candidate.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
                    continue;

                // Unity's unused 800x600 default shows up on scalers that are not in this mode, but
                // guard anyway: adopting it would halve every measurement.
                if (candidate.referenceResolution.x < 1280f)
                    continue;

                if (!candidate.gameObject.scene.IsValid())
                    continue;

                if (Mathf.Approximately(candidate.referenceResolution.x, _scaler.referenceResolution.x) &&
                    Mathf.Approximately(candidate.matchWidthOrHeight, _scaler.matchWidthOrHeight))
                {
                    return;
                }

                _scaler.referenceResolution = candidate.referenceResolution;
                _scaler.screenMatchMode = candidate.screenMatchMode;
                _scaler.matchWidthOrHeight = candidate.matchWidthOrHeight;
                _scaler.referencePixelsPerUnit = candidate.referencePixelsPerUnit;

                _log.Debug(
                    $"Adopted the game's canvas scaling: {candidate.referenceResolution.x}x" +
                    $"{candidate.referenceResolution.y}, match={candidate.matchWidthOrHeight}, " +
                    $"refPPU={candidate.referencePixelsPerUnit} (from '{candidate.gameObject.name}').");
                return;
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"Could not read the game's canvas scaling: {ex.GetType().Name}: {ex.Message}");
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
        _modules = null;
        _actions = null;
        _tutorial = null;
        _picker = null;
        _hint = null;
        _actionsTabPlate = null;
        _tutorialTabPlate = null;
        _modulesTabPlate = null;
        _alpha = 0f;
        _targetAlpha = 0f;
    }

    /// <summary>
    /// Disabling the <c>Canvas</c> stops it rendering while leaving the hierarchy active, so layout
    /// still computes and the panel never has to be measured from an inactive object.
    /// </summary>
    private void SetRenderingEnabled(bool enabled)
    {
        if (_canvas != null)
            _canvas.enabled = enabled;

        if (_raycaster != null)
            _raycaster.enabled = enabled;
    }

    private void BuildScrim(Transform parent)
    {
        var go = UiFactory.New("Scrim", parent);
        UiFactory.Stretch(UiFactory.Rect(go));

        var image = go.AddComponent<Image>();
        image.color = MenuStyle.Scrim;
        // Swallows clicks so they never reach the menu buttons or world UI underneath.
        image.raycastTarget = true;
    }

    private void BuildPanel(Transform parent)
    {
        var panelGo = UiFactory.New("Panel", parent);
        _panel = UiFactory.Rect(panelGo);
        _panel.anchorMin = new Vector2(0.5f, 0.5f);
        _panel.anchorMax = new Vector2(0.5f, 0.5f);
        _panel.pivot = new Vector2(0.5f, 0.5f);
        _panel.anchoredPosition = Vector2.zero;
        _panel.sizeDelta = new Vector2(
            MenuStyle.ActionsPanelWidth, MenuStyle.PanelHeight(MenuStyle.ActionRowHeight));

        BuildShadow(panelGo.transform);

        var backgroundGo = UiFactory.New("Background", panelGo.transform);
        UiFactory.Stretch(UiFactory.Rect(backgroundGo));
        UiFactory.Rounded(backgroundGo, MenuStyle.Backdrop, MenuStyle.PanelRadius).raycastTarget = true;

        var headingGo = UiFactory.New("Heading", panelGo.transform);
        UiFactory.AnchorTop(UiFactory.Rect(headingGo), MenuStyle.PanelPad, MenuStyle.PanelPad, MenuStyle.HeadingHeight);
        UiFactory.Text(
            headingGo,
            GameFonts.Title,
            MenuStyle.HeadingSize,
            MenuStyle.Text,
            TextAlignmentOptions.Center,
            false).text = Heading;

        BuildTabs(panelGo.transform);
        BuildPages(panelGo.transform);
        BuildFooter(panelGo.transform);
    }

    private void BuildShadow(Transform parent)
    {
        var sprite = GameSprites.ShadeRect();
        if (sprite == null)
            return;

        var go = UiFactory.New("Shadow", parent);
        var rect = UiFactory.Rect(go);
        UiFactory.Stretch(rect);
        rect.offsetMin = new Vector2(-MenuStyle.PanelPad, -MenuStyle.PanelPad);
        rect.offsetMax = new Vector2(MenuStyle.PanelPad, MenuStyle.PanelPad);

        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.fillCenter = false;
        image.pixelsPerUnitMultiplier = GameSprites.MultiplierFor(sprite, MenuStyle.PanelShadowSpread);
        image.color = MenuStyle.Shadow;
        image.raycastTarget = false;
    }

    /// <summary>
    /// Three tabs, centred, using the same plate/hover/click machinery as every other button here. The
    /// selected one takes the card's "on" green, so the state reads the same way a toggled module does.
    /// </summary>
    private void BuildTabs(Transform parent)
    {
        var fromTop = MenuStyle.PanelPad + MenuStyle.HeadingHeight + MenuStyle.HeadingGap;

        _actionsTabPlate = BuildTab(parent, "Actions", 0, fromTop, ScreenTab.Actions);
        _tutorialTabPlate = BuildTab(parent, "Tutorial", 1, fromTop, ScreenTab.Tutorial);
        _modulesTabPlate = BuildTab(parent, "Mods", 2, fromTop, ScreenTab.Modules);
    }

    private Image BuildTab(Transform parent, string label, int index, float fromTop, ScreenTab tab)
    {
        const int count = 3;
        var total = (count * MenuStyle.TabWidth) + ((count - 1) * MenuStyle.TabGap);
        var centre = (-total / 2f) + (MenuStyle.TabWidth / 2f) +
                     (index * (MenuStyle.TabWidth + MenuStyle.TabGap));

        var go = UiFactory.New($"Tab_{label}", parent);
        var rect = UiFactory.Rect(go);
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(MenuStyle.TabWidth, MenuStyle.TabHeight);
        rect.anchoredPosition = new Vector2(centre, -fromTop);

        Watch(UiFactory.PlateButton(
            go,
            label,
            MenuStyle.TabLabelSize,
            TextAlignmentOptions.Center,
            () => SetTab(tab),
            _callbacks,
            out _,
            out var plate,
            out _));

        return plate;
    }

    private void BuildPages(Transform parent)
    {
        var pagesGo = UiFactory.New("Pages", parent);
        UiFactory.AnchorMiddle(
            UiFactory.Rect(pagesGo),
            MenuStyle.PanelPad,
            MenuStyle.HeaderHeight,
            MenuStyle.FooterHeight + MenuStyle.FooterGap);

        _modules = ModulesPage.Build(pagesGo.transform);
        _actions = ActionsPage.Build(
            pagesGo.transform,
            action => ActionRequested?.Invoke(action),
            () => SyncFromRegistry(resetScroll: false));
        _tutorial = TutorialPage.Build(pagesGo.transform, () => SyncFromRegistry(resetScroll: false));

        // The picker overlays the page area rather than the whole panel, so the heading, the tabs and
        // Close stay visible and reachable while it is up.
        _picker = ChoicePicker.Build(
            pagesGo.transform,
            (action, choice) => ChoiceRequested?.Invoke(action, choice),
            () => _picker?.Close(),
            _callbacks);
    }

    /// <summary>
    /// Tells each page how much height it may claim, from the screen it is actually being shown on.
    /// The lists used to be capped at constants that were right on one display and left most of a
    /// 46-entry list unreachable on every other.
    /// </summary>
    private void ApplyBudget()
    {
        var budget = MenuStyle.PageBudget(LogicalScreenHeight());

        _actions?.SetBudget(budget);
        _tutorial?.SetBudget(budget);
        _modules?.SetBudget(budget);
    }

    /// <summary>Screen height in canvas units, which is what every measurement here is in.</summary>
    private float LogicalScreenHeight()
    {
        try
        {
            var scale = _canvas != null ? _canvas.scaleFactor : 0f;

            // The scaler has not run its first Handle() on the frame the canvas is built, so derive
            // the value it is about to settle on. Every scaler in the game matches on width alone.
            if (scale <= 0.01f && _scaler != null && _scaler.referenceResolution.x > 1f)
                scale = Screen.width / _scaler.referenceResolution.x;

            if (scale <= 0.01f || Screen.height <= 0)
                return MenuStyle.FallbackScreenHeight;

            return Screen.height / scale;
        }
        catch
        {
            return MenuStyle.FallbackScreenHeight;
        }
    }

    private void BuildFooter(Transform parent)
    {
        const float closeWidth = 100f;

        var rowBottom = MenuStyle.PanelPadBottom + MenuStyle.HintHeight + MenuStyle.HintGap;

        var closeGo = UiFactory.New("Close", parent);
        UiFactory.AnchorBottomCentre(
            UiFactory.Rect(closeGo),
            0f,
            rowBottom,
            new Vector2(closeWidth, MenuStyle.ButtonRowHeight));
        Watch(UiFactory.SmallButton(closeGo, "Close", () => CloseRequested?.Invoke(), _callbacks));

        var hintGo = UiFactory.New("Hint", parent);
        UiFactory.AnchorBottom(
            UiFactory.Rect(hintGo),
            MenuStyle.PanelPad,
            MenuStyle.PanelPadBottom,
            MenuStyle.HintHeight);
        _hint = UiFactory.Text(
            hintGo,
            GameFonts.Description,
            MenuStyle.HintSize,
            MenuStyle.Text,
            TextAlignmentOptions.Center,
            false);
        // A tutorial status line can be longer than the narrower Modules panel; truncating inside the
        // panel reads better than overflowing past its rounded edge.
        _hint.overflowMode = TextOverflowModes.Ellipsis;
    }

    private void SetTab(ScreenTab tab)
    {
        if (_tab == tab)
            return;

        _tab = tab;
        _picker?.Close();
        ApplyTab(resetScroll: true);
    }

    /// <summary>
    /// Shows the active page, recolours the tabs and resizes the panel to the page.
    /// <para>
    /// The width is applied before the active page is measured. Both pages wrap text against the panel
    /// they are inside, and the module cards are only their measured 310px when the narrow panel is the
    /// one holding them — measuring a card at the Actions page's width reports a height it will not have
    /// once the panel shrinks around it.
    /// </para>
    /// <para>
    /// Scrolling is only reset on an open or a tab change. Doing it after every action would throw the
    /// owner back to the top of the list every time they clicked something halfway down it.
    /// </para>
    /// </summary>
    private void ApplyTab(bool resetScroll)
    {
        if (_panel is null || _modules is null || _actions is null || _tutorial is null)
            return;

        _actions.SetActive(_tab == ScreenTab.Actions);
        _tutorial.SetActive(_tab == ScreenTab.Tutorial);
        _modules.SetActive(_tab == ScreenTab.Modules);

        Recolour(_actionsTabPlate, _tab == ScreenTab.Actions);
        Recolour(_tutorialTabPlate, _tab == ScreenTab.Tutorial);
        Recolour(_modulesTabPlate, _tab == ScreenTab.Modules);

        // Only the module cards are pixel-measured; the other two pages carry sentences and buttons
        // side by side and want the wider panel.
        var width = _tab == ScreenTab.Modules ? MenuStyle.PanelWidth : MenuStyle.ActionsPanelWidth;
        _panel.sizeDelta = new Vector2(width, _panel.sizeDelta.y);

        float page;
        switch (_tab)
        {
            case ScreenTab.Modules:
                _modules.Sync();
                page = _modules.PreferredHeight;
                break;
            case ScreenTab.Tutorial:
                _tutorial.Sync();
                page = _tutorial.PreferredHeight;
                break;
            default:
                _actions.Sync();
                page = _actions.PreferredHeight;
                break;
        }

        _panel.sizeDelta = new Vector2(width, MenuStyle.PanelHeight(page));

        if (!resetScroll)
            return;

        switch (_tab)
        {
            case ScreenTab.Modules:
                _modules.ScrollToTop();
                break;
            case ScreenTab.Tutorial:
                _tutorial.ScrollToTop();
                break;
            default:
                _actions.ScrollToTop();
                break;
        }
    }

    private static void Recolour(Image? plate, bool selected)
    {
        if (plate != null)
            plate.color = selected ? MenuStyle.CardOn : MenuStyle.CardIdle;
    }

    /// <summary>Gives a footer or tab button the same hover sound the cards and the game's buttons have.</summary>
    private void Watch(Button button) => _footerHovers.Add(new HoverWatcher(button.targetGraphic.TryCast<Image>()));
}
