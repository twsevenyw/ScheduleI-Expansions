namespace Expansions.SpecialCustomers.Archetypes;

/// <summary>
/// Deterministic pseudo-randomness, seeded by hand rather than by the clock.
/// <para>
/// Deliberately not <c>UnityEngine.Random</c>. Two things depend on the same inputs always producing
/// the same output: a co-op guest has to dress the same pool slot the same way the host did, and a
/// group has to look identical after a save/load instead of re-rolling into strangers. Feeding it a
/// stable seed — archetype, pool slot, visit — is what buys both.
/// </para>
/// </summary>
internal struct LookRandom
{
    private uint _state;

    internal LookRandom(string label, int a, int b = 0)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in label)
                hash = (hash ^ c) * 16777619u;

            hash = (hash ^ (uint)a) * 16777619u;
            hash = (hash ^ (uint)b) * 16777619u;

            // xorshift32 degenerates to a fixed point on zero, so the low bit is forced.
            _state = hash | 1u;
        }
    }

    internal int Next(int exclusiveMax)
    {
        if (exclusiveMax <= 1)
            return 0;

        return (int)(NextBits() % (uint)exclusiveMax);
    }

    internal float Range(float min, float max) => min + ((max - min) * (NextBits() / (float)uint.MaxValue));

    internal bool Chance(float probability) => Range(0f, 1f) < probability;

    internal T Pick<T>(params T[] options) => options[Next(options.Length)];

    /// <summary>
    /// Draws from a pool, always consuming exactly one number even when the pool is empty, so adding
    /// an option to one wardrobe cannot shift every later draw for that member.
    /// </summary>
    internal T? PickOrDefault<T>(IReadOnlyList<T> options)
    {
        var index = Next(Math.Max(1, options.Count));
        return options.Count == 0 ? default : options[index];
    }

    /// <summary>xorshift32: tiny, allocation-free and identical on every runtime and platform.</summary>
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
