using Expansions.Core.Actions;
using Expansions.Core.Diagnostics;
using Expansions.Tweaks.Runtime;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Expansions.Tweaks.AutoReorder;

/// <summary>
/// Puts a tick box on every row of the phone's Deliveries app — both the active orders and the past
/// orders, since those are the two places the player already manages a delivery.
/// <para>
/// The box is not drawn: it is a clone of the checkbox the phone already uses for "Listed for sale"
/// in the Product Manager app, so it inherits that sprite, that checkmark, that size and that
/// palette exactly, and it keeps inheriting them if the game restyles. No art ships with this mod.
/// </para>
/// <para>
/// Only the visual is cloned, not the <c>Toggle</c> component. A cloned <c>Toggle</c> would arrive
/// carrying the donor's inspector-wired <c>onValueChanged</c> calls and possibly a
/// <c>ToggleGroup</c>; a plain <c>Button</c> over the same graphic, with the checkmark child shown or
/// hidden to signal state, has neither problem and needs only <c>UnityAction</c>, which is the one
/// interop delegate shape this codebase has already proven.
/// </para>
/// </summary>
internal sealed class AutoReorderUi
{
    /// <summary>Also the adoption key: a rebuilt row is re-bound rather than given a second box.</summary>
    private const string TickName = "Expansions_AutoReorderTick";

    private const float RefreshSeconds = 0.15f;

    /// <summary>Canvas pixels between the tick box and the control it sits beside.</summary>
    private const float Gap = 6f;

    private readonly AutoReorderEngine _engine;
    private readonly AutoReorderSettings _settings;
    private readonly Dictionary<int, TickHandle> _ticks = new();

    private Toggle? _donor;
    private float _nextRefresh;

    internal AutoReorderUi(AutoReorderEngine engine, AutoReorderSettings settings)
    {
        _engine = engine;
        _settings = settings;
    }

    /// <summary>Why there are no tick boxes, or empty when there are. Reported by the probe.</summary>
    internal string Failure { get; private set; } = "the deliveries app has not been opened yet";

    internal int AttachedCount => _ticks.Count;

    internal string DonorName => _donor != null ? _donor.gameObject.name : "none resolved yet";

    internal void Tick()
    {
        var now = Time.realtimeSinceStartup;
        if (now < _nextRefresh)
            return;

        _nextRefresh = now + RefreshSeconds;

        Prune();

        if (!_settings.Enabled.Value)
        {
            Clear("the auto-reorder tweak is switched off");
            return;
        }

        if (!AutoReorderHostGate.Evaluate(out var authority))
        {
            Clear(authority);
            return;
        }

        var app = DeliveryApi.App();
        if (app is null)
        {
            Failure = "the phone's deliveries app has not started yet";
            return;
        }

        if (!DeliveryApi.IsAppOpen(app))
            return;

        // Same gate the engine uses, so a box can never show ticked for an order belonging to a
        // different save than the one loaded.
        if (!AutoReorderState.BindToLoadedSave(out var binding))
        {
            Failure = binding;
            return;
        }

        if (Donor() == null)
        {
            Failure =
                "no checkbox in the game's own UI to clone - the phone's 'Listed for sale' toggle was not found, " +
                "so arm repeats from the Expansions screen instead";
            return;
        }

        Failure = string.Empty;

        foreach (var row in DeliveryApi.StatusDisplays(app))
            Attach(row, "DeliveryInstance", "StatusImage", "StatusLabel");

        foreach (var row in DeliveryApi.PastDisplays(app))
            Attach(row, "Receipt", "_ReorderButton", "_reorderPriceLabel");
    }

    /// <summary>Removes every box. The rows themselves are the game's, and are left untouched.</summary>
    internal void Destroy() => Clear(string.Empty);

    private void Clear(string reason)
    {
        foreach (var handle in _ticks.Values)
        {
            if (handle.Root != null)
                Object.Destroy(handle.Root);
        }

        _ticks.Clear();
        Failure = reason;
    }

    /// <summary>Drops handles whose row the app has destroyed, so the table cannot grow unbounded.</summary>
    private void Prune()
    {
        if (_ticks.Count == 0)
            return;

        var dead = _ticks
            .Where(static pair => pair.Value.Root == null)
            .Select(static pair => pair.Key)
            .ToArray();

        foreach (var id in dead)
            _ticks.Remove(id);
    }

    /// <summary>
    /// Ensures one row carries a correctly bound tick box.
    /// <para>
    /// Rows are pooled, so the delivery a given row shows changes. The key is therefore re-read on
    /// every refresh rather than captured when the box was created.
    /// </para>
    /// </summary>
    private void Attach(object? row, string sourceMember, string primaryAnchor, string fallbackAnchor)
    {
        var component = AsComponent(row);
        if (component == null)
            return;

        var source = Members.ReadObject(row, sourceMember);
        if (!OrderShape.TryRead(source, out var contents) || contents.Items.Count == 0)
            return;

        var anchor = AnchorFor(row, primaryAnchor) ?? AnchorFor(row, fallbackAnchor);
        if (anchor == null)
        {
            Note($"a deliveries row has no '{primaryAnchor}' for the tick box to sit beside");
            return;
        }

        var id = component.GetInstanceID();

        if (!_ticks.TryGetValue(id, out var handle) || handle.Root == null)
        {
            var created = Create(anchor);
            if (created is null)
                return;

            handle = created;
            _ticks[id] = handle;
        }

        handle.Key = contents.Key;
        handle.Contents = contents;

        Place(handle.Rect, anchor);
        Show(handle, AutoReorderState.IsArmed(contents.Key));
    }

    private TickHandle? Create(RectTransform anchor)
    {
        var donor = Donor();
        if (donor == null)
            return null;

        var box = donor.targetGraphic;
        var check = donor.graphic;
        var path = RelativePath(check.transform, box.transform);

        if (path is null)
        {
            Failure = "the donor checkbox's checkmark is not a child of its background";
            return null;
        }

        try
        {
            Object original = box.gameObject;
            var clone = Object.Instantiate(original, anchor.parent, false).Cast<GameObject>();
            clone.name = TickName;
            clone.transform.localScale = Vector3.one;
            clone.transform.SetAsLastSibling();

            var checkmark = path.Length == 0 ? clone.transform : clone.transform.Find(path);
            if (checkmark == null)
            {
                Object.Destroy(clone);
                Failure = "the cloned checkbox lost its checkmark child";
                return null;
            }

            // A cloned Toggle would arrive still holding the donor's inspector-wired calls and its
            // ToggleGroup, so every Selectable that came with the clone goes before ours is added.
            foreach (var selectable in clone.GetComponents<Selectable>())
            {
                if (selectable != null)
                    Object.Destroy(selectable);
            }

            var image = clone.GetComponent<Image>();
            if (image == null)
            {
                Object.Destroy(clone);
                Failure = "the cloned checkbox has no Image to click on";
                return null;
            }

            var button = clone.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = donor.transition;

            var colors = donor.colors;
            button.colors = colors;

            // Assigning colors starts a fade from Unity's default opaque-white block; snap past it.
            image.canvasRenderer.SetColor(colors.normalColor);

            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            button.interactable = true;

            // A layout group on the row would otherwise treat the box as another item in the flow.
            // Explicit Unity null check rather than ??, which does not see a destroyed component.
            var layout = clone.GetComponent<LayoutElement>();
            if (layout == null)
                layout = clone.AddComponent<LayoutElement>();

            layout.ignoreLayout = true;

            var handle = new TickHandle(clone, button, checkmark.gameObject, clone.GetComponent<RectTransform>());
            handle.Bind(() => Clicked(handle));

            if (!clone.activeSelf)
                clone.SetActive(true);

            return handle;
        }
        catch (Exception ex)
        {
            Failure = $"cloning the phone's checkbox threw ({TweakLog.Describe(ex)})";
            TweakLog.Detail(Failure);
            return null;
        }
    }

    private void Clicked(TickHandle handle)
    {
        try
        {
            if (handle.Contents.IsEmpty)
                return;

            if (AutoReorderState.IsArmed(handle.Key))
            {
                _engine.Disarm(handle.Key);
                Show(handle, false);
                ActionLog.Note(
                    $"Auto-reorder off for {OrderShape.Describe(handle.Contents.Items)} from {handle.Contents.Store}.");
                return;
            }

            var key = _engine.Arm(handle.Contents, string.Empty, out var message);
            if (key.Length == 0)
            {
                ActionLog.Fail($"Auto-reorder: {message}");
                return;
            }

            handle.Key = key;
            Show(handle, true);
            ActionLog.Ok($"Auto-reorder on. {message}");
        }
        catch (Exception ex)
        {
            TweakLog.Error("Toggling an auto-reorder from the phone failed.", ex);
        }
    }

    private static void Show(TickHandle handle, bool armed)
    {
        if (handle.Check == null || handle.Check.activeSelf == armed)
            return;

        handle.Check.SetActive(armed);
    }

    /// <summary>
    /// Positions the box immediately left of <paramref name="anchor"/>, vertically centred on it.
    /// <para>
    /// Done in the parent's local space from <c>localPosition</c> plus <c>rect</c> rather than by
    /// copying the anchor's anchoring: the control it sits beside may be stretched, point-anchored or
    /// pivoted anywhere, and this arithmetic is the same in all three cases. It runs on every
    /// refresh, so a row that lays out a frame later still ends up with the box in the right place.
    /// </para>
    /// </summary>
    private static void Place(RectTransform tick, RectTransform anchor)
    {
        if (tick == null || anchor == null || anchor.parent == null)
            return;

        if (tick.parent == null || tick.parent.GetInstanceID() != anchor.parent.GetInstanceID())
            tick.SetParent(anchor.parent, false);

        // Measured before the anchors change: a stretched clone would collapse to nothing otherwise.
        var size = tick.rect.size;

        tick.anchorMin = new Vector2(0.5f, 0.5f);
        tick.anchorMax = new Vector2(0.5f, 0.5f);
        tick.pivot = new Vector2(0.5f, 0.5f);

        if (size.x > 0.5f && size.y > 0.5f)
            tick.sizeDelta = size;

        var box = anchor.rect;
        var leftEdge = anchor.localPosition.x + box.xMin;
        var centreY = anchor.localPosition.y + box.center.y;

        tick.localPosition = new Vector3(leftEdge - Gap - (tick.rect.width * 0.5f), centreY, 0f);
    }

    /// <summary>The control the box is placed to the left of, as a rect. Null when the row lacks it.</summary>
    private static RectTransform? AnchorFor(object? row, string member)
    {
        var component = AsComponent(Members.ReadObject(row, member));
        if (component == null)
            return null;

        var rect = component.transform.TryCast<RectTransform>();
        return rect != null && rect.parent != null ? rect : null;
    }

    private static Component? AsComponent(object? value)
    {
        var component = value switch
        {
            Component direct => direct,
            Il2CppObjectBase native => native.TryCast<Component>(),
            _ => null,
        };

        return component != null && GameReflection.IsPresent(component) ? component : null;
    }

    /// <summary>
    /// The phone's own checkbox. Preferred donor is the Product Manager app's "Listed for sale"
    /// toggle: same canvas, same scale and same palette as the deliveries rows.
    /// </summary>
    private Toggle? Donor()
    {
        if (_donor != null)
            return _donor;

        var panelType = GameReflection.FindType(AutoReorderTypes.ProductAppDetailPanel);
        if (panelType is not null)
        {
            foreach (var (_, typed) in Members.FindAll(panelType))
            {
                var listed = Usable(Members.ReadObject(typed, "ListedForSale") as Toggle)
                             ?? Usable(Members.ReadObject(typed, "FavouriteProduct") as Toggle);

                if (listed != null)
                    return _donor = listed;
            }
        }

        // Any other checkbox in the game is still a native one; the settings screens carry several.
        foreach (var candidate in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Toggle>()))
        {
            var fallback = Usable(candidate?.TryCast<Toggle>());
            if (fallback != null)
                return _donor = fallback;
        }

        return null;
    }

    /// <summary>A donor is only usable if its checkmark is a separate object inside its background.</summary>
    private static Toggle? Usable(Toggle? toggle)
    {
        if (toggle == null || !GameReflection.IsPresent(toggle))
            return null;

        var box = toggle.targetGraphic;
        var check = toggle.graphic;

        if (box == null || check == null || box.GetInstanceID() == check.GetInstanceID())
            return null;

        return RelativePath(check.transform, box.transform) is null ? null : toggle;
    }

    /// <summary>Path from <paramref name="root"/> down to <paramref name="child"/>, or null if unrelated.</summary>
    private static string? RelativePath(Transform child, Transform root)
    {
        if (child == null || root == null)
            return null;

        if (child.GetInstanceID() == root.GetInstanceID())
            return string.Empty;

        var parts = new List<string>(4);

        for (var current = child; current != null; current = current.parent)
        {
            if (current.GetInstanceID() == root.GetInstanceID())
            {
                parts.Reverse();
                return string.Join('/', parts);
            }

            parts.Add(current.name);
        }

        return null;
    }

    /// <summary>Records the first thing that went wrong without letting later rows overwrite it.</summary>
    private void Note(string reason)
    {
        if (Failure.Length == 0)
            Failure = reason;
    }

    /// <summary>
    /// One row's box. Holds the managed handler as well as the <c>UnityAction</c>: the native side
    /// calls through a trampoline that needs the managed delegate to still exist, and rooting both
    /// halves in the object that owns the button is what keeps that from drifting out of sync.
    /// </summary>
    private sealed class TickHandle
    {
        internal TickHandle(GameObject root, Button button, GameObject check, RectTransform rect)
        {
            Root = root;
            Button = button;
            Check = check;
            Rect = rect;
        }

        internal GameObject Root { get; }

        internal Button Button { get; }

        internal GameObject Check { get; }

        internal RectTransform Rect { get; }

        internal string Key { get; set; } = string.Empty;

        internal OrderContents Contents { get; set; } = OrderContents.Empty;

        private Action? Handler { get; set; }

        private UnityAction? Listener { get; set; }

        internal void Bind(Action handler)
        {
            Handler = handler;
            Listener = handler;

            Button.onClick.RemoveAllListeners();
            Button.onClick.AddListener(Listener);
        }
    }
}
