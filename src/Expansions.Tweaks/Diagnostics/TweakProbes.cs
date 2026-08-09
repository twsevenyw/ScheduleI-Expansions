using Expansions.Core.Diagnostics;
using Expansions.Tweaks.Patches;
using Expansions.Tweaks.Runtime;

namespace Expansions.Tweaks.Diagnostics;

/// <summary>
/// One probe per tweak, each answering the same question: is this actually doing anything?
/// <para>
/// Every one of the three works by writing a value the game already reads, which is exactly the kind
/// of change that can silently fail — a renamed field, a settings asset that never resolved, a
/// station that woke up after the sweep. So none of these probes report "the module is enabled".
/// They read the live value back out of the game and compare it to the vanilla figure, and the
/// verdict is that comparison rather than our own bookkeeping.
/// </para>
/// </summary>
internal static class TweakProbes
{
    private const string Area = "Quality of Life";

    internal static IEnumerable<IProbe> All()
    {
        yield return new DelegateProbe(
            "tweaks.mixing_speed",
            "Is mixing actually running faster?",
            Area,
            Mixing);

        yield return new DelegateProbe(
            "tweaks.deposit_limit",
            "What weekly ATM deposit ceiling is in force?",
            Area,
            Deposit);

        yield return new DelegateProbe(
            "tweaks.delivery_speed",
            "Are deliveries actually arriving sooner?",
            Area,
            Delivery);
    }

    private static void Mixing(ProbeContext context, ProbeResult result)
    {
        if (TweaksRuntime.Config is not { } config || TweaksRuntime.Mixing is not { } mixing)
        {
            result.Inconclusive("Quality of Life is switched off, so mixing is vanilla by definition.");
            return;
        }

        var wanted = config.ResolvedMixMultiplier;

        result.Fact("Configured multiplier", $"x{config.MixSpeedMultiplier.Value:0.##}" +
                                             (config.EnableFasterMixing.Value ? string.Empty : " (tweak switched off)"));
        result.Fact("Multiplier in force", $"x{wanted:0.##}");
        result.Fact("Applied multiplier", $"x{mixing.Multiplier:0.##}");
        result.Fact("Stations scaled", mixing.StationsScaled.ToString());
        result.Fact($"'{GameTypes.MixingStation}'", mixing.TypeResolved ? "resolved" : "NOT FOUND on this build");
        result.Fact("Mk2 station type", GameReflection.TypeExists(GameTypes.MixingStationMk2) ? "resolved" : "not on this build");
        result.Fact("Chemist mixing", config.MixSpeedAppliesToEmployees.Value
            ? "sped up as well"
            : $"excluded (best-effort; the exclusion patch has been reached {TweakPatches.MixTimeCalls} time(s))");

        Patches(result, "MixingStation.Awake", "MixingStationMk2.Awake", "MixingStation.MixingStart");

        result.Table(
            new[] { "Station", "Vanilla", "Written", "Live now" },
            mixing.Rows());

        if (!mixing.TypeResolved)
        {
            result.NotFound($"'{GameTypes.MixingStation}' is not on this build, so mixing is untouched.");
            return;
        }

        if (Math.Abs(wanted - 1f) < 0.0001f)
        {
            result.Ok("The multiplier is 1, so mixing is deliberately vanilla.");
            return;
        }

        if (mixing.StationsScaled == 0)
        {
            result.Inconclusive(
                "No mixing station has been scaled. Expected if the save owns none; if you have one, " +
                "walk into the property so it loads and run this again.");
            return;
        }

        // The comparison that matters: the field as the game reads it right now, not as we wrote it.
        // A destroyed station reports "destroyed" rather than a number; that is a scene teardown, not drift.
        var drifted = mixing.Rows(int.MaxValue)
            .Count(row => int.TryParse(row[3], out _) && !string.Equals(row[2], row[3], StringComparison.Ordinal));
        if (drifted > 0)
        {
            result.Fail($"{drifted} station(s) no longer hold the value written to them; something else is writing MixTimePerItem.");
            return;
        }

        result.Ok($"Mixing is running at x{wanted:0.##} across {mixing.StationsScaled} station(s), and every one reads back what was written.");
    }

    private static void Deposit(ProbeContext context, ProbeResult result)
    {
        if (TweaksRuntime.Config is not { } config || TweaksRuntime.Deposit is not { } deposit)
        {
            result.Inconclusive("Quality of Life is switched off, so the deposit ceiling is vanilla.");
            return;
        }

        var facts = deposit.LimitFacts;
        var sumFacts = deposit.SumFacts;

        result.Fact("Configured ceiling", config.EnableDepositLimit.Value
            ? "$" + config.WeeklyDepositLimit.Value.ToString("N0")
            : "vanilla (tweak switched off)");
        result.Fact("Game's own const", "$" + deposit.VanillaLimit.ToString("N0"));
        result.Fact("Ceiling in force", "$" + deposit.EffectiveLimit.ToString("N0"));
        result.Fact("Offset applied", deposit.IsActive ? "$" + deposit.Offset.ToString("N0") : "none");
        result.Fact("Deposited this week (true)", "$" + deposit.TrueSum.ToString("N0"));
        result.Fact("Counter as the game holds it", "$" + deposit.RawSum.ToString("N0"));
        result.Fact("Remaining allowance", "$" + deposit.RemainingAllowance.ToString("N0"));

        result.Table(
            new[] { "Field", "IL2CPP flags", "Verdict" },
            new IReadOnlyList<string>[]
            {
                new[] { "ATM.WeeklyDepositLimit", facts.FlagNames, facts.Explain("WeeklyDepositLimit") },
                new[] { "ATM.WeeklyDepositSum", sumFacts.FlagNames, sumFacts.Explain("WeeklyDepositSum") },
            });

        Patches(result, "ATM.WeekPass", "MoneyManager.GetSaveString", "MoneyManager.Load", "ATMInterface.Update");
        result.Fact("ATM screen frames seen", TweakPatches.AtmUpdateCalls.ToString());

        if (!GameReflection.TypeExists(GameTypes.Atm))
        {
            result.NotFound($"'{GameTypes.Atm}' is not on this build, so the ceiling is untouched.");
            return;
        }

        if (config.ResolvedDepositLimit is null)
        {
            result.Ok("The tweak is switched off, so the game's own $10,000 ceiling applies.");
            return;
        }

        if (!deposit.IsActive)
        {
            result.Fail(
                "The ceiling was asked for but no offset is applied. " +
                $"'{GameTypes.Atm}.WeeklyDepositSum' reports: {sumFacts.Explain("WeeklyDepositSum")}.");
            return;
        }

        if (!TweakPatches.IsApplied("MoneyManager.GetSaveString"))
        {
            result.Fail(
                "The offset is applied but MoneyManager.GetSaveString could not be patched, so a save " +
                "would record the shifted counter. Switch the tweak off until this resolves.");
            return;
        }

        result.Ok(
            $"The weekly ceiling is ${deposit.EffectiveLimit:N0} and ${deposit.RemainingAllowance:N0} of it is " +
            "left; the game's const is untouched and the save still records the true total.");
    }

    private static void Delivery(ProbeContext context, ProbeResult result)
    {
        if (TweaksRuntime.Config is not { } config || TweaksRuntime.Delivery is not { } delivery)
        {
            result.Inconclusive("Quality of Life is switched off, so deliveries are vanilla.");
            return;
        }

        var wanted = config.ResolvedDeliveryMultiplier;

        result.Fact("Configured multiplier", $"x{config.DeliverySpeedMultiplier.Value:0.##}" +
                                             (config.EnableFasterDeliveries.Value ? string.Empty : " (tweak switched off)"));
        result.Fact("Multiplier in force", $"x{wanted:0.##}");
        result.Fact("Applied multiplier", $"x{delivery.Multiplier:0.##}");
        result.Fact("Orders shortened mid-flight", delivery.InFlightRescaled.ToString());
        result.Fact("Plausible arrival ceiling", $"{delivery.PlausibleCeiling} game minutes");

        Patches(result, "DeliveryShop.GetDeliveryTime");
        result.Fact("Quotes seen", TweakPatches.DeliveryQuoteCalls.ToString());
        result.Fact("Quotes refused as implausible", delivery.RefusedQuotes.ToString());

        result.Table(new[] { "Vanilla quote", "Quoted to the player" }, delivery.SettingsRows());

        // Stored value and rendered string side by side. Agreement means both halves are sound; a
        // stored value past the ceiling means the data is poisoned; a sane stored value next to a
        // silly label would mean the UI is at fault. That distinction is the reason these exist.
        var rows = delivery.DeliveryRows();
        result.Table(
            new[]
            {
                "Delivery", "Shop", "Status",
                "TimeUntilArrival (int, game minutes)", "As a duration", "Plausible",
                "Shown on the phone", "Shortened by us",
            },
            rows);

        if (!GameReflection.TypeExists(GameTypes.DeliveryShop))
        {
            result.NotFound($"'{GameTypes.DeliveryShop}' is not on this build, so deliveries are untouched.");
            return;
        }

        var implausible = rows.Count(row => row[5].StartsWith("NO", StringComparison.Ordinal));
        if (implausible > 0)
        {
            result.Fail(
                $"{implausible} order(s) hold an arrival time past the plausible ceiling. Run \"Repair stuck " +
                "deliveries\" from the Actions tab; the orders themselves are intact and nothing needs cancelling.");
            return;
        }

        if (Math.Abs(wanted - 1f) < 0.0001f)
        {
            result.Ok("The multiplier is 1, so deliveries are deliberately vanilla and no quote is altered.");
            return;
        }

        if (!TweakPatches.IsApplied("DeliveryShop.GetDeliveryTime"))
        {
            result.Fail(
                "DeliveryShop.GetDeliveryTime could not be patched, so orders still take the vanilla time. " +
                "Check the log for the reason.");
            return;
        }

        result.Ok(
            $"Delivery timing is at x{wanted:0.##}. {rows.Count} order(s) tracked, all with sane arrival times. " +
            "The quote drives both the ETA on the order screen and the truck, so the phone and the truck agree.");
    }

    /// <summary>Bound-or-not for the named targets, so a silent no-op shows up as a row rather than a guess.</summary>
    private static void Patches(ProbeResult result, params string[] labels)
    {
        var rows = new List<IReadOnlyList<string>>();

        foreach (var label in labels)
        {
            rows.Add(new[]
            {
                label,
                TweakPatches.IsApplied(label) ? "bound"
                    : TweakPatches.MissingTargets.Contains(label) ? "NOT FOUND on this build"
                    : "not requested",
            });
        }

        result.Table(new[] { "Patch target", "State" }, rows);
    }
}
