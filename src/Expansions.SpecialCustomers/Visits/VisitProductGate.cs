using System.Reflection;
using Expansions.Core.Diagnostics;
using Expansions.SpecialCustomers.Configuration;
using Expansions.SpecialCustomers.Game;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;

namespace Expansions.SpecialCustomers.Visits;

/// <summary>
/// Keeps every in-town visitor purchase inside <c>product_allow_list</c>.
/// <para>
/// Leader bulk contracts are authored through <see cref="OfferFactory"/> and already honour the
/// list. Member street deals go through the game's own generator
/// (<c>GetWeightedRandomProduct</c> → <c>GetProductEnjoyment</c>), which otherwise picks from every
/// listed product. Affinity tuning alone is drug-type-granular and cannot pin Granddaddy Purple vs
/// any other weed, so this gate zeroes enjoyment for off-list products on live visitors — before the
/// offer text is built, not by rewriting the message afterwards.
/// </para>
/// <para>
/// Ownership is a managed <see cref="IntPtr"/> set. The Harmony postfix never dereferences the
/// customer to answer "is ours?".
/// </para>
/// </summary>
internal static class VisitProductGate
{
    private static readonly object Gate = new();
    private static readonly HashSet<IntPtr> LiveCustomers = new();

    private static bool _patched;

    internal static void Register(object? customer)
    {
        if (customer is not Il2CppObjectBase native || native.Pointer == IntPtr.Zero)
            return;

        lock (Gate)
            LiveCustomers.Add(native.Pointer);
    }

    internal static void Unregister(object? customer)
    {
        if (customer is not Il2CppObjectBase native || native.Pointer == IntPtr.Zero)
            return;

        lock (Gate)
            LiveCustomers.Remove(native.Pointer);
    }

    internal static void Clear()
    {
        lock (Gate)
            LiveCustomers.Clear();
    }

    internal static bool Apply(HarmonyLib.Harmony harmony)
    {
        lock (Gate)
        {
            if (_patched)
                return true;

            var customerType = GameReflection.FindType(GameTypes.Customer);
            if (customerType is null)
            {
                VisitorLog.Instance.Warn(
                    $"Could not patch product enjoyment: '{GameTypes.Customer}' not found. " +
                    "Member deals will not be constrained to the allow-list.");
                return false;
            }

            var productType = GameReflection.FindType("Il2CppScheduleOne.Product.ProductDefinition");
            var qualityType = GameReflection.FindType("Il2CppScheduleOne.ItemFramework.EQuality");
            var patched = 0;

            if (productType is not null)
            {
                var plain = AccessTools.Method(customerType, "GetProductEnjoyment", new[] { productType });
                if (plain is not null)
                {
                    harmony.Patch(
                        plain,
                        postfix: new HarmonyMethod(typeof(VisitProductGate).GetMethod(
                            nameof(AfterGetProductEnjoyment),
                            BindingFlags.NonPublic | BindingFlags.Static)));
                    patched++;
                }

                if (qualityType is not null)
                {
                    var withQuality = AccessTools.Method(
                        customerType, "GetProductEnjoyment", new[] { productType, qualityType });
                    if (withQuality is not null)
                    {
                        harmony.Patch(
                            withQuality,
                            postfix: new HarmonyMethod(typeof(VisitProductGate).GetMethod(
                                nameof(AfterGetProductEnjoymentWithQuality),
                                BindingFlags.NonPublic | BindingFlags.Static)));
                        patched++;
                    }
                }
            }

            if (patched == 0)
            {
                VisitorLog.Instance.Warn(
                    "Could not resolve Customer.GetProductEnjoyment for the allow-list gate. " +
                    "Member deals will not be constrained to the allow-list.");
                return false;
            }

            _patched = true;
            VisitorLog.Instance.Debug($"Visit product allow-list gate applied ({patched} enjoyment patch site(s)).");
            return true;
        }
    }

    /// <summary>Module Harmony unpatches on disable; this just forgets the local latch.</summary>
    internal static void Disarm()
    {
        lock (Gate)
        {
            _patched = false;
            LiveCustomers.Clear();
        }
    }

    private static bool IsLiveVisitor(object? customer)
    {
        if (customer is not Il2CppObjectBase native || native.Pointer == IntPtr.Zero)
            return false;

        lock (Gate)
            return LiveCustomers.Contains(native.Pointer);
    }

    private static void AfterGetProductEnjoyment(object __instance, object product, ref float __result) =>
        Constrain(__instance, product, ref __result);

    private static void AfterGetProductEnjoymentWithQuality(
        object __instance, object product, object quality, ref float __result) =>
        Constrain(__instance, product, ref __result);

    private static void Constrain(object? customer, object? product, ref float enjoyment)
    {
        if (!CustomerSettings.EnforceAllowListOnMemberDeals)
            return;

        if (!IsLiveVisitor(customer))
            return;

        var allowed = ProductAllowList.Current();
        if (!allowed.IsRestricted || allowed.Matched.Count == 0)
            return;

        if (product is null || !IsAllowed(product, allowed))
        {
            enjoyment = 0f;
            return;
        }

        // Preferred drug missing from the list still has to land on something allowed — keep listed
        // products above the shipped appeal floor so the generator does not walk away empty-handed.
        if (enjoyment < 0.25f)
            enjoyment = 0.25f + (0.5f * RelativeValue(product, allowed));
    }

    private static bool IsAllowed(object product, ProductAllowList.Resolution allowed)
    {
        string id = ReadString(product, "ID");
        string name = ReadString(product, "Name");

        foreach (var match in allowed.Matched)
        {
            try
            {
                if (id.Length > 0 && string.Equals(match.Product.ID, id, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (name.Length > 0 && string.Equals(match.Product.Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
                // A definition that cannot identify itself cannot be matched.
            }
        }

        return false;
    }

    private static float RelativeValue(object product, ProductAllowList.Resolution allowed)
    {
        float best = 1f;
        foreach (var match in allowed.Matched)
        {
            try
            {
                best = Math.Max(best, match.Product.MarketValue);
            }
            catch
            {
                // Skip unreadable definitions.
            }
        }

        var value = ReadFloat(product, "MarketValue");
        if (value <= 0f || best <= 0f)
            return 0.5f;

        return MathfClamp01(value / best);
    }

    private static string ReadString(object instance, string member) =>
        GameReflection.TryRead(instance, member, out var value, out _)
            ? GameReflection.Format(value)
            : string.Empty;

    private static float ReadFloat(object instance, string member) =>
        GameReflection.TryRead(instance, member, out var value, out _) && value is float number
            ? number
            : 0f;

    private static float MathfClamp01(float value) =>
        value < 0f ? 0f : value > 1f ? 1f : value;
}
