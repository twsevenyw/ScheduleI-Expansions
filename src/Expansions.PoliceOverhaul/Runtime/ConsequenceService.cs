using Expansions.Core.Diagnostics;
using Expansions.PoliceOverhaul.State;
using S1API.Money;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Makes getting caught cost something.
/// <para>
/// The shipped arrest is close to free: fines run $5-$150 a charge, cash only, with no debt if you
/// cannot pay — so carrying no cash makes an arrest cost nothing but the stash in your pockets, and
/// vehicle cargo is not touched at all. This closes both holes, and converts the shortfall into an
/// online-balance debit, which is the one currency the shipped economy actually rations
/// ($10,000/week of deposits, $20,000/24h of laundering).
/// </para>
/// </summary>
internal sealed class ConsequenceService
{
    private readonly PoliceConfig _config;
    private readonly HeatDirector _heat;
    private readonly OutlawState _outlaw;

    /// <summary>Charges recorded since the last release, keyed by player. The game clears its own on release.</summary>
    private readonly Dictionary<string, Dictionary<string, int>> _pending = new(StringComparer.Ordinal);

    private float _cashBeforeNotice = float.NaN;

    internal ConsequenceService(PoliceConfig config, HeatDirector heat, OutlawState outlaw)
    {
        _config = config;
        _heat = heat;
        _outlaw = outlaw;
    }

    /// <summary>
    /// Whether the game charges its own fine, learned by watching the cash balance across the arrest
    /// notice. Unknown until the first arrest; see <see cref="ChargeAtNoticeClose"/> for why that
    /// matters more than it sounds.
    /// </summary>
    internal bool? GameChargesItsOwnFine { get; private set; }

    internal float LastFineCharged { get; private set; }

    internal float LastDebtRaised { get; private set; }

    internal void Track(object? player, string crimeClassName, int quantity)
    {
        if (crimeClassName.Length == 0)
            return;

        var key = GameBridge.KeyFor(player);
        if (!_pending.TryGetValue(key, out var charges))
        {
            charges = new Dictionary<string, int>(StringComparer.Ordinal);
            _pending[key] = charges;
        }

        charges[crimeClassName] = charges.TryGetValue(crimeClassName, out var existing)
            ? existing + Math.Max(1, quantity)
            : Math.Max(1, quantity);
    }

    internal void ClearCharges(object? player) => _pending.Remove(GameBridge.KeyFor(player));

    internal void ClearAllCharges() => _pending.Clear();

    /// <summary>The multiplier the notice should show, and the one the charge uses.</summary>
    internal float MultiplierFor(object? player)
    {
        var tierMultiplier = HeatModel.FineMultiplier(_heat.RecordFor(player).Tier, Math.Max(0f, _config.IntensityScalar.Value));
        var outlawMultiplier = _outlaw.IsOutlawed(player) ? Math.Max(1f, _config.OutlawFineMultiplier.Value) : 1f;
        return tierMultiplier * outlawMultiplier;
    }

    internal float BaseFineFor(object? player)
    {
        if (!_pending.TryGetValue(GameBridge.KeyFor(player), out var charges))
            return 0f;

        var total = 0f;
        foreach (var (crime, quantity) in charges)
            total += PenaltyTable.FineFor(crime) * quantity;

        return total;
    }

    /// <summary>
    /// Widens confiscation to the top stealth tier while outlawed, by rewriting the argument in
    /// place. Advanced packaging is the game's own "the search will not find this" answer; taking it
    /// away is what makes an outlaw arrest hurt rather than annoy.
    /// </summary>
    internal void WidenStealthArgument(object?[] arguments)
    {
        if (!_config.EnableConsequences.Value || arguments.Length == 0)
            return;

        if (!_outlaw.IsOutlawed(GameBridge.LocalPlayer()))
            return;

        var advanced = GameBridge.BoxEnum(GameTypes.EStealthLevel, 2);
        if (advanced is not null)
            arguments[0] = advanced;
    }

    /// <summary>Adds our surcharge to the notice so the number the player reads is the number they pay.</summary>
    internal void AnnotatePenaltyList(object? penaltyList)
    {
        if (!_config.EnableConsequences.Value || penaltyList is null)
            return;

        var player = GameBridge.LocalPlayer();
        var baseFine = BaseFineFor(player);
        var multiplier = MultiplierFor(player);
        if (baseFine <= 0f || multiplier <= 1.001f)
            return;

        var surcharge = baseFine * (multiplier - 1f);
        var reason = _outlaw.IsOutlawed(player) ? "outlaw status" : "prior record";
        GameBridge.AddToIl2CppList(penaltyList, $"${surcharge:0} additional penalty ({reason})");
    }

    internal void NoticeOpening() => _cashBeforeNotice = SafeCash();

    /// <summary>
    /// Charges the difference between what the player owes and what the game already took.
    /// <para>
    /// Whether vanilla deducts a fine at all was never verified from metadata — no <c>MoneyManager</c>
    /// call site appears near <c>PenaltyHandler</c>. Rather than guess and risk double-billing, this
    /// measures the cash balance across the notice and charges only the shortfall. If the game does
    /// charge, we top up; if it does not, we charge the whole amount. Either way the player pays
    /// exactly once, and the answer is logged the first time so it stops being an unknown.
    /// </para>
    /// </summary>
    internal void ChargeAtNoticeClose()
    {
        if (!_config.EnableConsequences.Value)
            return;

        var player = GameBridge.LocalPlayer();
        var record = _heat.RecordFor(player);
        var baseFine = BaseFineFor(player);
        var owed = baseFine * MultiplierFor(player);

        var cashNow = SafeCash();
        if (float.IsNaN(cashNow))
            cashNow = 0f;

        var alreadyTaken = float.IsNaN(_cashBeforeNotice) ? 0f : Math.Max(0f, _cashBeforeNotice - cashNow);
        _cashBeforeNotice = float.NaN;

        if (GameChargesItsOwnFine is null && baseFine > 0f)
        {
            GameChargesItsOwnFine = alreadyTaken > 0.5f;
            PoliceLog.Msg(GameChargesItsOwnFine.Value
                ? $"The base game charges its own fine (${alreadyTaken:0} taken during the arrest notice); this module only adds the surcharge."
                : "The base game charged nothing during the arrest notice; this module charges the whole penalty.");
        }

        var outstanding = owed - alreadyTaken;
        LastFineCharged = 0f;
        LastDebtRaised = 0f;

        if (outstanding <= 0.5f)
            return;

        var paid = Math.Min(cashNow, outstanding);
        if (paid > 0f)
        {
            Money.ChangeCashBalance(-paid, true, true);
            LastFineCharged = paid;
        }

        var shortfall = outstanding - paid;
        if (shortfall > 0.5f && _config.EnableDebt.Value)
        {
            Money.CreateOnlineTransaction("Court fine", -shortfall, 1f, "Unpaid penalties");
            LastDebtRaised = shortfall;
        }

        _heat.AddPoliceTake(player, alreadyTaken + paid + LastDebtRaised);

        if (_config.ShowHud.Value && (LastFineCharged > 0f || LastDebtRaised > 0f))
        {
            GameBridge.Notify(
                "Penalties applied",
                LastDebtRaised > 0f
                    ? $"${LastFineCharged:0} in cash and ${LastDebtRaised:0} taken from your bank balance."
                    : $"${LastFineCharged:0} taken in cash.",
                7f);
        }

        PoliceLog.Msg(
            $"Arrest settled: base ${baseFine:0} x{MultiplierFor(player):0.00} = ${owed:0}; " +
            $"game took ${alreadyTaken:0}, we took ${LastFineCharged:0} cash and ${LastDebtRaised:0} debt. " +
            $"Lifetime police take ${record.PoliceTakeTotal:0}.");
    }

    /// <summary>
    /// Empties the product out of the vehicle you were arrested next to. The arrest notice already
    /// holds that vehicle, so there is no searching to do and no chance of hitting the wrong car.
    /// Only <c>Product</c> is taken: clearing the whole storage would take legitimate cargo and read
    /// as a bug.
    /// </summary>
    internal int ConfiscateVehicleCargo(object? arrestNoticeScreen)
    {
        if (!_config.EnableConsequences.Value || !_config.ConfiscateVehicleCargo.Value)
            return 0;

        var storage = Members.ReadPath(arrestNoticeScreen, "vehicle.Storage");
        if (storage is null)
            return 0;

        if (!GameReflection.TryRead(storage, "ItemSlots", out var slots, out _) || slots is null)
            return 0;

        var entries = GameReflection.Enumerate(slots, 64);
        var taken = 0;

        for (var i = 0; i < entries.Count; i++)
        {
            var instance = Members.ReadPath(entries[i], "ItemInstance");
            if (instance is null || !IsProduct(instance))
                continue;

            // conn: null is the server-side path; SetStoredInstance is a ServerRpc whose logic body
            // runs locally when we are the server, which the host gate has already guaranteed.
            if (Members.Invoke(storage, "SetStoredInstance", null, i, null))
                taken++;
        }

        if (taken > 0)
        {
            Members.Invoke(storage, "ContentsChanged");
            PoliceLog.Msg($"Seized {taken} stack(s) of product from the impounded vehicle.");

            if (_config.ShowHud.Value)
                GameBridge.Notify("Vehicle searched", $"{taken} stack(s) of product seized from your vehicle.", 7f);
        }

        return taken;
    }

    private static bool IsProduct(object instance)
    {
        var category = Members.ReadPath(instance, "Definition.Category");
        return category is not null && string.Equals(category.ToString(), "Product", StringComparison.Ordinal);
    }

    private static float SafeCash()
    {
        try
        {
            return Money.GetCashBalance();
        }
        catch
        {
            return float.NaN;
        }
    }
}
