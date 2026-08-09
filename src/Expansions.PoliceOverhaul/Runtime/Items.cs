namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// What an item is, in the two terms this module cares about: is it the product an arrest should
/// seize, and is it the kit a repeat offender should lose.
/// <para>
/// Category is read as a string rather than boxed back into the game's enum, because the enum lives
/// in <c>ScheduleOne.Core</c> and gained three members between builds — comparing names survives a
/// re-ordering that comparing ordinals would not.
/// </para>
/// </summary>
internal static class Items
{
    /// <summary>
    /// What the police take off you. Product only: a packaged brick is already category Product, so
    /// the Packaging category is nothing but empty baggies and jars, and seizing those is petty rather
    /// than punishing.
    /// </summary>
    private static readonly string[] ContrabandCategories = { "Product" };

    /// <summary>
    /// What a repeat offender loses on top of the product. Deliberately excludes Agriculture,
    /// Furniture, Lighting and Storage: seizing someone's pots and lamps is theft, not policing, and
    /// it would silently gut a farm the player then has to rebuild by hand.
    /// </summary>
    private static readonly string[] EquipmentCategories = { "Tools", "Equipment" };

    internal static string CategoryOf(object? instance)
    {
        var category = Members.ReadPath(instance, "Definition.Category");
        return category?.ToString() ?? string.Empty;
    }

    internal static bool IsContraband(object? instance) => Matches(instance, ContrabandCategories);

    internal static bool IsEquipment(object? instance) => Matches(instance, EquipmentCategories);

    internal static string NameOf(object? instance)
    {
        var name = Members.Read(Members.ReadPath(instance, "Definition"), "Name", string.Empty);
        if (name.Length > 0)
            return name;

        var id = Members.Read(Members.ReadPath(instance, "Definition"), "ID", string.Empty);
        return id.Length > 0 ? id : "an item";
    }

    internal static int QuantityOf(object? instance) => Math.Max(1, Members.Read(instance, "Quantity", 1));

    /// <summary>
    /// What the stack is worth. Used only for the running seizure total that federal triggers read,
    /// so an item type that cannot price itself contributes nothing rather than blocking the seizure.
    /// </summary>
    internal static float ValueOf(object? instance)
    {
        if (Members.InvokeFor(instance, "GetMonetaryValue") is float unit && unit > 0f)
            return unit * QuantityOf(instance);

        return 0f;
    }

    private static bool Matches(object? instance, string[] categories)
    {
        var category = CategoryOf(instance);
        if (category.Length == 0)
            return false;

        foreach (var candidate in categories)
        {
            if (string.Equals(category, candidate, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
