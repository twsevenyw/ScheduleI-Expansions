using Expansions.Core.Diagnostics;
using Expansions.PoliceOverhaul.State;
using S1API.GameTime;
using S1API.Money;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// What happens after the cell door closes, for someone the police already have a file on.
/// <para>
/// The game has no jail — no holding cell, no custody state, not one <c>jail</c> or <c>detain</c>
/// literal in the metadata — so building one means authoring an interior nobody asked for. Instead
/// this reuses two systems that already ship: the clock skip that a night's sleep runs on, and the
/// employee wage table. You lose the rest of the day and you pay a processing fee equal to a day of
/// your own payroll. The economic shape of "forfeit a day" without reaching into the payday system.
/// </para>
/// <para>
/// Outlaw only, deliberately. Losing a day to a curfew ticket would be a bug report.
/// </para>
/// </summary>
internal sealed class Custody
{
    /// <summary>Charged when the player employs nobody, so the fee is never zero for a lone operator.</summary>
    private const float MinimumProcessingFee = 250f;

    private readonly PoliceConfig _config;
    private readonly HeatDirector _heat;
    private readonly OutlawState _outlaw;

    internal Custody(PoliceConfig config, HeatDirector heat, OutlawState outlaw)
    {
        _config = config;
        _heat = heat;
        _outlaw = outlaw;
    }

    internal int DaysServed { get; private set; }

    internal float FeesCharged { get; private set; }

    /// <summary>
    /// Runs on release. Returns a sentence describing what is about to happen, or empty when nothing
    /// is.
    /// <para>
    /// The announcement is not optional and it goes out <em>before</em> the clock moves. An unexplained
    /// jump from Tuesday evening to Wednesday morning is indistinguishable from a crash, and a player
    /// who sees that once uninstalls.
    /// </para>
    /// </summary>
    internal string Process(object? player)
    {
        if (!_config.EnableConsequences.Value || !_config.EnableJailDay.Value)
            return string.Empty;

        if (!_outlaw.IsOutlawed(player))
            return string.Empty;

        var fee = ChargeProcessingFee(player);
        FeesCharged += fee;

        var sentence = "They held you until morning.";
        if (fee > 0f)
            sentence += $" Processing cost ${fee:0}, and your crew still had to be paid.";

        // Toast: the clock skip lands moments later; a phone text would arrive after the jump.
        if (_config.ShowHud.Value)
            PoliceMessages.BookedIn(sentence, player);

        // Off this stack: release finishes inside a FishNet RPC body, and moving the world clock from
        // in there re-enters the arrest teardown.
        Deferred.After(30, "skipping the day after an arrest", () =>
        {
            if (SkipToMorning())
            {
                DaysServed++;
                PoliceLog.Msg($"Custody processed: day skipped, fee ${fee:0}.");
            }
            else if (_config.ShowHud.Value)
            {
                PoliceMessages.EarlyRelease(
                    "The clock could not be moved, so you kept the rest of the day. The processing fee still stood.",
                    player);
            }
        });

        return sentence;
    }

    /// <summary>
    /// One day of the player's own payroll, in cash, falling back to a flat minimum. Uses the live
    /// roster rather than the shipped rate card so hiring more people really does raise the stakes.
    /// </summary>
    internal float DailyPayroll()
    {
        var total = 0f;

        foreach (var property in Estate.Owned())
        {
            foreach (var employee in GameReflection.Enumerate(Members.ReadPath(property, "Employees"), 64))
            {
                if (GameReflection.IsPresent(employee))
                    total += Members.Read(employee, "DailyWage", 0f);
            }
        }

        return total;
    }

    internal float ProcessingFeeDue() =>
        Math.Max(MinimumProcessingFee, DailyPayroll()) * Math.Max(0f, _config.JailProcessingFeeScalar.Value);

    private float ChargeProcessingFee(object? player)
    {
        var due = ProcessingFeeDue();
        if (due <= 0f)
            return 0f;

        var cash = SafeCash();
        var paid = Math.Min(Math.Max(0f, cash), due);

        if (paid > 0f)
            Money.ChangeCashBalance(-paid, true, true);

        var shortfall = due - paid;
        if (shortfall > 0.5f && _config.EnableDebt.Value)
        {
            Money.CreateOnlineTransaction("Processing fee", -shortfall, 1f, "Custody processing");
            paid += shortfall;
        }

        _heat.AddPoliceTake(player, paid);
        return paid;
    }

    /// <summary>
    /// Pushes the clock to the game's own wake time. <c>SkipForwardToTime</c> is the shipped call the
    /// sleep transition uses, so employees are paid, plants advance and the day counter moves exactly
    /// as they would after a night in bed.
    /// </summary>
    private static bool SkipToMorning()
    {
        var manager = GameBridge.Singleton(GameTypes.TimeManager);
        if (manager is null)
        {
            PoliceLog.Warn("Could not skip the day after an arrest: the time manager is not up.");
            return false;
        }

        if (Members.Read(manager, "IsSleepInProgress", false))
            return false;

        var wakeTime = ReadWakeTime();
        var before = TimeManager.ElapsedDays;

        if (!Members.Invoke(manager, "SkipForwardToTime", wakeTime))
        {
            PoliceLog.Warn($"Could not skip the day after an arrest: TimeManager.SkipForwardToTime({wakeTime}) did not run.");
            return false;
        }

        PoliceLog.Detail($"Custody skipped the clock to {wakeTime:0000} (day {before} -> {TimeManager.ElapsedDays}).");
        return true;
    }

    /// <summary>
    /// The game's own wake time, falling back to the shipped 07:00. Read rather than hard-coded so a
    /// balance patch that moves the morning moves this with it.
    /// </summary>
    private static int ReadWakeTime()
    {
        var type = GameReflection.FindType(GameTypes.TimeManager);
        if (type is not null && GameReflection.TryReadStatic(type, "WakeTime", out var value, out _) && value is int wake && wake > 0)
            return wake;

        return 700;
    }

    private static float SafeCash()
    {
        try
        {
            return Money.GetCashBalance();
        }
        catch
        {
            return 0f;
        }
    }
}
