using System.Reflection;
using Expansions.Core.Diagnostics;

namespace Expansions.SpecialCustomers.Game;

/// <summary>
/// Every game type name this module touches, in one place.
/// <para>
/// The whole module holds zero compile-time references to <c>Assembly-CSharp</c> or FishNet, so a
/// type TVGS renames in the next patch costs one feature and a logged warning rather than a
/// <c>TypeLoadException</c> that would stop the assembly loading and take Core down with it.
/// </para>
/// </summary>
internal static class GameTypes
{
    internal const string NpcManager = "Il2CppScheduleOne.NPCs.NPCManager";
    internal const string Npc = "Il2CppScheduleOne.NPCs.NPC";
    internal const string Customer = "Il2CppScheduleOne.Economy.Customer";
    internal const string CustomerData = "Il2CppScheduleOne.Economy.CustomerData";
    internal const string CustomerStandard = "Il2CppScheduleOne.Economy.ECustomerStandard";
    internal const string DrugType = "Il2CppScheduleOne.Product.EDrugType";
    internal const string MapRegion = "Il2CppScheduleOne.Map.EMapRegion";
    internal const string Map = "Il2CppScheduleOne.Map.Map";
    internal const string SaveManager = "Il2CppScheduleOne.Persistence.SaveManager";
    internal const string LoadManager = "Il2CppScheduleOne.Persistence.LoadManager";
    internal const string AvatarSettings = "Il2CppScheduleOne.AvatarFramework.AvatarSettings";
    internal const string LayerSetting = "Il2CppScheduleOne.AvatarFramework.AvatarSettings+LayerSetting";
    internal const string AccessorySetting = "Il2CppScheduleOne.AvatarFramework.AvatarSettings+AccessorySetting";
    internal const string EconomyNamespace = "Il2CppScheduleOne.Economy";

    /// <summary>
    /// Invokes an overload picked by parameter type rather than by argument count.
    /// <para>
    /// <see cref="GameReflection.TryInvoke"/> matches on arity, which is ambiguous for the handful
    /// of game and Unity methods that carry two one-argument overloads —
    /// <c>GameObject.GetComponent(string)</c> versus <c>GetComponent(Il2CppSystem.Type)</c> being the
    /// one that actually bites.
    /// </para>
    /// </summary>
    internal static bool TryInvokeExact(
        Type declaringType,
        object? instance,
        string methodName,
        Type[] parameterTypes,
        object?[] arguments,
        out object? result,
        out string failure)
    {
        result = null;

        MethodInfo? method = null;
        for (var current = declaringType; current is not null && method is null; current = current.BaseType)
        {
            foreach (var candidate in current.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                         BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (candidate.Name != methodName)
                    continue;

                var parameters = candidate.GetParameters();
                if (parameters.Length != parameterTypes.Length)
                    continue;

                var matches = true;
                for (var i = 0; i < parameters.Length; i++)
                {
                    if (!parameters[i].ParameterType.IsAssignableFrom(parameterTypes[i]))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    method = candidate;
                    break;
                }
            }
        }

        if (method is null)
        {
            failure = $"'{declaringType.Name}.{methodName}' has no overload taking ({string.Join(", ", parameterTypes.Select(t => t.Name))})";
            return false;
        }

        try
        {
            result = method.Invoke(method.IsStatic ? null : instance, arguments);
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = Describe.Of(ex);
            return false;
        }
    }

    /// <summary>Boxes a managed int into a game enum so it can be passed to a game method.</summary>
    internal static object? ToEnum(string enumTypeName, int value)
    {
        var type = GameReflection.FindType(enumTypeName);

        try
        {
            return type is null ? null : Enum.ToObject(type, value);
        }
        catch
        {
            return null;
        }
    }
}
