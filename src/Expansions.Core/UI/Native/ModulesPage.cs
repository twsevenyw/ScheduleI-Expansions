using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Expansions.Core.UI.Native;

/// <summary>
/// The toggle page: one <see cref="ModuleCard"/> per registered module, scrolling when there are more
/// than fit.
/// <para>
/// Card geometry is the measured <c>research-ext/UI-STYLE.md</c> spec and the panel is exactly as wide
/// as one card plus its padding, so nothing here scales with the panel — the Actions page widens the
/// panel and this page must look identical either way.
/// </para>
/// </summary>
internal sealed class ModulesPage
{
    private readonly List<ModuleCard> _cards = new();

    private readonly RectTransform _root;
    private readonly ScrollView _scroll;
    private readonly TextMeshProUGUI _empty;

    private float _budget = MenuStyle.MaxListHeight;

    private ModulesPage(RectTransform root, ScrollView scroll, TextMeshProUGUI empty)
    {
        _root = root;
        _scroll = scroll;
        _empty = empty;
    }

    /// <summary>Height the panel has to give this page, list content permitting.</summary>
    public float PreferredHeight { get; private set; } = MenuStyle.CardMinHeight;

    public static ModulesPage Build(Transform parent)
    {
        var pageGo = UiFactory.New("ModulesPage", parent);
        var page = UiFactory.Rect(pageGo);
        UiFactory.Stretch(page);

        var scroll = ScrollView.Build(pageGo, MenuStyle.ListPadX, MenuStyle.CardGap);

        var emptyGo = UiFactory.New("Empty", scroll.Content.transform);
        var empty = UiFactory.Text(
            emptyGo,
            GameFonts.Description,
            MenuStyle.DescSize,
            MenuStyle.Text,
            TextAlignmentOptions.Center,
            true);
        empty.text = "No expansion mods are installed.";
        emptyGo.AddComponent<LayoutElement>().preferredHeight = MenuStyle.CardMinHeight;
        emptyGo.SetActive(false);

        return new ModulesPage(page, scroll, empty);
    }

    public void SetActive(bool active)
    {
        if (InteropObjects.Alive(_root))
            _root.gameObject.SetActive(active);
    }

    /// <summary>How much page height the screen is willing to give this list. See <see cref="Sync"/>.</summary>
    public void SetBudget(float budget) => _budget = Mathf.Max(MenuStyle.CardMinHeight, budget);

    /// <summary>
    /// Rebuilds the cards from the registry when the module set changed, otherwise just refreshes their
    /// on/off state, then remeasures. Call while the canvas is enabled: TMP reports no preferred height
    /// until its text has been laid out once.
    /// </summary>
    public void Sync()
    {
        var contexts = ExpansionRegistry.Contexts;

        if (!Matches(contexts))
            Rebuild(contexts);
        else
            foreach (var card in _cards)
                card.SyncFromRegistry();

        _empty.gameObject.SetActive(contexts.Count == 0);

        _scroll.Rebuild();
        PreferredHeight = Mathf.Clamp(_scroll.ContentHeight, MenuStyle.CardMinHeight, _budget);
    }

    public void ScrollToTop() => _scroll.ScrollToTop();

    public void TickScroll(bool allowKeys) => _scroll.Tick(allowKeys);

    /// <summary>Plays the hover sound as the pointer arrives on a card, like the game's own buttons.</summary>
    public void PollHover()
    {
        foreach (var card in _cards)
        {
            if (card.Hover.Entered())
                GameSounds.PlayHover();
        }
    }

    public void Destroy()
    {
        foreach (var card in _cards)
            card.Destroy();

        _cards.Clear();
    }

    private bool Matches(IReadOnlyList<ModuleContext> contexts)
    {
        if (_cards.Count != contexts.Count)
            return false;

        for (var i = 0; i < contexts.Count; i++)
        {
            if (!string.Equals(_cards[i].ModuleId, contexts[i].Module.Id, StringComparison.Ordinal))
                return false;

            if (!InteropObjects.Alive(_cards[i].Root))
                return false;
        }

        return true;
    }

    private void Rebuild(IReadOnlyList<ModuleContext> contexts)
    {
        foreach (var card in _cards)
            card.Destroy();

        _cards.Clear();

        foreach (var context in contexts)
        {
            var module = context.Module;
            _cards.Add(ModuleCard.Build(
                _scroll.Content.transform,
                module.Id,
                module.DisplayName,
                module.Description,
                context.IsActive));
        }

        // The empty-state label is a layout child too, so keep it last in the list.
        _empty.transform.SetAsLastSibling();
    }
}
