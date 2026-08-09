using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace Expansions.SpecialCustomers;

/// <summary>Short, allocation-cheap renderings for log lines and probe cells.</summary>
internal static class Describe
{
    internal static string Of(Exception exception)
    {
        var inner = exception is TargetInvocationException { InnerException: { } target } ? target : exception;
        return $"{inner.GetType().Name}: {inner.Message}";
    }

    internal static string Of(Vector3 value) => string.Format(
        CultureInfo.InvariantCulture,
        "{0:0.##}, {1:0.##}, {2:0.##}",
        value.x,
        value.y,
        value.z);

    internal static string Metres(float value) => value.ToString("0.#", CultureInfo.InvariantCulture) + " m";

    internal static string YesNo(bool value) => value ? "yes" : "no";
}
