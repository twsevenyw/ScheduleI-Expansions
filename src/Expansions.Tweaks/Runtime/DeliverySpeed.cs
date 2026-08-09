using Expansions.Core.Diagnostics;

namespace Expansions.Tweaks.Runtime;

/// <summary>
/// Faster shop deliveries, applied at the one function that quotes them.
/// <para>
/// <c>DeliveryShop.GetDeliveryTime(itemCount)</c> returns the number of game minutes an order will
/// take. It is the single funnel: the order screen prints it, and <c>SubmitOrder</c> bakes it into
/// the new <c>DeliveryInstance.TimeUntilArrival</c>. Scaling its result moves the quoted ETA and the
/// truck together, from one postfix, with nothing to restore on disable.
/// </para>
/// <para>
/// There is no ETA-text patch and no need for one. The countdown on the phone is
/// <c>TimeUntilArrival</c>, which <c>DeliveryInstance.OnTimePass(minutes)</c> decrements one per game
/// minute; the number shown is the literal number of minutes left, before and after. Scaling the
/// tick instead would make the countdown run at double speed and the quoted ETA a lie.
/// </para>
/// <para>
/// <b>Why it is done this way.</b> The first version of this scaled the three
/// <c>DeliverySettings</c> fields — <c>DeliveryTimePerItem</c>, <c>MinimumDeliveryTime</c>,
/// <c>MaximumDeliveryTime</c> — directly on the settings asset. Those are not primitives: each is a
/// <c>SerializableSettingsField&lt;T&gt;</c>, and reading <c>.Value</c> off that generic wrapper
/// through reflection does not come back as the number. It came back as a raw 32-bit payload,
/// 807768992, identical for all three fields (read as a float that bit pattern is a denormal that
/// prints as 0, which is why the per-item time logged as "0 -&gt; 0"). Half of it, 403884480, was
/// then written back as the delivery floor and ceiling, so every subsequent order was clamped to
/// 403,884,480 game minutes and the app showed 6731407h. Quoting through the game's own function
/// instead of second-guessing its storage removes that entire class of mistake: this module never
/// writes to the settings asset again.
/// </para>
/// </summary>
internal sealed class DeliverySpeed
{
    /// <summary><c>EDeliveryStatus.InTransit</c>. Waiting/Arrived/Completed are 1/2/3.</summary>
    private const int InTransit = 0;

    /// <summary>
    /// The longest arrival this module will believe, in game minutes. Seven in-game days is far
    /// beyond anything the shop quotes, so a value past it did not come from the delivery system
    /// doing arithmetic — it came from something writing nonsense, and is a repair candidate.
    /// </summary>
    private const int PlausibleCeilingMinutes = 10_080;

    /// <summary>How many distinct quotes the probe remembers.</summary>
    private const int QuoteMemory = 12;

    /// <summary>
    /// Quotes actually seen, keyed by the vanilla result: what the game asked for against what the
    /// player was told. The probe reports these rather than recomputing, so it shows what happened
    /// rather than what should have happened.
    /// </summary>
    private static readonly Dictionary<int, (int Vanilla, int Quoted)> Quotes = new();

    private static float _multiplier = 1f;
    private static int _refused;

    private readonly Dictionary<string, float> _rescaled = new(StringComparer.Ordinal);

    internal float Multiplier => _multiplier;

    /// <summary>True once the quote function has actually been called, i.e. the scaling is proven live.</summary>
    internal bool SettingsResolved => Quotes.Count > 0;

    internal int InFlightRescaled => _rescaled.Count;

    /// <summary>Quotes left alone because the vanilla value was not a plausible duration.</summary>
    internal int RefusedQuotes => _refused;

    internal int PlausibleCeiling => PlausibleCeilingMinutes;

    /// <summary>A duration this module is willing to treat as a real minutes-to-arrival.</summary>
    internal static bool IsPlausible(int minutes) => minutes >= 0 && minutes <= PlausibleCeilingMinutes;

    /// <summary>
    /// Arms the scaling. Nothing is written anywhere: the multiplier is picked up by the postfix on
    /// the next quote, so this cannot fail and cannot leave the game in a modified state.
    /// </summary>
    internal bool Apply(float multiplier)
    {
        if (Math.Abs(multiplier - _multiplier) > 0.0001f)
        {
            _multiplier = multiplier;
            Quotes.Clear();
            _refused = 0;
        }

        return true;
    }

    /// <summary>Disarms the scaling. Later quotes come out vanilla; there is nothing to undo.</summary>
    internal void Restore()
    {
        _multiplier = 1f;
        Quotes.Clear();
        _refused = 0;
    }

    /// <summary>
    /// The scaled quote, called from the <c>GetDeliveryTime</c> postfix.
    /// <para>
    /// At a multiplier of 1.0 this returns the vanilla result unchanged, by identity rather than by
    /// arithmetic that happens to round back — so "off" is byte-identical to not being installed. A
    /// vanilla result that is not itself a plausible duration is handed straight back untouched and
    /// counted, because scaling a number that was already wrong only hides where it came from.
    /// </para>
    /// </summary>
    internal static int ScaleQuote(int vanillaMinutes)
    {
        if (Math.Abs(_multiplier - 1f) < 0.0001f || vanillaMinutes <= 0)
            return vanillaMinutes;

        if (!IsPlausible(vanillaMinutes))
        {
            _refused++;
            TweakLog.Warn(
                $"The game quoted {vanillaMinutes} minutes for a delivery, which is past the plausible " +
                $"ceiling of {PlausibleCeilingMinutes}. The quote has been left exactly as it is.");
            return vanillaMinutes;
        }

        var scaled = Math.Max(1, (int)Math.Round(vanillaMinutes / _multiplier, MidpointRounding.AwayFromZero));

        if (Quotes.Count < QuoteMemory || Quotes.ContainsKey(vanillaMinutes))
            Quotes[vanillaMinutes] = (vanillaMinutes, scaled);

        return scaled;
    }

    /// <summary>
    /// Speeds up the orders that were already on the way when the module was switched on.
    /// <para>
    /// Deliberately fired only on the enable transition, never on a scene load. A delivery ordered
    /// while the module was already on was quoted by the scaled function, so sweeping again after a
    /// save reload would halve it a second time — and a third, and a fourth. One sweep per enable,
    /// undone by the matching disable, cannot compound.
    /// </para>
    /// </summary>
    internal void RescaleInFlight()
    {
        if (IsVanilla)
            return;

        foreach (var delivery in Live())
        {
            var id = Members.Read(delivery, "DeliveryID", string.Empty);
            if (id.Length == 0 || _rescaled.ContainsKey(id))
                continue;

            if (Members.Read(delivery, "Status", -1) != InTransit)
                continue;

            var remaining = Members.Read(delivery, "TimeUntilArrival", 0);
            if (remaining <= 1)
                continue;

            // Refuse to scale a value that was not a sane duration to begin with. Halving nonsense
            // produces slightly smaller nonsense and buries the evidence; Repair is the tool for
            // that case, and it runs immediately after this.
            if (!IsPlausible(remaining))
            {
                TweakLog.Warn(
                    $"Delivery '{id}' reports {remaining} minutes to arrival, which is not a plausible " +
                    $"duration (the ceiling is {PlausibleCeilingMinutes}); leaving it for the repair pass.");
                continue;
            }

            var scaled = Math.Max(1, (int)Math.Round(remaining / _multiplier, MidpointRounding.AwayFromZero));
            if (!Members.Write(delivery, "TimeUntilArrival", scaled))
                continue;

            _rescaled[id] = _multiplier;
            TweakLog.Detail($"Delivery '{id}' shortened from {remaining} to {scaled} minutes.");
        }
    }

    /// <summary>Puts back what <see cref="RescaleInFlight"/> took off, for anything still on the way.</summary>
    internal void RestoreInFlight()
    {
        if (_rescaled.Count == 0)
            return;

        foreach (var delivery in Live())
        {
            var id = Members.Read(delivery, "DeliveryID", string.Empty);
            if (id.Length == 0 || !_rescaled.TryGetValue(id, out var factor))
                continue;

            var remaining = Members.Read(delivery, "TimeUntilArrival", 0);
            if (remaining > 0)
                Members.Write(delivery, "TimeUntilArrival", Math.Max(1, (int)Math.Round(remaining * factor)));
        }

        _rescaled.Clear();
    }

    /// <summary>Drops in-flight bookkeeping without writing, for a scene that is already gone.</summary>
    internal void Forget() => _rescaled.Clear();

    /// <summary>
    /// Puts a sane arrival time back on any delivery whose countdown is not a plausible duration.
    /// <para>
    /// Repair, never cancellation: the order, its contents, its destination and its money are all
    /// untouched, and the only field written is <c>TimeUntilArrival</c>. The replacement is what the
    /// shop would quote for the same basket today, taken from the game's own
    /// <c>GetDeliveryTime</c> — so it already carries the configured multiplier and matches what a
    /// fresh order would say.
    /// </para>
    /// <para>
    /// Idempotent by construction: a delivery already inside the plausible range is skipped, so this
    /// writes nothing on a healthy save and does nothing at all the second time it runs. That is
    /// what makes it safe to fire automatically on every load.
    /// </para>
    /// </summary>
    internal RepairReport Repair(string because)
    {
        var repaired = new List<IReadOnlyList<string>>();
        var inspected = 0;
        var beyondHelp = 0;

        foreach (var delivery in Live())
        {
            // In transit or waiting for a free dock: both still have a countdown to run down.
            if (Members.Read(delivery, "Status", -1) is not (InTransit or 1))
                continue;

            inspected++;

            var remaining = Members.Read(delivery, "TimeUntilArrival", 0);
            if (IsPlausible(remaining))
                continue;

            var id = Members.Read(delivery, "DeliveryID", "?");
            var shop = Members.Read(delivery, "StoreName", "?");
            var replacement = Quote(delivery);

            if (replacement <= 0)
            {
                beyondHelp++;
                TweakLog.Warn(
                    $"Delivery '{id}' from {shop} reports {remaining} minutes to arrival, and no sane " +
                    "replacement could be worked out because no delivery shop was reachable to quote " +
                    "one. Nothing has been cancelled and the order is exactly as it was; it will be " +
                    "repaired on the next pass, or from the \"Repair stuck deliveries\" action.");
                continue;
            }

            if (!Members.Write(delivery, "TimeUntilArrival", replacement))
            {
                beyondHelp++;
                TweakLog.Warn($"Delivery '{id}' from {shop} could not be written to; it is left untouched.");
                continue;
            }

            // Mark it as already handled so the in-flight sweep does not then scale it again: the
            // replacement quote came through the scaled function and is final.
            _rescaled[id] = 1f;

            repaired.Add(new[] { id, shop, remaining.ToString(), replacement.ToString() });

            TweakLog.Msg(
                $"Repaired delivery '{id}' from {shop}: minutes to arrival {remaining} -> {replacement} " +
                $"({Describe(replacement)}). Nothing else about the order was changed.");
        }

        if (repaired.Count > 0 || beyondHelp > 0)
        {
            TweakLog.Msg(
                $"Delivery repair ({because}): {inspected} order(s) inspected, {repaired.Count} repaired, " +
                $"{beyondHelp} left for a later pass.");
        }
        else
        {
            TweakLog.Detail($"Delivery repair ({because}): {inspected} order(s) inspected, all plausible.");
        }

        return new RepairReport(inspected, repaired, beyondHelp);
    }

    /// <summary>
    /// What the shop would quote for this order's basket right now, straight from the game's own
    /// <c>GetDeliveryTime</c>. Zero when no shop is reachable, which the caller reads as "leave it
    /// alone and try later" rather than inventing a number.
    /// </summary>
    private static int Quote(object? delivery)
    {
        var shopType = GameReflection.FindType(GameTypes.DeliveryShop);
        if (shopType is null)
            return 0;

        var items = 0;
        foreach (var pair in GameReflection.Enumerate(Members.ReadObject(delivery, "Items")))
            items += Math.Max(0, Members.Read(pair, "Int", 0));

        items = Math.Max(1, items);

        foreach (var (_, shop) in Members.FindAll(shopType))
        {
            if (!GameReflection.TryInvoke(shopType, shop, "GetDeliveryTime", new object?[] { items }, out var raw, out _))
                continue;

            if (raw is int quoted && IsPlausible(quoted) && quoted > 0)
                return quoted;
        }

        return 0;
    }

    /// <summary>What one repair pass did, for the menu action and the probe.</summary>
    internal readonly struct RepairReport
    {
        internal RepairReport(int inspected, IReadOnlyList<IReadOnlyList<string>> repaired, int beyondHelp)
        {
            Inspected = inspected;
            Repaired = repaired;
            BeyondHelp = beyondHelp;
        }

        internal int Inspected { get; }

        /// <summary>One row per repaired order: id, shop, the bad value, the value written.</summary>
        internal IReadOnlyList<IReadOnlyList<string>> Repaired { get; }

        /// <summary>Orders that looked wrong but could not be given a sane value, and were left alone.</summary>
        internal int BeyondHelp { get; }
    }

    /// <summary>Every quote the game has asked for since the module armed: what it wanted, what it got.</summary>
    internal IReadOnlyList<IReadOnlyList<string>> SettingsRows()
    {
        var rows = new List<IReadOnlyList<string>>();

        foreach (var (vanilla, quoted) in Quotes.Values.OrderBy(entry => entry.Vanilla))
            rows.Add(new[] { $"{vanilla} ({Describe(vanilla)})", $"{quoted} ({Describe(quoted)})" });

        return rows;
    }

    /// <summary>
    /// One row per live delivery, deliberately reporting both halves of the pipeline: the raw field
    /// as stored, the unit it is in, the duration that follows from it, and — separately — the string
    /// the phone is actually showing.
    /// <para>
    /// Those columns exist because of a specific failure. Every order read "6731407h" while the
    /// stored <c>TimeUntilArrival</c> was 403,883,971 game minutes, and the two facts had to be put
    /// side by side before it was obvious that the data was poisoned rather than the label. Printing
    /// the stored value, its unit, the duration it implies and the rendered string together settles
    /// "the delivery is broken" against "the label is lying" in one glance, with no screenshot.
    /// </para>
    /// </summary>
    internal IReadOnlyList<IReadOnlyList<string>> DeliveryRows()
    {
        var rows = new List<IReadOnlyList<string>>();
        var labels = RenderedLabels();

        foreach (var delivery in Live())
        {
            var id = Members.Read(delivery, "DeliveryID", "?");
            var minutes = Members.Read(delivery, "TimeUntilArrival", -1);

            rows.Add(new[]
            {
                id.Length > 8 ? id[..8] : id,
                Members.Read(delivery, "StoreName", "?"),
                StatusName(Members.Read(delivery, "Status", -1)),
                minutes.ToString(),
                Describe(minutes),
                IsPlausible(minutes) ? "yes" : $"NO (ceiling {PlausibleCeilingMinutes})",
                labels.TryGetValue(id, out var label) ? label : "<not on screen>",
                _rescaled.ContainsKey(id) ? "yes" : "no",
            });
        }

        return rows;
    }

    /// <summary>Game minutes rendered the way a player reads them, for comparison against the phone.</summary>
    internal static string Describe(int minutes)
    {
        if (minutes < 0)
            return "unreadable";

        return minutes < 60
            ? $"{minutes}m"
            : $"{minutes / 60}h {minutes % 60}m";
    }

    /// <summary>
    /// What the phone's deliveries app currently shows, by delivery id. Read-only: this reports the
    /// UI, it never writes to it, so a wrong label stays visible rather than being papered over.
    /// </summary>
    private static Dictionary<string, string> RenderedLabels()
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);

        var displayType = GameReflection.FindType(GameTypes.DeliveryStatusDisplay);
        if (displayType is null)
            return labels;

        foreach (var (_, display) in Members.FindAll(displayType))
        {
            var instance = Members.ReadObject(display, "DeliveryInstance");
            if (instance is null)
                continue;

            var id = Members.Read(instance, "DeliveryID", string.Empty);
            if (id.Length == 0 || labels.ContainsKey(id))
                continue;

            var label = Members.ReadObject(display, "StatusLabel");
            labels[id] = label is null ? "<no label>" : Members.Read(label, "text", "<empty>");
        }

        return labels;
    }

    /// <summary>Every delivery the manager is tracking, or nothing if there is no loaded game.</summary>
    internal IReadOnlyList<object?> Live()
    {
        if (!GameReflection.TryGetSingleton(GameTypes.DeliveryManager, out var manager, out _))
            return Array.Empty<object?>();

        return GameReflection.Enumerate(Members.ReadObject(manager, "Deliveries"));
    }

    internal static string StatusName(int status) => status switch
    {
        0 => "InTransit",
        1 => "Waiting",
        2 => "Arrived",
        3 => "Completed",
        _ => "unknown",
    };

    private static bool IsVanilla => Math.Abs(_multiplier - 1f) < 0.0001f;
}
