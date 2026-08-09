using Expansions.Core.Diagnostics;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// The handful of global police tuning statics that are actually writable, plus the machinery that
/// proves which ones those are.
/// <para>
/// This started as the mod's primary lever and is now a footnote, because 28 of the 31 candidate
/// statics turned out to be C# <c>const</c>. That matters far beyond "the write does nothing":
/// Il2CppInterop projects a const and a real static as the same read/write property, and the setter
/// is a bare <c>il2cpp_field_static_set_value</c> with no literal guard. On a class whose statics are
/// all const there is no static-field block, so the write lands near null and kills the process
/// outright — it has already done so once in this project. Every write here therefore goes through
/// <c>GameReflection.TryWriteStatic</c>, which refuses a const-inlined field before the native call.
/// </para>
/// </summary>
internal sealed class LawLevers
{
    /// <summary>
    /// The three levers the metadata says are real mutable statics, plus their vanilla defaults for
    /// the report. Candidacy is still re-checked at runtime rather than trusted from this list.
    /// </summary>
    private static readonly (string Type, string Member, string Purpose)[] Candidates =
    {
        (GameTypes.VisionCone, "UniversalAttentivenessScale", "how fast every NPC notices you"),
        (GameTypes.VisionCone, "UniversalMemoryScale", "how long every NPC stays suspicious"),
        (GameTypes.LawManager, "DISPATCH_VEHICLE_USE_THRESHOLD", "how readily dispatch sends a cruiser instead of a foot unit"),
    };

    private readonly Dictionary<string, object?> _originals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LeverStatus> _status = new(StringComparer.Ordinal);

    internal IReadOnlyDictionary<string, LeverStatus> Status => _status;

    /// <summary>Decides writability once, without writing anything. Safe to call before a save loads.</summary>
    internal void Survey()
    {
        _status.Clear();

        foreach (var (typeName, member, purpose) in Candidates)
        {
            var key = Key(typeName, member);
            var type = GameReflection.FindType(typeName);

            if (type is null)
            {
                _status[key] = new LeverStatus(key, purpose, false, $"type '{typeName}' is not on this build");
                continue;
            }

            var facts = Il2CppFieldFacts.Inspect(type, member);
            _status[key] = new LeverStatus(key, purpose, facts.IsSafeToWrite, facts.Explain(member));
        }

        var writable = _status.Values.Count(s => s.IsWritable);
        PoliceLog.Msg($"Global police levers: {writable}/{_status.Count} writable. Everything else runs off per-officer and Harmony fallbacks.");
    }

    internal bool IsWritable(string typeName, string member) =>
        _status.TryGetValue(Key(typeName, member), out var status) && status.IsWritable;

    /// <summary>
    /// Writes a static, snapshotting the vanilla value the first time so <see cref="Restore"/> has
    /// something to put back. A refusal is logged once and then silent.
    /// </summary>
    internal void Set(string typeName, string member, object value)
    {
        var key = Key(typeName, member);
        if (!_status.TryGetValue(key, out var status) || !status.IsWritable)
            return;

        var type = GameReflection.FindType(typeName);
        if (type is null)
            return;

        if (!_originals.ContainsKey(key))
        {
            if (!GameReflection.TryReadStatic(type, member, out var original, out var readFailure))
            {
                PoliceLog.Warn($"Could not snapshot {key} ({readFailure}); leaving it alone rather than risk not being able to restore it.");
                _status[key] = status.Blocked($"unreadable: {readFailure}");
                return;
            }

            _originals[key] = original;
        }

        if (!GameReflection.TryWriteStatic(type, member, value, out var failure))
        {
            PoliceLog.Warn($"Refusing to write {key}: {failure}");
            _status[key] = status.Blocked(failure);
            _originals.Remove(key);
        }
    }

    /// <summary>Puts every touched static back. Idempotent, and safe from a half-initialised state.</summary>
    internal void Restore()
    {
        foreach (var (key, original) in _originals)
        {
            var split = key.IndexOf('.');
            if (split <= 0)
                continue;

            var type = GameReflection.FindType(key[..split]);
            if (type is null)
                continue;

            if (!GameReflection.TryWriteStatic(type, key[(split + 1)..], original, out var failure))
                PoliceLog.Warn($"Could not restore {key}: {failure}");
        }

        _originals.Clear();
    }

    private static string Key(string typeName, string member) => typeName + "." + member;

    internal readonly struct LeverStatus
    {
        internal LeverStatus(string key, string purpose, bool isWritable, string verdict)
        {
            Key = key;
            Purpose = purpose;
            IsWritable = isWritable;
            Verdict = verdict;
        }

        internal string Key { get; }

        /// <summary>What the lever does, in words the owner can act on.</summary>
        internal string Purpose { get; }

        internal bool IsWritable { get; }

        /// <summary>Why it is or is not writable, straight from the IL2CPP field flags.</summary>
        internal string Verdict { get; }

        internal LeverStatus Blocked(string reason) => new(Key, Purpose, false, reason);
    }
}
