using Expansions.HireableDrivers.Config;
using Expansions.HireableDrivers.Game;

namespace Expansions.HireableDrivers.Runtime;

/// <summary>
/// How many drivers a property can hold.
/// <para>
/// A driver slot is its own budget, deliberately separate from <c>Property.EmployeeCapacity</c>: every
/// property gets one regardless of how many botanists, handlers, chemists and cleaners are already
/// there, and the docks warehouse gets two because it is the freight property.
/// </para>
/// <para>
/// Keys are real <c>Property.propertyCode</c> values, read out of shipped save data rather than guessed
/// from display names: <c>barn</c>, <c>bungalow</c>, <c>dockswarehouse</c>, <c>manor</c>,
/// <c>motelroom</c>, <c>rv</c>, <c>seweroffice</c>, <c>storageunit</c>, <c>sweatshop</c>, and the
/// businesses <c>carwash</c>, <c>laundromat</c>, <c>postoffice</c>, <c>tacoticklers</c>.
/// </para>
/// </summary>
internal static class DriverCapacity
{
    internal const string DocksWarehouseCode = "dockswarehouse";

    /// <summary><c>*</c> is the fallback for any property the map does not name.</summary>
    internal const string DefaultMap = "*=1,dockswarehouse=2";

    private static readonly Dictionary<string, int> Parsed = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    private static string _parsedFrom = string.Empty;
    private static int _fallback = 1;

    internal static int For(string? propertyCode)
    {
        EnsureParsed();

        lock (Gate)
        {
            return propertyCode is { Length: > 0 } && Parsed.TryGetValue(propertyCode, out var slots)
                ? slots
                : _fallback;
        }
    }

    internal static int ForProperty(object? property) => For(WorldApi.PropertyCode(property));

    internal static int Used(string? propertyCode) => DriverRegistry.Drivers.Count(driver =>
        string.Equals(driver.Record.HomePropertyCode, propertyCode, StringComparison.OrdinalIgnoreCase));

    internal static int Free(string? propertyCode) => Math.Max(0, For(propertyCode) - Used(propertyCode));

    internal static bool HasRoom(object? property) => Free(WorldApi.PropertyCode(property)) > 0;

    /// <summary>"1/2 driver slots used" — the phrasing the hiring choice and the diagnostics both use.</summary>
    internal static string Describe(string? propertyCode) => $"{Used(propertyCode)}/{For(propertyCode)} driver slots used";

    /// <summary>
    /// Parses lazily and only when the setting string actually changed, because the availability
    /// predicate behind a dialogue choice is re-evaluated every time the interaction list is drawn.
    /// </summary>
    private static void EnsureParsed()
    {
        var raw = DriverSettings.DriverSlotsPerProperty;

        lock (Gate)
        {
            if (string.Equals(raw, _parsedFrom, StringComparison.Ordinal))
                return;

            Parsed.Clear();
            _fallback = 1;
            _parsedFrom = raw;

            foreach (var entry in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var split = entry.IndexOf('=');
                if (split <= 0)
                    continue;

                var key = entry[..split].Trim();
                if (!int.TryParse(entry[(split + 1)..].Trim(), out var slots))
                    continue;

                slots = Math.Clamp(slots, 0, 16);

                if (key == "*")
                    _fallback = slots;
                else
                    Parsed[key] = slots;
            }
        }
    }
}
