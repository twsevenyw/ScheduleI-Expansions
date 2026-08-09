namespace Expansions.SpecialCustomers.Visitors;

/// <summary>
/// The visitor types this mod contributes, in the one order every peer will agree on.
/// <para>
/// FishNet spawnable registration is index-based, so two peers that register the same prefabs in a
/// different order disagree about what every prefab id means. S1API 3.1.7 already sorts the types it
/// discovers by <c>Type.FullName</c> with <c>StringComparer.Ordinal</c> before pre-registering them,
/// and this list uses the same key so an explicit pass produces the same relative order as the scan.
/// </para>
/// </summary>
internal static class VisitorRoster
{
    /// <summary>The prefix S1API prepends to the simple type name when it names the prefab.</summary>
    internal const string PrefabNamePrefix = "S1API_";

    internal static IReadOnlyList<Type> Types { get; } = Order(new[]
    {
        typeof(SpecialVisitor01),
        typeof(SpecialVisitor02),
        typeof(SpecialVisitor03),
        typeof(SpecialVisitor04),
        typeof(SpecialVisitor05),
        typeof(SpecialVisitor06),
        typeof(SpecialVisitor07),
        typeof(SpecialVisitor08),
    });

    internal static string PrefabNameOf(Type type) => PrefabNamePrefix + type.Name;

    /// <summary>Prefab names in registration order — the co-op ordering the probe reports.</summary>
    internal static IReadOnlyList<string> PrefabNames { get; } =
        Types.Select(PrefabNameOf).ToArray();

    private static Type[] Order(Type[] types)
    {
        Array.Sort(types, static (a, b) => string.CompareOrdinal(a.FullName, b.FullName));
        return types;
    }
}
