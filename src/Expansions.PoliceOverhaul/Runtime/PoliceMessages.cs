using Expansions.PoliceOverhaul.State;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Player-facing police copy. Delivered as on-screen toasts — never via a custom NPC.
/// <para>
/// A Dispatch phone contact used to send these as texts, but S1API could not finalize that NPC
/// (SetVisible NRE) and the half-built body flooded <c>UpdateUmbrellaUse</c> until the process died.
/// Toasts carry the full sentence; truncated UI is preferred over a game crash.
/// </para>
/// <para>
/// Co-op: <see cref="NotificationsManager"/> is local-only and we have no custom RPCs, so every
/// announcement is routed through <see cref="AnnounceRelay"/> — local toast when this peer is the
/// audience, Steam lobby publish so a client peer can toast the same line on their machine.
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
        HostGate.Evaluate(out var peer);
        _lastDelivery =
            $"toast+lobby relay ready ({DispatchContact.DisplayName}); peer={peer}; route={AnnounceRelay.RouteInUse}";
        PoliceLog.Msg(
            $"Police messages: on-screen toasts labelled '{DispatchContact.DisplayName}'. " +
            "Co-op clients receive the same lines via the Steam lobby side-channel " +
            "(NotificationsManager cannot cross the wire; no custom RPCs). " +
            $"Peer role now: {peer}.");
    }

    /// <summary>
    /// Full announcement path. <paramref name="forPlayer"/> null = world (every peer).
    /// <paramref name="urgent"/> is reserved for call-site clarity.
    /// </summary>
    internal static void Announce(
        string title,
        string body,
        float toastSeconds = 8f,
        bool urgent = false,
        object? forPlayer = null)
    {
        if (string.IsNullOrWhiteSpace(body))
            return;

        if (!AnnouncementsOn())
            return;

        var text = body.Trim();
        var heading = string.IsNullOrWhiteSpace(title) ? DispatchContact.DisplayName : title.Trim();
        Deliver(heading, text, toastSeconds, TargetKey(forPlayer), bypassHudGate: false);
        _ = urgent;
    }

    /// <summary>World-level announcement — every peer (host toast + lobby for clients).</summary>
    internal static void AnnounceWorld(string title, string body, float toastSeconds = 8f, bool urgent = false) =>
        Announce(title, body, toastSeconds, urgent, forPlayer: null);

    /// <summary>Toast with the Dispatch heading for one player (or world when <paramref name="forPlayer"/> is null).</summary>
    internal static void Message(string body, object? forPlayer = null)
    {
        if (string.IsNullOrWhiteSpace(body) || !AnnouncementsOn())
            return;

        Deliver(DispatchContact.DisplayName, body.Trim(), 10f, TargetKey(forPlayer), bypassHudGate: false);
    }

    /// <summary>
    /// Immediate toast. Prefer <see cref="Announce"/> for new call sites.
    /// </summary>
    internal static void Toast(string title, string body, float seconds = 8f, bool urgent = false, object? forPlayer = null) =>
        Announce(title, body, seconds, urgent, forPlayer);

    internal static void HeatTierChanged(bool rising, string tierName, float heat, string meaning, object? forPlayer = null) =>
        Message(rising
            ? $"Police attention just stepped up to {tierName} (heat {heat:0}). {meaning}"
            : $"Police attention has eased to {tierName} (heat {heat:0}). {meaning}",
            forPlayer);

    /// <summary>
    /// Arrest wiped street heat. Outlaw tier is called out when still latched so the player does not
    /// think booking also laundered Marked/Hunted.
    /// </summary>
    internal static void ArrestClearedHeat(float previousHeat, OutlawTier outlaw, object? forPlayer = null)
    {
        var outlawNote = outlaw switch
        {
            OutlawTier.Hunted =>
                " Your Hunted flag is still on file — booking clears heat, not the record. Clean days or the legal fee clear that.",
            OutlawTier.Marked =>
                " You are still Marked — arrest wiped the heat score, not the outlaw latch. Clean days or the legal fee clear that.",
            _ => string.Empty,
        };

        Message(
            previousHeat > 0.5f
                ? $"Arrest processed. Your heat was wiped from {previousHeat:0} back to 0 — street pressure resets when you pay the price.{outlawNote}"
                : $"Arrest processed. Heat stays at 0.{outlawNote}",
            forPlayer);
    }

    internal static void OutlawChanged(OutlawTier tier, object? forPlayer = null)
    {
        Message(tier switch
        {
            OutlawTier.Hunted =>
                "Your file just went Hunted. Every officer recognises you on sight, searches always turn something up, " +
                "pursuits will not time out, card-only shops will not serve you, and your dealers are taking a hazard cut. " +
                $"Clear it with clean days or knock on the police station door and pay the ${PoliceRuntime.Config?.LegalFeeHunted.Value:N0} legal fee.",
            OutlawTier.Marked =>
                "You are Marked. Officers treat you as always-suspicious, body searches will find contraband, " +
                "and pursuits stick around longer. Three clean days in a row (or the legal fee at the station door) drops you back toward Clean. " +
                $"Knock on the police station door to pay ${PoliceRuntime.Config?.LegalFeeMarked.Value:N0}. " +
                "Stay Marked long enough and a second federal encounter or a second arrest promotes you to Hunted.",
            _ =>
                "Your outlaw flag is cleared. Fines, searches and shops are back to the normal heat rules — " +
                "but the heat score itself is unchanged, so the street presence still matches how wanted you are.",
        }, forPlayer);
    }

    internal static void FederalBegan(bool stakeout, string propertyName, string trigger, int agents, int hours, object? forPlayer = null) =>
        Message(stakeout
            ? $"A plain-clothes federal team ({agents}) is sitting on {propertyName} for about {hours} in-game hour(s). " +
              $"Reason logged: {trigger}. They are not local PD — walking past them while Marked or Hunted is enough for a search. " +
              "Leaving the property does not end the watch; they hold the post until the assignment expires."
            : $"A plain-clothes federal team ({agents}) is in town asking about you for about {hours} in-game hour(s). " +
              $"Reason logged: {trigger}. They pursue on foot and will not call the whole local force in behind them. " +
              "Survive the window without getting bagged and the encounter still counts toward outlaw pressure.",
            forPlayer);

    internal static void FederalEnded(string reason, object? forPlayer = null) =>
        Message($"The federal team has pulled out ({reason}). The cooldown on the next visit has started. " +
                "Your heat and outlaw status are unchanged by the withdrawal itself.",
            forPlayer);

    internal static void RaidWarning(string propertyName, string when, string reason)
    {
        // Always deliver — a silent raid is undefendable. Bypasses show_heat_hud. World audience:
        // owned properties are shared in co-op.
        var body =
            $"{propertyName} in about {when}. {reason}. Be inside when they arrive or they take product from the containers.";
        Deliver("Raid inbound", body, 9f, AnnounceRelay.WorldTarget, bypassHudGate: true);
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

    internal static void FineSettled(float cash, float bank, object? forPlayer = null) =>
        Message(bank > 0.5f
            ? $"Court penalties hit: ${cash:0} taken from cash on hand, and ${bank:0} pulled from your online / bank balance " +
              "as unpaid fine debt. That bank hit is permanent play history — toggling the mod off will not refund it."
            : $"Court penalties hit: ${cash:0} taken from cash on hand. Your bank balance was not touched this time.",
            forPlayer);

    internal static void LegalFeePaid(string summary, object? forPlayer = null) => Message(summary, forPlayer);

    internal static void InformantFallout(string names, object? forPlayer = null) =>
        Message($"{names} is who called it in. Their relationship with you just dropped for making that call. " +
                "This is not a federal tip — it is a civilian who dialled local PD.",
            forPlayer);

    internal static void EquipmentSeized(string names, object? forPlayer = null) =>
        Message($"On top of the product, they kept your kit: {names}. Seeds, soil, furniture and lighting are never taken.",
            forPlayer);

    internal static void VehicleSearched(int stacks, object? forPlayer = null) =>
        Message($"The vehicle you were arrested next to was searched. {stacks} stack(s) of product were seized from its storage. " +
                "Vanilla leaves vehicle cargo alone — this module does not.",
            forPlayer);

    internal static void BookedIn(string sentence, object? forPlayer = null) =>
        Announce("Booked in", sentence, toastSeconds: 10f, urgent: true, forPlayer);

    internal static void EarlyRelease(string body, object? forPlayer = null) =>
        Announce("They let you go early", body, toastSeconds: 7f, urgent: true, forPlayer);

    internal static void ShopRefused(string reason, object? forPlayer = null) =>
        Announce("They will not serve you", reason, toastSeconds: 8f, urgent: false, forPlayer);

    internal static string DescribeMode() =>
        "toast + steam-lobby relay (no custom Dispatch NPC; no custom RPCs)";

    /// <summary>
    /// Player key for relay targeting. Null player ⇒ world audience (every peer).
    /// </summary>
    internal static string TargetKey(object? player) =>
        player is null ? AnnounceRelay.WorldTarget : GameBridge.KeyFor(player);

    /// <summary>Resolve a heat-record key back to a live player object when possible.</summary>
    internal static object? PlayerForRecord(PlayerHeatRecord record)
    {
        foreach (var player in GameBridge.Players())
        {
            if (player is not null &&
                string.Equals(GameBridge.KeyFor(player), record.PlayerKey, StringComparison.Ordinal))
                return player;
        }

        return GameBridge.LocalPlayer();
    }

    private static void Deliver(string title, string text, float toastSeconds, string targetKey, bool bypassHudGate)
    {
        if (!bypassHudGate && !AnnouncementsOn())
            return;

        _lastBody = text;
        _sent++;
        AnnounceRelay.Deliver(title, text, toastSeconds, targetKey, bypassHudGate);
        _lastDelivery =
            $"#{_sent}: {title} → {targetKey} via {AnnounceRelay.RouteInUse} ({AnnounceRelay.PeerRole})";
        PoliceLog.Detail($"Dispatch toast: [{targetKey}] {TrimForLog(text)}");
    }

    private static bool AnnouncementsOn() =>
        PoliceRuntime.Config is not { ShowHud.Value: false };

    private static string TrimForLog(string text) =>
        text.Length <= 160 ? text : text[..157] + "...";
}
