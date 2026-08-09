using Expansions.Core.Diagnostics;

namespace Expansions.Tweaks.Runtime;

/// <summary>
/// A configurable weekly ATM deposit ceiling, without writing the ceiling.
/// <para>
/// <c>ATM.WeeklyDepositLimit</c> is a <c>public const float</c>. IL2CPP gives a const no storage and
/// bakes it into every call site, so writing it is not merely useless — <c>il2cpp_field_static_set_value</c>
/// has no literal guard and would memcpy into <c>static_fields + offset</c> on a class that may have
/// no static block at all. That is an access violation the process does not survive, and no managed
/// <c>catch</c> can help. Core's <see cref="GameReflection.TryWriteStatic"/> refuses it; the probe
/// reports the verdict rather than testing it.
/// </para>
/// <para>
/// So the ceiling is moved by shifting the other side of the subtraction instead. Every allowance
/// check in the game is <c>WeeklyDepositLimit - ATM.WeeklyDepositSum</c>, and
/// <c>WeeklyDepositSum</c> is a real mutable static. Holding it at <c>true sum - (wanted - 10000)</c>
/// makes that expression evaluate to <c>wanted - true sum</c> everywhere at once: the enabled amount
/// buttons, the clamp on the selected amount, and the remaining-allowance text. Crucially it also
/// works where the getter was inlined, because an inlined copy still reads the same field.
/// </para>
/// <para>
/// The counter is saved to <c>Money.json</c>, so the offset is lifted for the duration of
/// <c>MoneyManager.GetSaveString()</c> and put back afterwards. Nothing shifted ever reaches disk,
/// and disabling the module puts the live counter back to its true value.
/// </para>
/// </summary>
internal sealed class DepositLimit
{
    private const string SumField = "WeeklyDepositSum";

    private const string LimitField = "WeeklyDepositLimit";

    private Type? _atm;
    private float _vanillaLimit;
    private float _offset;
    private bool _active;

    /// <summary>
    /// The exact value last written to the counter, and whether one has been written yet. This is the
    /// idempotence key for <see cref="Reconcile"/>: <c>ATM.WeekPass()</c> is an instance method, so a
    /// save with five ATMs in it fires the weekly reset five times, and an unconditional re-shift
    /// would subtract the offset once per machine.
    /// </summary>
    private float _lastWritten;

    private bool _hasLastWritten;

    /// <summary>The caption as this last left it, so an unchanged one costs a string compare.</summary>
    private string _lastCaption = string.Empty;

    /// <summary>True once the offset is being maintained on the live counter.</summary>
    internal bool IsActive => _active;

    internal float Offset => _offset;

    internal float VanillaLimit => _vanillaLimit;

    /// <summary>The ceiling in force right now, whether or not this module set it.</summary>
    internal float EffectiveLimit => _active ? _vanillaLimit + _offset : _vanillaLimit;

    /// <summary>What the game's own const says, and whether it is one.</summary>
    internal Il2CppFieldFacts LimitFacts => Facts(LimitField);

    internal Il2CppFieldFacts SumFacts => Facts(SumField);

    /// <summary>The player's real week-to-date deposits, with our shift taken back out.</summary>
    internal float TrueSum => RawSum + (_active ? _offset : 0f);

    /// <summary>What the field currently holds, which is deliberately not the true sum while active.</summary>
    internal float RawSum => Members.ReadStatic(Resolve(), SumField, 0f);

    /// <summary>What the game will tell the player they may still bank this week.</summary>
    internal float RemainingAllowance => EffectiveLimit - TrueSum;

    /// <summary>
    /// Starts maintaining the offset. Safe to call repeatedly: a second call with the same ceiling is
    /// a no-op, and a call with a different one re-bases rather than stacking.
    /// </summary>
    internal bool Apply(float? wantedLimit)
    {
        var atm = Resolve();
        if (atm is null)
        {
            TweakLog.Warn($"'{GameTypes.Atm}' is not on this build; the deposit limit is left vanilla.");
            return false;
        }

        if (_vanillaLimit <= 0f)
            _vanillaLimit = Members.ReadStatic(atm, LimitField, 10_000f);

        if (wantedLimit is not { } wanted || Math.Abs(wanted - _vanillaLimit) < 0.5f)
        {
            Restore();
            return true;
        }

        var offset = wanted - _vanillaLimit;
        if (_active && Math.Abs(offset - _offset) < 0.5f)
            return true;

        Restore();

        if (!SumFacts.IsSafeToWrite)
        {
            TweakLog.Warn(
                $"'{GameTypes.Atm}.{SumField}' is not a writable static on this build " +
                $"({SumFacts.Explain(SumField)}); the deposit limit is left vanilla.");
            return false;
        }

        _offset = offset;
        _active = true;

        if (!Shift(-_offset))
        {
            _active = false;
            _offset = 0f;
            return false;
        }

        TweakLog.Msg(
            $"Weekly ATM deposit limit {Money(_vanillaLimit)} -> {Money(wanted)} " +
            $"(the game's own const is untouched; the week-to-date counter carries the difference).");

        return true;
    }

    /// <summary>Puts the true sum back and stops maintaining the offset.</summary>
    internal void Restore()
    {
        if (!_active)
            return;

        Shift(_offset);
        _active = false;
        _offset = 0f;
        _hasLastWritten = false;
        _lastCaption = string.Empty;
    }

    /// <summary>
    /// Re-applies the shift after the game has overwritten the counter with a true value — the weekly
    /// reset and a save load. A deposit needs nothing, because it is a relative change to a counter
    /// that is already shifted.
    /// <para>
    /// The guard is what makes this safe to fire more than once. There is no way to ask the field
    /// whether it is currently shifted, so the last value written is remembered instead: if the field
    /// still holds it, the game has not touched it since and there is nothing to convert. Without
    /// that, the five ATMs in a large save would each subtract the offset again on the same weekly
    /// reset and the ceiling would drift by a factor of five.
    /// </para>
    /// </summary>
    internal void Reconcile(string because)
    {
        if (!_active)
            return;

        var current = ReadRaw();
        if (float.IsNaN(current))
            return;

        if (_hasLastWritten && Math.Abs(current - _lastWritten) < 0.001f)
            return;

        if (SetRaw(current - _offset))
            TweakLog.Detail($"Re-applied the deposit-limit offset after {because} ({current} -> {current - _offset}).");
    }

    /// <summary>Takes the shift back out for the duration of a save write.</summary>
    internal void LiftForSave()
    {
        if (_active)
            Shift(_offset);
    }

    internal void RestoreAfterSave()
    {
        if (_active)
            Shift(-_offset);
    }

    /// <summary>
    /// Repairs the ATM's remaining-allowance caption when it has rendered something the shift made
    /// wrong. Two artefacts are possible and both are matched exactly rather than guessed at: the
    /// vanilla ceiling printed as a literal, and the shifted counter printed as a negative amount.
    /// </summary>
    internal void RepairCaption(object? atmInterface)
    {
        if (!_active || atmInterface is null)
            return;

        var label = Members.ReadObject(atmInterface, "depositLimitText");
        if (label is null)
            return;

        var text = Members.Read(label, "text", string.Empty);
        if (text.Length == 0)
            return;

        // This runs every frame the ATM is on screen, and the caption only changes when the player
        // does something. Recognising our own last output keeps the steady state to one comparison.
        if (string.Equals(text, _lastCaption, StringComparison.Ordinal))
            return;

        var repaired = text;

        // The counter first: a negative amount can only have come from the shift.
        var raw = RawSum;
        if (raw < 0f)
            repaired = Replace(repaired, raw, TrueSum);

        // The ceiling second, and only when the remaining allowance is not itself sitting on the
        // vanilla figure — otherwise a legitimate "$10,000 left" would be rewritten into a lie.
        if (Math.Abs(RemainingAllowance - _vanillaLimit) > 0.5f)
            repaired = Replace(repaired, _vanillaLimit, EffectiveLimit);

        _lastCaption = repaired;

        if (!string.Equals(repaired, text, StringComparison.Ordinal))
            Members.Write(label, "text", repaired);
    }

    private static string Replace(string text, float from, float to)
    {
        var needle = Money(from);
        return needle.Length == 0 ? text : text.Replace(needle, Money(to), StringComparison.Ordinal);
    }

    private bool Shift(float delta)
    {
        var current = ReadRaw();
        return !float.IsNaN(current) && SetRaw(current + delta);
    }

    private float ReadRaw() => Members.ReadStatic(Resolve(), SumField, float.NaN);

    /// <summary>
    /// The only place the counter is written. Recording the value here is what lets
    /// <see cref="Reconcile"/> tell "the game reset this" from "this is still ours".
    /// </summary>
    private bool SetRaw(float value)
    {
        var atm = Resolve();
        if (atm is null)
            return false;

        if (!GameReflection.TryWriteStatic(atm, SumField, value, out var failure))
        {
            TweakLog.Warn($"Could not move '{GameTypes.Atm}.{SumField}': {failure}");
            return false;
        }

        _lastWritten = value;
        _hasLastWritten = true;
        return true;
    }

    private Il2CppFieldFacts Facts(string member) =>
        Il2CppFieldFacts.Inspect(Resolve()!, member);

    private Type? Resolve() => _atm ??= GameReflection.FindType(GameTypes.Atm);

    /// <summary>
    /// The game's own currency rendering, so a caption repair substitutes strings the game itself
    /// produced. Falling back to our own format would not match, and the repair would do nothing —
    /// which is the correct failure, not a wrong number.
    /// </summary>
    private static string Money(float amount)
    {
        // Memoised because the caption repair asks for the same two or three amounts on every frame
        // the ATM is open, and each miss is a reflective invoke.
        if (MoneyStrings.TryGetValue(amount, out var cached))
            return cached;

        var formatted = FormatMoney(amount);

        if (MoneyStrings.Count < 256)
            MoneyStrings[amount] = formatted;

        return formatted;
    }

    private static readonly Dictionary<float, string> MoneyStrings = new();

    private static string FormatMoney(float amount)
    {
        var type = GameReflection.FindType(GameTypes.MoneyManager);
        if (type is null)
            return string.Empty;

        return GameReflection.TryInvokeExact(
            type,
            null,
            "FormatAmount",
            new[] { typeof(float), typeof(bool), typeof(bool) },
            new object?[] { amount, false, false },
            out var result,
            out _) && result is string text
            ? text
            : string.Empty;
    }
}
