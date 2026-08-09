using Expansions.Core;
using Expansions.Core.Actions;
using Expansions.Core.Diagnostics;
using Expansions.Tweaks.Runtime;

namespace Expansions.Tweaks.Menu;

/// <summary>
/// What the owner can do to this module from the F7 screen.
/// <para>
/// Three read-outs and one repair. The read-outs exist because all three tweaks are invisible by
/// nature — nothing on screen says "mixing is at 2x", and a tweak that silently failed to bind looks
/// exactly like one that worked. Each button answers with the number the game is actually using, not
/// the number in the config file.
/// </para>
/// <para>
/// <see cref="ActionRegistry"/> is called directly with named arguments rather than through
/// reflection against a guessed constructor, which is how a sibling module once registered eight
/// actions that never appeared.
/// </para>
/// </summary>
internal static class TweakActions
{
    /// <summary>Set by the mixing read-out. The tutorial's first objective watches it.</summary>
    internal static bool MixingWasRead { get; private set; }

    /// <summary>Set by the ATM read-out.</summary>
    internal static bool DepositWasRead { get; private set; }

    /// <summary>Set by the delivery read-out.</summary>
    internal static bool DeliveryWasRead { get; private set; }

    internal static void Register(ModuleLifetime lifetime)
    {
        MixingWasRead = false;
        DepositWasRead = false;
        DeliveryWasRead = false;

        Bind(lifetime, new ExpansionAction(
            id: "tweaks.show_mixing",
            label: "Show mixing speed",
            description: "The multiplier in force and the per-item mix time every station in this save is actually using.",
            isAvailable: Live,
            invoke: ShowMixing,
            order: 0));

        Bind(lifetime, new ExpansionAction(
            id: "tweaks.show_deposit_limit",
            label: "Show ATM deposit allowance",
            description: "The weekly ceiling in force, what you have banked this week, and what is left of it.",
            isAvailable: Live,
            invoke: ShowDeposit,
            order: 1));

        Bind(lifetime, new ExpansionAction(
            id: "tweaks.show_deliveries",
            label: "Show delivery timing",
            description: "The delivery multiplier in force, the scaled timing figures, and the ETA of anything on the way.",
            isAvailable: Live,
            invoke: ShowDeliveries,
            order: 2));

        Bind(lifetime, new ExpansionAction(
            id: "tweaks.repair_deliveries",
            label: "Repair stuck deliveries",
            description: "Finds any order whose countdown is not a plausible duration and puts a correct arrival time back on it. Healthy orders are left completely alone, and nothing is ever cancelled, re-ordered or charged for.",
            isAvailable: Live,
            invoke: RepairDeliveries,
            order: 3));

        Bind(lifetime, new ExpansionAction(
            id: "tweaks.reapply",
            label: "Re-apply all three tweaks",
            description: "Re-reads the config and re-scales everything. Use it after editing the preferences file by hand, or if a station built after you loaded is still running at vanilla speed.",
            isAvailable: Live,
            invoke: Reapply,
            order: 4));
    }

    private static ActionResult RepairDeliveries()
    {
        if (TweaksRuntime.Delivery is not { } delivery)
            return NotWired();

        var report = delivery.Repair("the menu action");

        if (report.Inspected == 0)
            return ActionResult.NoChange("Nothing is on the way right now, so there is nothing to repair.");

        if (report.Repaired.Count == 0)
        {
            return ActionResult.Ok(
                report.BeyondHelp == 0
                    ? $"Checked {report.Inspected} order(s); every one has a sensible arrival time. Nothing changed."
                    : $"Checked {report.Inspected} order(s). {report.BeyondHelp} look wrong but could not be given a " +
                      "sane time yet because no delivery shop was reachable to quote one - open the deliveries " +
                      "app once and try again. Nothing was cancelled.");
        }

        var detail = string.Join("; ", report.Repaired.Take(4).Select(row =>
            $"{row[1]} was {row[2]} min, now {DeliverySpeed.Describe(int.TryParse(row[3], out var m) ? m : 0)}"));
        var more = report.Repaired.Count > 4 ? $" (+{report.Repaired.Count - 4} more)" : string.Empty;

        return ActionResult.Ok(
            $"Repaired {report.Repaired.Count} of {report.Inspected} order(s): {detail}{more}. " +
            "Contents, cost and destination are untouched." +
            (report.BeyondHelp > 0 ? $" {report.BeyondHelp} could not be worked out and were left alone." : string.Empty));
    }

    private static ActionResult ShowMixing()
    {
        MixingWasRead = true;

        if (TweaksRuntime.Config is not { } config || TweaksRuntime.Mixing is not { } mixing)
            return NotWired();

        var multiplier = config.ResolvedMixMultiplier;

        if (!mixing.TypeResolved)
            return ActionResult.Failed($"'{GameTypes.MixingStation}' is not on this build, so mixing is untouched.");

        if (Math.Abs(multiplier - 1f) < 0.0001f)
        {
            return ActionResult.Ok(config.EnableFasterMixing.Value
                ? "Mixing multiplier is 1x, which is exactly vanilla. Raise mix_speed_multiplier to speed it up."
                : "Faster mixing is switched off, so mixing runs at vanilla speed.");
        }

        if (mixing.StationsScaled == 0)
        {
            return ActionResult.Ok(
                $"Mixing is set to x{multiplier:0.##}, but no station has been scaled yet. That is expected if " +
                "this save owns none; otherwise walk into the property so the station loads.");
        }

        var rows = mixing.Rows(4);
        var detail = string.Join(", ", rows.Select(row => $"{row[0]} {row[1]}->{row[3]} min/item"));
        var more = mixing.StationsScaled > rows.Count ? $" (+{mixing.StationsScaled - rows.Count} more)" : string.Empty;

        return ActionResult.Ok(
            $"Mixing is running at x{multiplier:0.##} across {mixing.StationsScaled} station(s): {detail}{more}. " +
            (config.MixSpeedAppliesToEmployees.Value
                ? "Chemists get the same speed-up."
                : "Chemist-run mixes are held at the vanilla time where the game lets us tell who started them."));
    }

    private static ActionResult ShowDeposit()
    {
        DepositWasRead = true;

        if (TweaksRuntime.Config is not { } config || TweaksRuntime.Deposit is not { } deposit)
            return NotWired();

        if (!GameReflection.TypeExists(GameTypes.Atm))
            return ActionResult.Failed($"'{GameTypes.Atm}' is not on this build, so the deposit ceiling is untouched.");

        if (config.ResolvedDepositLimit is null)
        {
            return ActionResult.Ok(
                $"The custom ceiling is switched off, so the game's own ${deposit.VanillaLimit:N0} a week applies. " +
                $"You have banked ${deposit.TrueSum:N0} of it.");
        }

        if (!deposit.IsActive)
        {
            return ActionResult.Failed(
                $"A ${config.WeeklyDepositLimit.Value:N0} ceiling was asked for but could not be applied: " +
                $"{deposit.SumFacts.Explain("WeeklyDepositSum")}. The vanilla ${deposit.VanillaLimit:N0} still applies.");
        }

        return ActionResult.Ok(
            $"The weekly ATM ceiling is ${deposit.EffectiveLimit:N0}, up from the game's own ${deposit.VanillaLimit:N0}. " +
            $"You have banked ${deposit.TrueSum:N0} this week, so ${deposit.RemainingAllowance:N0} is left. " +
            "Your save still records the true total, not the shifted one.");
    }

    private static ActionResult ShowDeliveries()
    {
        DeliveryWasRead = true;

        if (TweaksRuntime.Config is not { } config || TweaksRuntime.Delivery is not { } delivery)
            return NotWired();

        var multiplier = config.ResolvedDeliveryMultiplier;
        var live = delivery.DeliveryRows();

        var headline = Math.Abs(multiplier - 1f) < 0.0001f
            ? config.EnableFasterDeliveries.Value
                ? "Delivery multiplier is 1x, which is exactly vanilla."
                : "Faster deliveries are switched off, so orders take the vanilla time."
            : $"Deliveries are running at x{multiplier:0.##}, so an order the game would quote at 120 minutes is " +
              $"quoted {Math.Max(1, (int)Math.Round(120 / multiplier))} instead." +
              (delivery.SettingsResolved
                  ? string.Empty
                  : " No order has been quoted yet this session, so the scaling is armed but unproven - open the " +
                    "deliveries app once to confirm it.");

        if (live.Count == 0)
            return ActionResult.Ok(headline + " Nothing is on the way right now.");

        var detail = string.Join("; ", live.Take(4).Select(row => $"{row[1]} {row[2].ToLowerInvariant()}, {row[3]} min out"));
        var more = live.Count > 4 ? $" (+{live.Count - 4} more)" : string.Empty;

        return ActionResult.Ok($"{headline} On the way: {detail}{more}.");
    }

    private static ActionResult Reapply()
    {
        if (TweaksRuntime.Reapply is not { } reapply || TweaksRuntime.Config is not { } config)
            return NotWired();

        reapply();

        var mixing = TweaksRuntime.Mixing;
        return ActionResult.Ok(
            $"Re-applied. Mixing x{config.ResolvedMixMultiplier:0.##} across {mixing?.StationsScaled ?? 0} station(s), " +
            $"deposit ceiling {(config.ResolvedDepositLimit is { } limit ? "$" + limit.ToString("N0") : "vanilla")}, " +
            $"deliveries x{config.ResolvedDeliveryMultiplier:0.##}.");
    }

    private static ActionAvailability Live() =>
        TweaksRuntime.IsLive
            ? ActionAvailability.Ready
            : ActionAvailability.Unavailable("Quality of Life is switched off");

    private static ActionResult NotWired() => ActionResult.Failed(
        "Quality of Life is not wired into a loaded game yet, so there is nothing to report. Load a save and try again.");

    private static void Bind(ModuleLifetime lifetime, ExpansionAction action)
    {
        try
        {
            lifetime.Add(ActionRegistry.Register(action));
        }
        catch (Exception ex)
        {
            TweakLog.Warn($"Could not register menu action '{action.Id}': {TweakLog.Describe(ex)}");
        }
    }
}
