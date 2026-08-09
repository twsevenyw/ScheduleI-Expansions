using Expansions.Core.Diagnostics;
using Expansions.PoliceOverhaul.State;
using S1API.Money;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// The latched Clean / Marked / Hunted status. Heat is the pressure; this is the phase change.
/// <para>
/// Outlaw deliberately changes <em>rules</em> rather than the world. Almost everything it does lives
/// in a Harmony patch that reads <see cref="TierFor"/> and is removed wholesale by
/// <c>UnpatchSelf()</c>; the one piece of real state it owns is a visibility label on the player,
/// which is why that label is namespaced and why <see cref="Revert"/> exists.
/// </para>
/// </summary>
internal sealed class OutlawState
{
    /// <summary>
    /// Namespaced so it can never be confused with one of the game's own visual states.
    /// <c>EntityVisibility</c> keys states by this string, so removal is exact.
    /// </summary>
    private const string VisibilityLabel = "Expansions.Police.Outlaw";

    private readonly PoliceConfig _config;
    private readonly Func<string, PlayerHeatRecord?> _lookup;
    private readonly HashSet<string> _labelled = new(StringComparer.Ordinal);

    internal OutlawState(PoliceConfig config, Func<string, PlayerHeatRecord?> lookup)
    {
        _config = config;
        _lookup = lookup;
        Economy = new OutlawEconomy(config);
    }

    /// <summary>The business-side half of the status: dealer cuts, snitch chances, card vendors.</summary>
    internal OutlawEconomy Economy { get; }

    /// <summary>True when any player is outlawed — the cheap gate the patch bodies test first.</summary>
    internal bool AnyoneOutlawed { get; private set; }

    internal OutlawTier TierFor(object? player) =>
        !_config.EnableOutlaw.Value ? OutlawTier.Clean : _lookup(GameBridge.KeyFor(player))?.Outlaw ?? OutlawTier.Clean;

    internal bool IsOutlawed(object? player) => TierFor(player) != OutlawTier.Clean;

    /// <summary>
    /// Promotes or clears a record and reports whether anything moved, so the caller owns the
    /// announcement. Latching means heat falling back below the threshold does not clear the status.
    /// </summary>
    internal bool Evaluate(PlayerHeatRecord record, out OutlawTier previous)
    {
        previous = record.Outlaw;

        if (!_config.EnableOutlaw.Value)
            return false;

        if (record.Outlaw == OutlawTier.Clean && record.Heat >= _config.OutlawHeatThreshold.Value)
        {
            record.Outlaw = OutlawTier.Marked;
            record.ArrestsWhileOutlaw = 0;
            record.OutlawPromotions++;
            return true;
        }

        if (record.Outlaw == OutlawTier.Marked && (record.FederalEncounters >= 2 || record.ArrestsWhileOutlaw >= 2))
        {
            record.Outlaw = OutlawTier.Hunted;
            record.ArrestsWhileOutlaw = 0;
            record.OutlawPromotions++;
            return true;
        }

        if (record.Outlaw != OutlawTier.Clean && record.CleanDayStreak >= _config.OutlawClearDays.Value)
        {
            Demote(record);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Drops one tier and pulls heat back under the latch threshold, so clearing a status does not
    /// immediately re-latch on the next tick.
    /// </summary>
    internal void Demote(PlayerHeatRecord record)
    {
        if (record.Outlaw == OutlawTier.Clean)
            return;

        record.Outlaw = record.Outlaw == OutlawTier.Hunted ? OutlawTier.Marked : OutlawTier.Clean;
        record.CleanDayStreak = 0;
        record.ArrestsWhileOutlaw = 0;
        record.FederalEncounters = 0;
        record.OutlawTiersCleared++;
        record.Heat = Math.Min(record.Heat, _config.OutlawHeatThreshold.Value - 1);
    }

    internal void Promote(PlayerHeatRecord record, OutlawTier tier)
    {
        if (tier == record.Outlaw)
            return;

        record.Outlaw = tier;
        record.ArrestsWhileOutlaw = 0;
        record.CleanDayStreak = 0;

        if (tier != OutlawTier.Clean)
            record.OutlawPromotions++;
    }

    /// <summary>
    /// Pushes the "every cop knows you on sight" label onto outlawed players and takes it off
    /// everyone else. Runs on the minute tick because the label is runtime-only: death, respawn and
    /// save load all drop it, and re-applying is cheaper than detecting each of those.
    /// </summary>
    internal void Sync()
    {
        var anyone = false;

        foreach (var player in GameBridge.Players())
        {
            if (player is null)
                continue;

            var key = GameBridge.KeyFor(player);
            var outlawed = _config.EnableOutlaw.Value && (_lookup(key)?.Outlaw ?? OutlawTier.Clean) != OutlawTier.Clean;
            anyone |= outlawed;

            if (outlawed)
            {
                if (_labelled.Add(key))
                    PoliceLog.Detail($"Outlaw visibility applied to '{key}'.");

                ApplyLabel(player);
            }
            else if (_labelled.Remove(key))
            {
                RemoveLabel(player);
            }
        }

        AnyoneOutlawed = anyone;
        Economy.Sync(anyone);
    }

    /// <summary>
    /// The "buy your way out" lane. Cash first, then the bank, because refusing a rich player over
    /// where the money is sitting would be an accounting rule pretending to be a design one.
    /// Returns false with a reason the caller shows verbatim.
    /// </summary>
    internal bool PayLegalFee(PlayerHeatRecord record, out string message)
    {
        if (!_config.EnableOutlaw.Value)
        {
            message = "Outlaw status is switched off in this module's settings (enable_outlaw), so there is nothing to buy down.";
            return false;
        }

        if (record.Outlaw == OutlawTier.Clean)
        {
            message = "Your record is already clean. There is nothing for a lawyer to do.";
            return false;
        }

        var fee = _config.FeeFor(record.Outlaw);
        var cash = Wallet.Cash();
        var bank = Wallet.Online();

        if (cash + bank < fee)
        {
            message = $"Clearing {Describe(record.Outlaw)} costs ${fee:N0} and you have ${cash + bank:N0} between your pockets and the bank. " +
                      "Come back with the money, knock on the police station door again, or serve the clean days.";
            return false;
        }

        var fromCash = Math.Min(cash, fee);
        if (fromCash > 0f)
            Money.ChangeCashBalance(-fromCash, true, true);

        var fromBank = fee - fromCash;
        if (fromBank > 0.5f)
            Money.CreateOnlineTransaction("Legal fees", -fromBank, 1f, "Representation");

        var previous = record.Outlaw;
        Demote(record);
        record.LegalFeesPaid += fee;
        Sync();

        message = $"Legal fee paid at the station: ${fee:N0}. {Describe(previous)} down to {Describe(record.Outlaw)}. " +
                  (fromBank > 0.5f ? $"${fromCash:N0} in cash and ${fromBank:N0} off your bank balance." : "Paid in cash.");

        PoliceLog.Msg($"Legal fee paid: ${fee} ({Describe(previous)} -> {Describe(record.Outlaw)}).");

        if (_config.ShowHud.Value)
            PoliceMessages.LegalFeePaid(message);

        return true;
    }

    /// <summary>Removes every label this module applied. Safe from a half-initialised state.</summary>
    internal void Revert()
    {
        Economy.Revert();

        if (_labelled.Count == 0)
        {
            AnyoneOutlawed = false;
            return;
        }

        foreach (var player in GameBridge.Players())
        {
            if (player is not null)
                RemoveLabel(player);
        }

        PoliceLog.Msg($"Cleared the outlaw visibility label from {_labelled.Count} player(s).");
        _labelled.Clear();
        AnyoneOutlawed = false;
    }

    internal static string Describe(OutlawTier tier) => tier switch
    {
        OutlawTier.Hunted => "HUNTED",
        OutlawTier.Marked => "MARKED",
        _ => "CLEAN",
    };

    /// <summary>
    /// Idempotent by design. Visibility states are runtime-only — death, respawn and save load all
    /// drop them — so this is called on every tick and asks the component whether the label is still
    /// there rather than tracking that ourselves and getting it wrong.
    /// </summary>
    private static void ApplyLabel(object player)
    {
        var visibility = Members.ReadPath(player, "VisibilityComponent");
        if (visibility is null)
            return;

        if (Members.InvokeFor(visibility, "GetState", VisibilityLabel) is not null)
            return;

        // Boxed through the live enum type so a re-ordered enum still resolves by name rather than
        // by our idea of its ordinal.
        var wanted = WantedState();
        if (wanted is null)
            return;

        Members.Invoke(visibility, "ApplyState", VisibilityLabel, wanted, 0f);
    }

    private static void RemoveLabel(object player)
    {
        var visibility = Members.ReadPath(player, "VisibilityComponent");
        if (visibility is not null)
            Members.Invoke(visibility, "RemoveState", VisibilityLabel, 0f);
    }

    private static object? WantedState()
    {
        var type = GameReflection.FindType(GameTypes.EVisualState);
        if (type is null)
            return null;

        try
        {
            return Enum.Parse(type, "Wanted");
        }
        catch
        {
            return null;
        }
    }
}
