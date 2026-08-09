using Expansions.PoliceOverhaul.State;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Player-facing police copy. Delivered as on-screen toasts only — never via a custom NPC.
/// <para>
/// A Dispatch phone contact used to send these as texts, but S1API could not finalize that NPC
/// (SetVisible NRE) and the half-built body flooded <c>UpdateUmbrellaUse</c> until the process died.
/// Toasts carry the full sentence; truncated UI is preferred over a game crash.
/// </para>
/// </summary>
internal static class PoliceMessages
{
    internal const string ModeToastAndText = "toast_and_text";
    internal const string ModeTextOnly = "text_only";

    private static string _lastDelivery = "none yet";
    private static string _lastBody = string.Empty;
    private static int _sent;

    /// <summary>Probe / menu read-out.</summary>
    internal static string LastDelivery => _lastDelivery;

    internal static string LastBody => _lastBody;

    internal static int Sent => _sent;

    /// <summary>Always true: there is no contact to wait for.</summary>
    internal static bool ContactReady => true;

    internal static void NoteReady()
    {
        _lastDelivery = $"toast channel ready ({DispatchContact.DisplayName})";
        PoliceLog.Msg(
            $"Police messages will come as on-screen toasts labelled '{DispatchContact.DisplayName}' " +
            "(no custom Dispatch NPC — that path crashed the game).");
    }

    /// <summary>
    /// Full announcement path. <paramref name="urgent"/> is reserved for call-site clarity; every
    /// announcement toasts when announcements are on.
    /// </summary>
    internal static void Announce(string title, string body, float toastSeconds = 8f, bool urgent = false)
    {
        if (string.IsNullOrWhiteSpace(body))
            return;

        if (!AnnouncementsOn())
            return;

        var text = body.Trim();
        var heading = string.IsNullOrWhiteSpace(title) ? DispatchContact.DisplayName : title.Trim();
        DeliverToast(heading, text, toastSeconds);
        _ = urgent;
    }

    /// <summary>Toast with the Dispatch heading.</summary>
    internal static void Message(string body)
    {
        if (string.IsNullOrWhiteSpace(body) || !AnnouncementsOn())
            return;

        DeliverToast(DispatchContact.DisplayName, body.Trim(), 10f);
    }

    /// <summary>
    /// Immediate toast. Prefer <see cref="Announce"/> for new call sites.
    /// </summary>
    internal static void Toast(string title, string body, float seconds = 8f, bool urgent = false) =>
        Announce(title, body, seconds, urgent);

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

    internal static void RaidWarning(string propertyName, string when, string reason)
    {
        // Always deliver — a silent raid is undefendable. Bypasses show_heat_hud.
        var body =
            $"{propertyName} in about {when}. {reason}. Be inside when they arrive or they take product from the containers.";
        DeliverToastForced("Raid inbound", body, 9f);
    }

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
        Announce("Booked in", sentence, toastSeconds: 10f, urgent: true);

    internal static void EarlyRelease(string body) =>
        Announce("They let you go early", body, toastSeconds: 7f, urgent: true);

    internal static void ShopRefused(string reason) =>
        Announce("They will not serve you", reason, toastSeconds: 8f, urgent: false);

    internal static string DescribeMode() =>
        "toast_only (no custom Dispatch NPC — phone text was retired because it crashed the game)";

    private static void DeliverToast(string title, string text, float toastSeconds)
    {
        if (!AnnouncementsOn())
            return;

        DeliverToastForced(title, text, toastSeconds);
    }

    /// <summary>Toast ignoring show_heat_hud (raid/custody critical path).</summary>
    private static void DeliverToastForced(string title, string text, float toastSeconds)
    {
        _lastBody = text;
        _sent++;
        _lastDelivery = $"toast #{_sent}: {title}";
        GameBridge.Notify(title, text, toastSeconds);
        PoliceLog.Detail($"Dispatch toast: {TrimForLog(text)}");
    }

    private static bool AnnouncementsOn() =>
        PoliceRuntime.Config is not { ShowHud.Value: false };

    private static string TrimForLog(string text) =>
        text.Length <= 160 ? text : text[..157] + "...";
}
