using Expansions.PoliceOverhaul.State;
using S1API.Entities;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Player-facing police copy. Substantive events go out as phone texts from <see cref="DispatchContact"/>;
/// toasts are reserved for moments where waiting on the phone would be too slow to act on.
/// </summary>
internal static class PoliceMessages
{
    private static NPC? _contact;
    private static string _lastDelivery = "none yet";
    private static string _lastBody = string.Empty;
    private static int _sent;

    /// <summary>Probe / menu read-out.</summary>
    internal static string LastDelivery => _lastDelivery;

    internal static string LastBody => _lastBody;

    internal static int Sent => _sent;

    internal static bool ContactReady => _contact is not null;

    internal static void NoteContactReady(NPC contact)
    {
        _contact = contact;
        _lastDelivery = $"contact ready ({DispatchContact.ContactFirstName} {DispatchContact.ContactLastName})";
        PoliceLog.Msg($"Police messages will come from '{contact.FullName}' ({contact.ID}).");
    }

    /// <summary>
    /// Phone text. Falls back to a toast only when the contact is not up yet (menu scene / pre-save),
    /// so a substantive event is never silent.
    /// </summary>
    internal static void Message(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return;

        var text = body.Trim();
        _lastBody = text;

        var contact = Resolve();
        if (contact is not null)
        {
            try
            {
                contact.SendTextMessage(text, network: true);
                _sent++;
                _lastDelivery = $"message #{_sent} via {contact.ID}";
                PoliceLog.Detail($"Dispatch text: {TrimForLog(text)}");
                return;
            }
            catch (Exception ex)
            {
                PoliceLog.Warn($"Dispatch text failed ({PoliceLog.Describe(ex)}); falling back to a toast.");
            }
        }

        GameBridge.Notify("Dispatch", text, 10f);
        _lastDelivery = "toast fallback (Dispatch contact not ready)";
    }

    /// <summary>
    /// Immediate toast. Kept for the inbound-raid warning (and custody clock-skip), where the player
    /// has seconds to react and a phone thread is too slow.
    /// </summary>
    internal static void Toast(string title, string body, float seconds = 8f)
    {
        _lastBody = body;
        _lastDelivery = $"toast: {title}";
        GameBridge.Notify(title, body, seconds);
    }

    internal static void HeatTierChanged(bool rising, string tierName, float heat, string meaning) =>
        Message(rising
            ? $"Police attention just stepped up to {tierName} (heat {heat:0}). {meaning}"
            : $"Police attention has eased to {tierName} (heat {heat:0}). {meaning}");

    internal static void OutlawChanged(OutlawTier tier)
    {
        Message(tier switch
        {
            OutlawTier.Hunted =>
                "Your file just went Hunted. Every officer recognises you on sight, searches always turn something up, " +
                "pursuits will not time out, card-only shops will not serve you, and your dealers are taking a hazard cut. " +
                "Clear it with clean days or pay the legal fee from the Police Improvements menu.",
            OutlawTier.Marked =>
                "You are Marked. Officers treat you as always-suspicious, body searches will find contraband, " +
                "and pursuits stick around longer. Three clean days in a row (or the legal fee) drops you back toward Clean. " +
                "Stay Marked long enough and a second federal encounter or a second arrest promotes you to Hunted.",
            _ =>
                "Your outlaw flag is cleared. Fines, searches and shops are back to the normal heat rules — " +
                "but the heat score itself is unchanged, so the street presence still matches how wanted you are.",
        });
    }

    internal static void FederalBegan(bool stakeout, string propertyName, string trigger, int agents, int hours) =>
        Message(stakeout
            ? $"A plain-clothes federal team ({agents}) is sitting on {propertyName} for about {hours} in-game hour(s). " +
              $"Reason logged: {trigger}. They are not local PD — walking past them while Marked or Hunted is enough for a search. " +
              "Leaving the property does not end the watch; they hold the post until the assignment expires."
            : $"A plain-clothes federal team ({agents}) is in town asking about you for about {hours} in-game hour(s). " +
              $"Reason logged: {trigger}. They pursue on foot and will not call the whole local force in behind them. " +
              "Survive the window without getting bagged and the encounter still counts toward outlaw pressure.");

    internal static void FederalEnded(string reason) =>
        Message($"The federal team has pulled out ({reason}). The cooldown on the next visit has started. " +
                "Your heat and outlaw status are unchanged by the withdrawal itself.");

    internal static void RaidWarning(string propertyName, string when, string reason) =>
        Toast(
            "Raid inbound",
            $"{propertyName} in about {when}. {reason}. Be inside when they arrive or they take product from the containers.",
            9f);

    internal static void RaidResolved(string propertyName, bool present, int stacks, float value, string haul)
    {
        if (present)
        {
            Message($"A raid team reached {propertyName}, saw you on the property, and drove on. " +
                    "Nothing was taken. Showing up is the whole defence — the cooldown still applies.");
            return;
        }

        Message(stacks > 0
            ? $"Your place at {propertyName} was raided. They took {haul} (about ${value:0}) from the storage on site. " +
              "Legitimate gear was left alone; only product stacks were pulled, and never every last one. " +
              "Move what is left before the next warrant."
            : $"Your place at {propertyName} was searched. The containers were opened but nothing worth seizing was in them. " +
              "The raid still counts against the cooldown.");
    }

    internal static void RaidCalledOff(string propertyName, string reason) =>
        Message($"The inbound raid on {propertyName} was called off ({reason}). Nothing was taken.");

    internal static void FineSettled(float cash, float bank) =>
        Message(bank > 0.5f
            ? $"Court penalties hit: ${cash:0} taken from cash on hand, and ${bank:0} pulled from your online / bank balance " +
              "as unpaid fine debt. That bank hit is permanent play history — toggling the mod off will not refund it."
            : $"Court penalties hit: ${cash:0} taken from cash on hand. Your bank balance was not touched this time.");

    internal static void LegalFeePaid(string summary) => Message(summary);

    internal static void InformantFallout(string names) =>
        Message($"{names} is who called it in. Their relationship with you just dropped for making that call. " +
                "This is not a federal tip — it is a civilian who dialled local PD.");

    internal static void EquipmentSeized(string names) =>
        Message($"On top of the product, they kept your kit: {names}. Seeds, soil, furniture and lighting are never taken.");

    internal static void VehicleSearched(int stacks) =>
        Message($"The vehicle you were arrested next to was searched. {stacks} stack(s) of product were seized from its storage. " +
                "Vanilla leaves vehicle cargo alone — this module does not.");

    internal static void BookedIn(string sentence) =>
        Toast("Booked in", sentence, 10f);

    internal static void EarlyRelease(string body) =>
        Toast("They let you go early", body, 7f);

    private static NPC? Resolve()
    {
        if (_contact is not null)
            return _contact;

        try
        {
            var found = NPC.Get(DispatchContact.ContactId) ?? NPC.Get<DispatchContact>();
            if (found is not null)
                NoteContactReady(found);

            return found;
        }
        catch
        {
            return null;
        }
    }

    private static string TrimForLog(string text) =>
        text.Length <= 160 ? text : text[..157] + "...";
}
