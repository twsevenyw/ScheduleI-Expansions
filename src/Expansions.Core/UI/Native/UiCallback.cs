using UnityEngine.Events;

namespace Expansions.Core.UI.Native;

/// <summary>
/// Keeps a button handler alive for as long as the button does.
/// <para>
/// <c>UnityAction</c> is not a CLR delegate under Il2CppInterop — it is projected as a
/// <c>MulticastDelegate</c> subclass with an implicit conversion from <c>System.Action</c>. The native
/// side ends up calling through a trampoline that needs the managed delegate to still exist, so
/// whoever creates the button holds one of these until it destroys it. Rooting both halves in one
/// object is what keeps that guarantee from drifting out of sync.
/// </para>
/// </summary>
internal sealed class UiCallback
{
    public UiCallback(Action handler)
    {
        Handler = handler;
        Listener = handler;
    }

    public Action Handler { get; }

    public UnityAction Listener { get; }
}
