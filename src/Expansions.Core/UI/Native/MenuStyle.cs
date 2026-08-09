using UnityEngine;

namespace Expansions.Core.UI.Native;

/// <summary>
/// The measured Schedule I UI constants from <c>research-ext/UI-STYLE.md</c>.
/// <para>
/// Every value here came from pixel-measuring shipped UI or from aggregating the game's own
/// serialised components — they are not design choices and rounding them to nicer numbers breaks the
/// match. The comment on each group names the section it came from.
/// </para>
/// </summary>
internal static class MenuStyle
{
    // §1.1 palette. The description colour is the counter-intuitive one: it is white, not grey. At
    // 12px an Open Sans SemiBoldItalic stem is ~1.4px wide, so SDF antialiasing never reaches full
    // coverage and the text *reads* grey; setting real grey looks washed out beside the reference.
    public static readonly Color Backdrop = Rgb(0x01, 0x01, 0x01);
    public static readonly Color CardIdle = Rgb(0x45, 0x47, 0x49);
    public static readonly Color CardOn = Rgb(0x35, 0x9A, 0x38);
    public static readonly Color BorderOn = Color.white;
    public static readonly Color Text = Color.white;
    public static readonly Color TrophyGold = Rgb(0xFF, 0xC0, 0x4B);
    public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.55f);

    // Actions surface. Derived from the palette above rather than measured — the reference screenshot
    // has no equivalent of an action list, so these stay inside the same family instead of inventing
    // a second one.

    /// <summary>An action row whose availability predicate said no. Reads as inert beside an active row.</summary>
    public static readonly Color RowDisabled = Rgb(0x30, 0x32, 0x34);

    /// <summary>Body text on an inert row: still legible, clearly not the thing to read first.</summary>
    public static readonly Color RowMuted = Rgb(0x74, 0x78, 0x7B);

    /// <summary>The output pane and the picker sit on this, a step darker than a card.</summary>
    public static readonly Color Sunken = Rgb(0x1A, 0x1C, 0x1E);

    /// <summary>The picker's own backdrop: opaque, because it covers the rows it was opened from.</summary>
    public static readonly Color PickerBackdrop = Rgb(0x0B, 0x0C, 0x0D);

    /// <summary>Secondary text: group headings, row descriptions, the reason a row is greyed out.</summary>
    public static readonly Color TextDim = Rgb(0xB4, 0xB8, 0xBB);

    /// <summary>Failure lines in the output pane. Nothing else in the UI is red.</summary>
    public static readonly Color TextBad = Rgb(0xE8, 0x74, 0x6A);

    /// <summary>"Done" states: a completed tutorial chapter. Reads as the card green, lightened to text weight.</summary>
    public static readonly Color TextGood = Rgb(0x6F, 0xC7, 0x72);

    /// <summary>Scrollbar track, one step off the sunken fill so the gutter reads as a channel.</summary>
    public static readonly Color ScrollTrack = Rgb(0x22, 0x24, 0x26);

    /// <summary>Scrollbar handle: a lifted card grey, so it is visible without competing with a row.</summary>
    public static readonly Color ScrollHandle = Rgb(0x5F, 0x63, 0x66);

    public static readonly Color ScrollHandleHover = Rgb(0x84, 0x88, 0x8C);

    /// <summary>Dims the live scene behind the panel. §7.2 item 1: the reference backdrop is most
    /// likely the blurred world under a near-opaque scrim, not a shipped UI texture.</summary>
    public static readonly Color Scrim = new Color(0f, 0f, 0f, 0.86f);

    // §1.2 state tints, the "black darkening overlay" ColorBlock (64 of the game's 545 buttons).
    // These are alphas on an OPAQUE WHITE overlay: Selectable tinting multiplies Image.color by the
    // tint, so a transparent overlay graphic multiplies to nothing in every state.
    public static readonly Color TintNormal = new Color(0f, 0f, 0f, 0f);
    public static readonly Color TintHover = new Color(0f, 0f, 0f, 47f / 255f);
    public static readonly Color TintPressed = new Color(0f, 0f, 0f, 110f / 255f);
    public static readonly Color TintDisabled = new Color(200f / 255f, 200f / 255f, 200f / 255f, 128f / 255f);

    /// <summary>§6.3: 406 of the game's 473 buttons. Unity's ColorTint fade is linear and the game
    /// never overrides it with a curve, so do not add easing it does not have.</summary>
    public const float FadeDuration = 0.1f;

    /// <summary>§6.3, extrapolated from the button fade — the game gives no panel-transition reference.</summary>
    public const float PanelFadeDuration = 0.12f;

    // §6.1 canvas. Every scaler in the game that scales with screen size uses 1920x1080 with
    // match = 0 (width only). 0.5 is the value most mods get wrong; on a 21:9 display it drifts.
    public static readonly Vector2 ReferenceResolution = new(1920f, 1080f);
    public const float ReferencePixelsPerUnit = 100f;
    public const float MatchWidthOrHeight = 0f;
    public const int SortingOrder = 30000;

    /// <summary>Unity's built-in "UI" layer.</summary>
    public const int UiLayer = 5;

    // §3.1 card anatomy, measured with GetPixel on the reference at native scale.
    public const float CardWidth = 310f;
    public const float CardMinHeight = 71f;
    public const float CardGap = 10f;
    public const float CardRadius = 8.5f;
    public const float CardBorder = 2f;

    /// <summary>§4.4: nests correctly inside an 8.5px outer radius once inset by the 2px border.</summary>
    public const float CardFillRadius = 6.375f;

    // §3.3 layout recipe, solved against Open Sans metrics (ascender 1.0688em, line height 1.3618em).
    public const int CardPadX = 12;
    public const int CardPadY = 5;
    public const float CardSpacing = 3f;
    public const float TitleSize = 18f;
    public const float TitleLineHeight = 25f;
    public const float DescSize = 12f;

    // §3.5 trophy: the gold ink measures 19x19 but the game sprite carries a soft black halo.
    public const float TrophyBox = 24f;
    public static readonly Vector2 TrophyOffset = new(5f, -5f);

    // §5.5 list container.
    public const float ListPadX = 10f;

    // §2.5 type scale. Heading and button-label sizes are extrapolated from the game's size
    // histogram (24 -> 157 components, 14 -> 225) rather than measured off the reference.
    public const float HeadingSize = 24f;
    public const float ButtonLabelSize = 14f;
    public const float HintSize = 11f;

    // §4.4 panel: r = 10.2px is the game's most common panel radius (104 uses).
    public const float PanelRadius = 10.2f;
    public const float PanelShadowSpread = 13.6f;
    public const int PanelPad = 16;
    public const float HeadingHeight = 34f;
    public const float HeadingGap = 10f;
    public const float FooterGap = 12f;
    public const float ButtonRowHeight = 30f;
    public const float HintHeight = 16f;
    public const float HintGap = 8f;
    public const int PanelPadBottom = 14;

    /// <summary>Fallback list cap, used only until the screen tells a page its real budget. See
    /// <see cref="PageBudget"/>.</summary>
    public const float MaxListHeight = 620f;

    /// <summary>Card width plus the list container's own horizontal padding.</summary>
    public const float ViewportWidth = CardWidth + (2f * ListPadX);

    /// <summary>
    /// Added to the narrow panel rather than taken out of it: the card is a pixel-measured 310px and
    /// squeezing the scrollbar's channel out of that width would break the one page on this screen
    /// whose geometry is a measurement rather than a choice.
    /// </summary>
    public const float PanelWidth = ViewportWidth + ScrollGutter + (2f * PanelPad);

    // Tab strip, between the heading and whichever page is showing. Three tabs at 104 plus two 6px
    // gaps is 324, which still clears the narrow Modules panel's 330px inner width.
    public const float TabHeight = 26f;
    public const float TabGap = 6f;
    public const float TabRowGap = 10f;
    public const float TabLabelSize = 13f;
    public const float TabWidth = 104f;

    /// <summary>Everything below the page area: the Close button plus the status line.</summary>
    public const float FooterHeight = ButtonRowHeight + HintGap + HintHeight + PanelPadBottom;

    /// <summary>Heading, tab strip and the gaps around them — the fixed height above any page.</summary>
    public const float HeaderHeight = PanelPad + HeadingHeight + HeadingGap + TabHeight + TabRowGap;

    // Actions page. Wider than the module page because an action row carries a sentence and a button
    // side by side, and a probe report path is 100 characters that must not be truncated.
    public const float ActionsPanelWidth = 640f;
    public const float ActionRowHeight = 60f;
    public const float ActionRowGap = 6f;
    public const float ActionRowPadX = 12f;
    public const float ActionLabelSize = 14f;
    public const float ActionDescSize = 11f;
    public const float ActionButtonWidth = 104f;
    public const float ActionButtonHeight = 26f;
    public const float GroupHeaderHeight = 24f;
    public const float GroupHeaderSize = 13f;

    /// <summary>Fallback list cap, used only when the screen height cannot be read. The real cap is
    /// computed per open from the space the panel actually has; see <see cref="PageBudget"/>.</summary>
    public const float MaxActionListHeight = 372f;

    // Output pane: the replacement for everything the console used to print. Bounded and collapsible —
    // it used to be a fixed 168px slice of the panel that the action list could never reclaim.
    public const float OutputHeight = 168f;
    public const float OutputGap = 10f;
    public const float OutputPadX = 10f;
    public const float OutputPadY = 8f;
    public const float OutputLineSize = 11f;
    public const float OutputRadius = 6f;

    /// <summary>Header strip, and therefore the pane's whole height once collapsed.</summary>
    public const float OutputBarHeight = 26f;

    public const float OutputTitleSize = 11f;
    public const float OutputToggleWidth = 68f;

    /// <summary>Smallest useful expanded body: about three wrapped lines.</summary>
    public const float OutputMinBodyHeight = 44f;

    /// <summary>Ceiling on the pane's share of the page, so a tall screen grows the list, not the log.</summary>
    public const float OutputMaxPageFraction = 0.32f;

    // Scroll containers. A visible gutter on every list is the point: with 46 actions the owner has to
    // be able to see at a glance that there is more below.
    public const float ScrollBarWidth = 8f;
    public const float ScrollBarGap = 4f;
    public const float ScrollBarRadius = 4f;

    /// <summary>Width every scrolling region reserves on its right edge for the bar.</summary>
    public const float ScrollGutter = ScrollBarWidth + ScrollBarGap;

    /// <summary>Canvas pixels per wheel notch. Roughly one action row.</summary>
    public const float ScrollWheelStep = 62f;

    /// <summary>Canvas pixels per arrow-key press.</summary>
    public const float ScrollKeyStep = 44f;

    /// <summary>Fraction of the viewport a Page Up / Page Down moves, leaving a line of overlap.</summary>
    public const float ScrollPageFraction = 0.88f;

    // Panel sizing. The panel grows to use the screen it is on instead of sitting at a fixed height
    // with an unreachable list behind it.
    public const float PanelScreenFraction = 0.9f;
    public const float PanelMinHeight = 360f;
    public const float PanelMaxHeight = 1000f;

    /// <summary>Used when the canvas scale is not readable yet, e.g. the frame the screen is built.</summary>
    public const float FallbackScreenHeight = 1080f;

    // Tutorial page: one row per chapter over a toolbar carrying the whole-line controls.
    public const float TutorialRowHeight = 66f;
    public const float TutorialRowGap = 6f;
    public const float TutorialRowPadX = 12f;
    public const float TutorialToolbarHeight = 28f;
    public const float TutorialToolbarGap = 10f;
    public const float TutorialToolbarButtonWidth = 116f;
    public const float TutorialToolbarGapX = 8f;
    public const float TutorialToggleWidth = 78f;
    public const float TutorialToggleHeight = 26f;
    public const float TutorialTitleSize = 14f;
    public const float TutorialDescSize = 11f;
    public const float TutorialStateSize = 11f;

    // Event chooser: a small standalone overlay the event hotkey raises in-world, deliberately
    // narrower and shorter than the Expansions screen so it reads as a prompt rather than a menu.
    public const float EventPanelWidth = 460f;
    public const float EventPad = 12f;
    public const float EventHeaderHeight = 22f;
    public const float EventHeaderGap = 6f;
    public const float EventRowHeight = 42f;
    public const float EventRowGap = 4f;
    public const float EventRowPadX = 10f;
    public const float EventRowIndexWidth = 20f;

    /// <summary>A row's own width: the panel less its padding and the scroll gutter.</summary>
    public const float EventRowWidth = EventPanelWidth - (2f * EventPad) - ScrollGutter;

    /// <summary>Body height in toast mode. Two wrapped lines of result text, which every
    /// <c>ActionResult</c> in the suite fits inside.</summary>
    public const float EventToastHeight = 40f;

    public const float EventTitleSize = 13f;
    public const float EventDescSize = 10f;
    public const float EventHeaderSize = 13f;
    public const float EventFooterSize = 10f;
    public const float EventFooterHeight = 16f;
    public const float EventFooterGap = 8f;

    /// <summary>Rows visible without scrolling. Past this the list scrolls; the chooser stays compact.</summary>
    public const int EventMaxVisibleRows = 7;

    /// <summary>Fraction of the screen height the panel's centre sits at, measured from the bottom.</summary>
    public const float EventPanelScreenY = 0.42f;

    /// <summary>Under the Expansions screen: the two are never up together, but the order should be
    /// deterministic if a third party opens one over the other.</summary>
    public const int EventSortingOrder = SortingOrder - 10;

    /// <summary>How long the fired-result toast stays on screen before it fades itself out.</summary>
    public const float EventToastSeconds = 2.6f;

    // Picker overlay, drawn over the whole page area.
    public const float PickerTitleHeight = 24f;
    public const float PickerSearchHeight = 22f;
    public const float PickerGap = 8f;
    public const float PickerRowHeight = 34f;
    public const float PickerRowGap = 4f;
    public const float PickerTitleSize = 15f;
    public const float PickerSearchSize = 12f;
    public const float PickerRowLabelSize = 13f;
    public const float PickerRowDetailSize = 10f;
    public const float PickerCancelWidth = 96f;

    /// <summary>Rendered picker rows. Search narrows the list; building 130 rows per keystroke does not.</summary>
    public const int PickerMaxRows = 40;

    public static float PanelHeight(float pageHeight) =>
        HeaderHeight + pageHeight + FooterGap + FooterHeight;

    /// <summary>
    /// The tallest a page may be on a screen whose logical (canvas-space) height is
    /// <paramref name="screenHeight"/>. Inverse of <see cref="PanelHeight"/>, clamped so the panel is
    /// never taller than the display and never collapses to nothing on a very short one.
    /// </summary>
    public static float PageBudget(float screenHeight)
    {
        var panel = Mathf.Clamp(screenHeight * PanelScreenFraction, PanelMinHeight, PanelMaxHeight);
        return Mathf.Max(ActionRowHeight, panel - HeaderHeight - FooterGap - FooterHeight);
    }

    private static Color Rgb(byte r, byte g, byte b) => new(r / 255f, g / 255f, b / 255f, 1f);
}
