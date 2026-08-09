using Expansions.SpecialCustomers.Configuration;
using Expansions.SpecialCustomers.Game;
using Expansions.SpecialCustomers.Visitors;

namespace Expansions.SpecialCustomers.Visits;

/// <summary>
/// What happens when the player walks up to a visitor and presses the interact key.
/// <para>
/// Without this a group member has a greeting and nothing else: the dialogue canvas opens on a node
/// with no choices, and the only way out is Escape. That is worse than not being interactable at
/// all, so every member of a visiting group gets a real menu entry — the leader manages the bulk
/// order, everyone else sells on the spot through the shipped street-deal flow.
/// </para>
/// <para>
/// The entry is <b>appended</b> to the NPC's own choice list rather than overriding the container,
/// so the shipped customer choices — sample, deal, and above all "complete contract", which is how a
/// delivery is handed over — keep working alongside it.
/// </para>
/// </summary>
internal static class VisitorDialogue
{
    private const string LeaderContainer = "expansions_sc_leader_talk";
    private const string MemberContainer = "expansions_sc_member_talk";

    private const string ResendLabel = "expansions_sc_resend";
    private const string StatusLabel = "expansions_sc_status";
    private const string ClearLabel = "expansions_sc_clear";
    private const string BuyLabel = "expansions_sc_buy";
    private const string LeaderLabel = "expansions_sc_who";
    private const string LeaveLabel = "expansions_sc_leave";

    /// <summary>Above the shipped customer choices, because it is the reason the group is in town.</summary>
    private const int ChoicePriority = 20;

    private static readonly Dictionary<int, object> Attached = new();

    /// <summary>
    /// Keyed on the <c>NPCDialogue</c> instance, not the NPC id. S1API has no way to remove a single
    /// choice callback, and it builds a fresh wrapper for every save load — so identity is the only
    /// key that binds exactly once per live handler, whether or not the scene changed underneath.
    /// </summary>
    private static readonly HashSet<object> BoundDialogues = new(ReferenceEqualityComparer.Instance);

    private static readonly object Gate = new();

    internal static int AttachedCount
    {
        get
        {
            lock (Gate)
                return Attached.Count;
        }
    }

    internal static string LastFailure { get; private set; } = string.Empty;

    /// <summary>Gives every member of <paramref name="visit"/> something to say and something to do.</summary>
    internal static void Attach(Visit visit)
    {
        Detach();
        LastFailure = string.Empty;

        foreach (var slot in visit.Members)
        {
            if (!AttachSlot(visit, slot, out var failure))
            {
                LastFailure = failure;
                VisitorLog.Instance.Error(
                    $"FAILED to attach group dialogue for {slot.FullName}: {failure}. " +
                    "Talking to them will fall back to the plain civilian greeting. " +
                    (GameDialogue.LastAddFailure.Length > 0
                        ? $"AddChoice detail: {GameDialogue.LastAddFailure}."
                        : string.Empty));
            }
        }
    }

    /// <summary>True when this slot currently has a group dialogue choice on its controller.</summary>
    internal static bool IsAttached(int slotIndex)
    {
        lock (Gate)
            return Attached.ContainsKey(slotIndex);
    }

    /// <summary>Takes every entry back out, so a parked visitor is an ordinary background NPC again.</summary>
    internal static void Detach()
    {
        List<KeyValuePair<int, object>> attached;
        lock (Gate)
        {
            attached = Attached.ToList();
            Attached.Clear();
        }

        foreach (var pair in attached)
        {
            var slot = VisitorSlot.Find(pair.Key);
            if (slot is null)
                continue;

            var npc = GameNpc.Resolve(slot.Id, out _);
            if (npc is null)
                continue;

            if (!GameDialogue.RemoveChoice(npc, pair.Value, out var failure))
                VisitorLog.Instance.Debug($"Removing the group dialogue entry from slot {pair.Key:00}: {failure}");
        }
    }

    /// <summary>
    /// Drops references to the previous scene's dialogue objects. The choices themselves died with
    /// their controllers, so there is nothing to remove — only stale handles to let go of.
    /// </summary>
    internal static void OnSceneChanged()
    {
        lock (Gate)
        {
            Attached.Clear();
            BoundDialogues.Clear();
        }

        LastFailure = string.Empty;
    }

    private static bool AttachSlot(Visit visit, VisitorSlot slot, out string failure)
    {
        var wrapper = VisitorRuntime.Resolve(slot);
        if (wrapper is null)
        {
            failure = "they are not in the world";
            return false;
        }

        var npc = GameNpc.Resolve(slot.Id, out failure);
        if (npc is null)
            return false;

        var isLeader = slot.Index == visit.LeaderSlot;
        var containerName = isLeader ? LeaderContainer : MemberContainer;

        try
        {
            wrapper.Dialogue.BuildAndRegisterContainer(
                containerName,
                builder => Build(builder, visit, isLeader));
        }
        catch (Exception ex)
        {
            failure = Describe.Of(ex);
            return false;
        }

        BindCallbacks(slot, wrapper);

        var container = GameDialogue.FindContainer(npc, containerName, out failure);
        if (container is null)
            return false;

        var text = isLeader
            ? $"About the {visit.Archetype.ShortName} order"
            : $"Talk business ({visit.Archetype.ShortName})";

        var choice = GameDialogue.AddChoice(npc, text, container, ChoicePriority, out failure);
        if (choice is null)
            return false;

        lock (Gate)
            Attached[slot.Index] = choice;

        return true;
    }

    /// <summary>
    /// Outcomes are reported as a text from the NPC rather than as dialogue text: the node shown
    /// after a choice is baked into the container at build time, so it cannot carry a live reason,
    /// and a wrong reason is worse than no reason.
    /// </summary>
    private static void Build(S1API.Entities.Dialogue.DialogueContainerBuilder builder, Visit visit, bool isLeader)
    {
        builder.SetAllowExit(true);

        if (isLeader)
        {
            builder
                .AddNode("ENTRY", $"We're the {visit.Archetype.DisplayName}. We're buying in bulk while we're in town.", choices =>
                {
                    choices.Add(ResendLabel, "Send the order to my phone.", "resend");
                    choices.Add(StatusLabel, "Where are we with the order?", "status");
                    choices.Add(ClearLabel, "Something's stuck. Start over.", "clear");
                    choices.Add(LeaveLabel, "Later.", "leave");
                })
                .AddNode("resend", "Give me a second. I'll text you the numbers.")
                .AddNode("status", "I'll text you where we're at.")
                .AddNode("clear", "Fine. Forget the last one, I'll text you a fresh set of numbers.")
                .AddNode("leave", "We're not going anywhere yet.");

            return;
        }

        var canBuy = CustomerSettings.AllowWalkUpSales;

        builder
            .AddNode("ENTRY", $"Rolling with the {visit.Archetype.DisplayName}. What do you want?", choices =>
            {
                if (canBuy)
                    choices.Add(BuyLabel, "You buying?", "buy");

                choices.Add(LeaderLabel, "Who's running this?", "who");
                choices.Add(LeaveLabel, "Nothing.", "leave");
            })
            .AddNode("buy", "Show me what you've got.")
            .AddNode("who", $"{visit.Leader.FullName} handles the big orders. Take it up with them.")
            .AddNode("leave", "Suit yourself.");
    }

    /// <summary>
    /// Bound once per NPC per session and never unbound: S1API keys its callbacks by label with no
    /// per-label removal, so the bodies read live state and do nothing when no group is in town.
    /// </summary>
    private static void BindCallbacks(VisitorSlot slot, S1API.Entities.NPC wrapper)
    {
        var dialogue = wrapper.Dialogue;

        lock (Gate)
        {
            if (!BoundDialogues.Add(dialogue))
                return;
        }

        var index = slot.Index;

        try
        {
            dialogue
                .OnChoiceSelected(ResendLabel, () => Guard(index, "resending the order", s => Resend(s, force: false)))
                .OnChoiceSelected(ClearLabel, () => Guard(index, "clearing the order", s => Resend(s, force: true)))
                .OnChoiceSelected(StatusLabel, () => Guard(index, "reporting the order status", ReportStatus))
                .OnChoiceSelected(BuyLabel, () => Guard(index, "opening a walk-up deal", OpenWalkUpDeal));
        }
        catch (Exception ex)
        {
            lock (Gate)
                BoundDialogues.Remove(dialogue);

            VisitorLog.Instance.Warn(
                $"Could not bind the group dialogue callbacks for {slot.FullName} ({Describe.Of(ex)}); " +
                "their menu entry will open but do nothing.");
        }
    }

    private static void Guard(int slotIndex, string what, Action<VisitorSlot> body)
    {
        var slot = VisitorSlot.Find(slotIndex);
        if (slot is null)
            return;

        try
        {
            body(slot);
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Error($"Special Customers failed while {what} for {slot.FullName}.", ex);
            Reply(slot, "Something went wrong on our end. Try the Expansions menu.");
        }
    }

    private static void Resend(VisitorSlot slot, bool force)
    {
        var director = SpecialCustomersModule.Director;
        if (director?.Current is null)
        {
            Reply(slot, "We're not doing business right now.");
            return;
        }

        if (force && !director.ClearOfferState(out var clearMessage))
        {
            Reply(slot, clearMessage);
            return;
        }

        Reply(slot, director.ForceOffer(out var message) ? message : $"No deal: {message}");
    }

    private static void ReportStatus(VisitorSlot slot)
    {
        var director = SpecialCustomersModule.Director;
        if (director is null)
        {
            Reply(slot, "We're not doing business right now.");
            return;
        }

        Reply(slot, director.DescribeSaleLoop());
    }

    /// <summary>
    /// Hands off to the shipped street-deal flow: the customer asks the player for product, the
    /// handover screen opens, and the game's own pricing and satisfaction maths pay out. Nothing
    /// here fakes a sale.
    /// </summary>
    private static void OpenWalkUpDeal(VisitorSlot slot)
    {
        if (!CustomerSettings.AllowWalkUpSales)
        {
            Reply(slot, "I don't carry cash. Talk to whoever's running this.");
            return;
        }

        var wrapper = VisitorRuntime.Resolve(slot);
        var link = CustomerLink.Resolve(slot.Id, out var failure);
        if (wrapper is null || link is null)
        {
            Reply(slot, $"Can't do business right now ({failure}).");
            return;
        }

        if (link.PendingInstantDeal)
        {
            Reply(slot, "I already asked. Check your screen.");
            return;
        }

        var before = link.MinutesSinceInstantDeal;
        wrapper.Customer.RequestProduct();

        if (!link.WalkUpDealOpened(before))
        {
            Reply(slot,
                "Not right now — I bought too recently, or nothing you're carrying is what I'm after. " +
                $"Take it to {CurrentLeaderName(slot)} instead.");
        }
    }

    private static string CurrentLeaderName(VisitorSlot fallback) =>
        SpecialCustomersModule.Director?.Current?.Leader.FullName ?? fallback.FullName;

    /// <summary>
    /// A text from the NPC. The dialogue canvas is already closing by the time a choice callback
    /// runs, and the phone is where the rest of this feature reports anyway.
    /// </summary>
    private static void Reply(VisitorSlot slot, string text)
    {
        try
        {
            VisitorRuntime.Resolve(slot)?.SendTextMessage(text);
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"Could not text the player from slot {slot.Index:00} ({Describe.Of(ex)}).");
        }
    }
}
