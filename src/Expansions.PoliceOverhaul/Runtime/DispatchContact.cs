using S1API.Entities;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Invisible phone contact that every Police Improvements message is sent from.
/// <para>
/// S1API messaging needs an NPC sender. Using a federal-agent clone would attribute the text to
/// whoever that officer was copied from (and put a temporary chase NPC in the contacts list). A
/// dedicated non-physical contact is one intentional entry — "Dispatch" — and nothing else.
/// </para>
/// <para>
/// Discovered by S1API from the type alone, so the id and simple type name are save data: do not
/// rename either. The contact exists whenever this DLL is loaded, even if the module is toggled off;
/// we simply stop sending.
/// </para>
/// </summary>
public sealed class DispatchContact : NPC
{
    /// <summary>Never rename: this is the on-disk NPC id and the messaging key.</summary>
    internal const string ContactId = "expansions_police_dispatch";

    internal const string ContactFirstName = "Dispatch";

    internal const string ContactLastName = "Office";

    /// <summary>Invisible contact — messaging only, no world body.</summary>
    public override bool IsPhysical => false;

    protected override void ConfigurePrefab(NPCPrefabBuilder builder) =>
        builder.WithIdentity(ContactId, ContactFirstName, ContactLastName);

    protected override void OnCreated()
    {
        base.OnCreated();

        try
        {
            // Keep it out of the Customer / Dealer / Supplier filtered contact lists. The messages
            // app still shows the thread when Dispatch texts the player — that is the whole point.
            ConversationCanBeHidden = true;
            ClearConversationCategories();
            PoliceMessages.NoteContactReady(this);
        }
        catch (Exception ex)
        {
            PoliceLog.Warn($"Dispatch contact initialised with a warning: {PoliceLog.Describe(ex)}");
        }
    }
}
