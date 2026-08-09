using UnityEngine.UI;

namespace Expansions.Core.UI.Native;

/// <summary>
/// Latched pointer-enter detection for one control, without an injected event handler.
/// <para>
/// <c>Selectable.Transition.ColorTint</c> is already driving the overlay's <c>CanvasRenderer</c>
/// colour, so a non-zero tint alpha means the pointer is over it. Reading that instead of
/// implementing <c>IPointerEnterHandler</c> is what keeps <c>ClassInjector</c> out of this mod —
/// injecting a type is process-global and irreversible, which would break the reversible-module
/// guarantee.
/// </para>
/// </summary>
internal sealed class HoverWatcher
{
    private readonly Image? _overlay;
    private bool _wasHovered;

    public HoverWatcher(Image? overlay) => _overlay = overlay;

    /// <summary>True only on the frame the pointer arrives, so a sound fires once per entry.</summary>
    public bool Entered()
    {
        var hovered = UiFactory.IsHovered(_overlay);
        var entered = hovered && !_wasHovered;
        _wasHovered = hovered;
        return entered;
    }
}
