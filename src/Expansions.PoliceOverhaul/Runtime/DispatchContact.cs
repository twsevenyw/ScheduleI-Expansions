namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Display labels for police announcements. Intentionally <b>not</b> an <c>S1API.Entities.NPC</c>.
/// <para>
/// A dedicated Dispatch NPC was half-built by S1API (SetVisible / GetAndValidateReferences NREs on
/// finalize) and then ticked every frame via <c>NPCActions.UpdateUmbrellaUse</c>, killing the game.
/// Messages are toast-only through <see cref="PoliceMessages"/> / <see cref="GameBridge.Notify"/> —
/// a readable announcement does not require a custom NPC, and a custom NPC is what crashed.
/// </para>
/// <para>
/// The former id <c>expansions_police_dispatch</c> is kept as a constant for probes and save-folder
/// archaeology only. No type with that id is registered anymore, so S1API will not spawn it.
/// </para>
/// </summary>
internal static class DispatchContact
{
    /// <summary>Legacy id — do not create an NPC with this. Probe / log reference only.</summary>
    internal const string ContactId = "expansions_police_dispatch";

    internal const string ContactFirstName = "Dispatch";

    internal const string ContactLastName = "Office";

    internal const string DisplayName = "Dispatch Office";
}
