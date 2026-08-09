using Expansions.Core.Diagnostics;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// What being a known outlaw costs you in business rather than in police attention.
/// <para>
/// Three effects, all of them a change to a number the shipped economy already reads every day:
/// dealers charge a hazard premium on top of their flat cut, customers are readier to pick up the
/// phone, and the two vendors who only take card stop serving someone whose name is on a warrant.
/// Cash trade keeps working — you are an outlaw, not bankrupt.
/// </para>
/// <para>
/// Every write is snapshotted first and written back by <see cref="Revert"/>, which runs on leaving
/// outlaw status, on module disable <em>and</em> on scene unload. That last one matters more than it
/// looks: dealer and customer data are assets rather than scene objects, so they survive a scene
/// change and a value left raised here would follow the player into their next save.
/// </para>
/// </summary>
internal sealed class OutlawEconomy
{
    private readonly PoliceConfig _config;

    /// <summary>
    /// Game minutes between full re-scans once the effects are on. The first pass covers everyone who
    /// exists; after that the only thing a re-scan finds is a dealer recruited or a customer unlocked
    /// since, which is not worth walking a hundred customers a second for.
    /// </summary>
    private const int RescanEveryMinutes = 30;

    private readonly Dictionary<IntPtr, Snapshot> _dealerCuts = new();
    private readonly Dictionary<IntPtr, Snapshot> _snitchChances = new();

    private bool _applied;
    private int _minutesSinceScan;

    internal OutlawEconomy(PoliceConfig config) => _config = config;

    internal bool IsApplied => _applied;

    internal int DealersAffected => _dealerCuts.Count;

    internal int CustomersAffected => _snitchChances.Count;

    /// <summary>
    /// Card-only vendors that should turn an outlaw away. Read as a live predicate rather than a
    /// baked list, so a shop added by a future patch is covered without a code change.
    /// </summary>
    internal bool ShouldRefuseShop(object? shop)
    {
        if (!_applied || !_config.OutlawBlocksCardVendors.Value || shop is null)
            return false;

        // EPaymentType.Online == 1. A shop that will take cash stays open: the point is that the
        // paperwork trail is closed to you, not that nobody will serve you.
        return Members.Read<object?>(shop, "PaymentType", null) is { } payment && Convert.ToInt32(payment) == 1;
    }

    internal string RefusalFor(object? shop) =>
        $"{Members.Read(shop, "ShopName", "This business")} does not do card business with people who have a warrant out. " +
        "Clear your outlaw status, or find somewhere that takes cash.";

    /// <summary>
    /// Brings the world into line with whether anyone is outlawed. Idempotent and cheap when nothing
    /// changed, so it can sit on the minute tick and pick up dealers recruited since last time.
    /// </summary>
    internal void Sync(bool anyoneOutlawed)
    {
        if (!_config.EnableOutlaw.Value || !_config.EnableConsequences.Value)
        {
            if (_applied)
                Revert();

            return;
        }

        if (!anyoneOutlawed)
        {
            if (_applied)
                Revert();

            return;
        }

        if (_applied && ++_minutesSinceScan < RescanEveryMinutes)
            return;

        _applied = true;
        _minutesSinceScan = 0;
        ApplyDealerCuts();
        ApplySnitchChances();
    }

    /// <summary>Writes every touched number back. Safe from a half-applied state and safe to repeat.</summary>
    internal void Revert()
    {
        var dealers = Restore(_dealerCuts, "SalesCutPercentage");
        var customers = Restore(_snitchChances, "CallPoliceChance");

        if (_applied && (dealers > 0 || customers > 0))
            PoliceLog.Msg($"Outlaw economy reverted: {dealers} dealer cut(s) and {customers} customer snitch chance(s) restored.");

        _applied = false;
        _minutesSinceScan = 0;
    }

    // ── Effects ───────────────────────────────────────────────────────────────────────────────

    private void ApplyDealerCuts()
    {
        var bonus = Math.Max(0f, _config.OutlawDealerCutBonus.Value);
        if (bonus <= 0f)
            return;

        var type = GameReflection.FindType(GameTypes.Dealer);
        if (type is null || !GameReflection.TryReadStatic(type, "AllPlayerDealers", out var dealers, out _))
            return;

        foreach (var dealer in GameReflection.Enumerate(dealers, 32))
        {
            if (!GameReflection.IsPresent(dealer) || Members.Read(dealer, "IsRecruited", false) is false)
                continue;

            // The cut lives on the dealer's runtime NPCData, which is a per-NPC deep copy of the
            // designer asset — so this raises one dealer's take, not every dealer of that archetype.
            // NPC.NPCData is declared as the base NPCData, so the wrapper has to be re-read as the
            // dealer subclass or SalesCutPercentage is invisible to reflection.
            var data = Components.Reinterpret(Members.ReadPath(dealer, "NPCData"), GameTypes.DealerNPCData);
            if (data is null)
            {
                PoliceLog.Detail($"'{Members.Read(dealer, "FullName", "a dealer")}' has no DealerNPCData; its cut is left alone.");
                continue;
            }

            Raise(_dealerCuts, data, "SalesCutPercentage", bonus, 0.95f);
        }
    }

    private void ApplySnitchChances()
    {
        var bonus = Math.Max(0f, _config.OutlawSnitchBonus.Value);
        if (bonus <= 0f)
            return;

        var type = GameReflection.FindType(GameTypes.Customer);
        if (type is null || !GameReflection.TryReadStatic(type, "UnlockedCustomers", out var customers, out _))
            return;

        foreach (var customer in GameReflection.Enumerate(customers, 256))
        {
            if (!GameReflection.IsPresent(customer))
                continue;

            var data = Members.ReadPath(customer, "CustomerData");
            Raise(_snitchChances, data, "CallPoliceChance", bonus, 1f);
        }
    }

    /// <summary>
    /// Raises one float by a fixed amount, remembering the vanilla value the first time. Writing the
    /// absolute snapshot-plus-bonus rather than adding on each pass is what makes the minute tick
    /// idempotent — an additive write would compound to the cap within a minute.
    /// </summary>
    private static void Raise(Dictionary<IntPtr, Snapshot> snapshots, object? target, string member, float bonus, float ceiling)
    {
        if (!GameReflection.IsPresent(target) || target is null)
            return;

        var pointer = Components.PointerOf(target);
        if (pointer == IntPtr.Zero)
            return;

        if (!snapshots.TryGetValue(pointer, out var snapshot))
        {
            if (!GameReflection.TryRead(target, member, out var raw, out _) || raw is not float original)
                return;

            snapshot = new Snapshot(target, original);
            snapshots[pointer] = snapshot;
        }

        var wanted = Math.Min(snapshot.Original + bonus, ceiling);
        if (Math.Abs(Members.Read(target, member, wanted) - wanted) > 0.0001f)
            Members.TryWrite(target, member, wanted);
    }

    private static int Restore(Dictionary<IntPtr, Snapshot> snapshots, string member)
    {
        var restored = 0;

        foreach (var snapshot in snapshots.Values)
        {
            // The target is an asset, not a scene object, so it normally outlives the scene — but a
            // reload can still have collected it, and writing into a dead native is not survivable.
            if (!GameReflection.IsPresent(snapshot.Target))
                continue;

            if (Members.TryWrite(snapshot.Target, member, snapshot.Original))
                restored++;
        }

        snapshots.Clear();
        return restored;
    }

    private readonly struct Snapshot
    {
        internal Snapshot(object target, float original)
        {
            Target = target;
            Original = original;
        }

        internal object Target { get; }

        internal float Original { get; }
    }
}
