namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Deterministic pseudo-randomness for police event timing.
/// <para>
/// Deliberately not <c>UnityEngine.Random</c>: a save reload and a co-op guest must schedule the same
/// next fire from the same seed. Hand-rolled xorshift32, same shape as Special Customers' look RNG.
/// </para>
/// </summary>
internal struct PoliceRng
{
    private uint _state;

    internal PoliceRng(uint seed) => _state = seed == 0 ? 1u : seed;

    /// <summary>Builds a non-zero seed from a save-stable label and one or more integers.</summary>
    internal static uint SeedFor(string label, params int[] parts)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in label)
                hash = (hash ^ c) * 16777619u;

            foreach (var part in parts)
                hash = (hash ^ (uint)part) * 16777619u;

            return hash | 1u;
        }
    }

    internal int Next(int exclusiveMax)
    {
        if (exclusiveMax <= 1)
            return 0;

        return (int)(NextBits() % (uint)exclusiveMax);
    }

    /// <summary>Inclusive range.</summary>
    internal int RangeInclusive(int min, int max)
    {
        if (max <= min)
            return min;

        return min + Next((max - min) + 1);
    }

    internal float Unit() => NextBits() / (float)uint.MaxValue;

    internal bool Chance(float probability) => Unit() < probability;

    private uint NextBits()
    {
        unchecked
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state;
        }
    }
}
