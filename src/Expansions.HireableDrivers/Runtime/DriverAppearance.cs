using Expansions.HireableDrivers.Game;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace Expansions.HireableDrivers.Runtime;

/// <summary>
/// Gives repurposed Packagers a distinct delivery-driver uniform without touching the shared
/// Handler prefab, NPCData, appearance index, or save schema.
/// <para>
/// The original live AvatarSettings asset is retained for teardown and one dressed copy is cloned.
/// Face, hair, tattoos, glasses and facial hair remain personal; only top/bottom/footwear, waistwear
/// and headwear become the driver uniform.
/// </para>
/// </summary>
internal static class DriverAppearance
{
    private const string RolledShirt = "Avatar/Layers/Top/RolledButtonup";
    private const string CargoPants = "Avatar/Layers/Bottom/CargoPants";
    private const string Boots = "Avatar/Accessories/Feet/CombatBoots/CombatBoots";
    private const string Belt = "Avatar/Accessories/Waist/Belt/Belt";
    private const string Cap = "Avatar/Accessories/Head/Cap/Cap";

    private static readonly Color ShirtBlue = new(0.12f, 0.31f, 0.55f, 1f);
    private static readonly Color PantsCharcoal = new(0.11f, 0.13f, 0.16f, 1f);
    private static readonly Color BootBlack = new(0.035f, 0.035f, 0.04f, 1f);
    private static readonly Color CapBlue = new(0.10f, 0.27f, 0.48f, 1f);

    private static readonly Dictionary<string, Look> Looks = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    internal static bool IsApplied(string employeeId)
    {
        lock (Gate)
            return Looks.ContainsKey(employeeId);
    }

    internal static bool Apply(DriverBrain brain)
    {
        var employee = brain.Employee;
        if (employee is null)
            return false;

        return Apply(brain.Record.EmployeeId, brain.Name, employee);
    }

    internal static bool Apply(string employeeId, string displayName, object employee)
    {
        var pointer = Gx.PointerOf(employee);
        lock (Gate)
        {
            if (Looks.TryGetValue(employeeId, out var existing) &&
                existing.EmployeePointer == pointer)
            {
                var liveAvatar = Gx.GetAlive(employee, "Avatar");
                var liveSettings = Gx.Cast(Gx.Get(liveAvatar, "CurrentSettings"), GameTypes.AvatarSettings);
                if (Gx.PointerOf(liveSettings) == Gx.PointerOf(existing.Dressed))
                    return true;

                if (Load(employee, existing.Dressed))
                    return true;

                Looks.Remove(employeeId);
                Destroy(existing);
                return false;
            }
        }

        var avatar = Gx.GetAlive(employee, "Avatar");
        var current = Gx.Cast(Gx.Get(avatar, "CurrentSettings"), GameTypes.AvatarSettings);
        if (avatar is null || current is not UnityObject donor || donor == null)
        {
            DriverLog.Warn($"{displayName}: could not read Avatar.CurrentSettings; keeping the base Handler outfit.");
            return false;
        }

        var dressed = Clone(donor, $"DriverUniform_{employeeId}");
        if (dressed is null)
        {
            Destroy(dressed);
            return false;
        }

        if (!Dress(dressed))
        {
            Destroy(dressed);
            DriverLog.Warn($"{displayName}: the driver uniform could not be built; keeping the base appearance.");
            return false;
        }

        var look = new Look(pointer, donor, dressed);
        lock (Gate)
        {
            if (Looks.TryGetValue(employeeId, out var stale))
                Destroy(stale);

            Looks[employeeId] = look;
        }

        var applied = Load(employee, dressed);
        if (!applied)
        {
            lock (Gate)
                Looks.Remove(employeeId);

            Destroy(look);
            return false;
        }

        DriverLog.Debug($"{displayName}: delivery-driver uniform applied.");
        return true;
    }

    internal static void Restore(string employeeId, object? employee)
    {
        Look? look;
        lock (Gate)
        {
            if (!Looks.Remove(employeeId, out look))
                return;
        }

        if (employee is not null)
            Load(employee, look.Neutral);

        Destroy(look);
    }

    internal static void RestoreAll(IEnumerable<DriverBrain> drivers)
    {
        foreach (var driver in drivers)
            Restore(driver.Record.EmployeeId, driver.Employee);

        Look[] leftovers;
        lock (Gate)
        {
            leftovers = Looks.Values.ToArray();
            Looks.Clear();
        }

        foreach (var look in leftovers)
            Destroy(look);
    }

    private static UnityObject? Clone(UnityObject donor, string name)
    {
        try
        {
            var raw = UnityObject.Instantiate(donor);
            var typed = Gx.Cast(raw, GameTypes.AvatarSettings);
            if (typed is not UnityObject clone || clone == null)
            {
                if (raw != null)
                    UnityObject.Destroy(raw);
                return null;
            }

            clone.name = name;
            clone.hideFlags = HideFlags.HideAndDontSave;
            return clone;
        }
        catch (Exception ex)
        {
            DriverLog.Warn($"AvatarSettings clone failed ({Gx.Explain(ex)}).");
            return null;
        }
    }

    private static bool Dress(object settings)
    {
        if (!Gx.Set(settings, "UseCombinedLayer", false))
            return false;

        var body = Gx.Get(settings, "BodyLayerSettings");
        var accessories = Gx.Get(settings, "AccessorySettings");
        if (body is null || accessories is null)
            return false;

        var keptBody = Gx.List(body)
            .Where(layer =>
            {
                var path = Gx.Get<string>(layer, "layerPath", string.Empty);
                return path.Length > 0 &&
                       !path.StartsWith("Avatar/Layers/Top/", StringComparison.OrdinalIgnoreCase) &&
                       !path.StartsWith("Avatar/Layers/Bottom/", StringComparison.OrdinalIgnoreCase);
            })
            .Take(4)
            .ToArray();

        var keptAccessories = Gx.List(accessories)
            .Where(accessory =>
            {
                var path = Gx.Get<string>(accessory, "path", string.Empty);
                if (path.Length == 0)
                    return false;

                if (path.Contains("/Feet/", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains("/Chest/", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains("/Waist/", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Keep eyewear and facial hair, but replace hats.
                return !IsHeadwear(path);
            })
            .Take(6)
            .ToArray();

        if (!ReplaceBody(
                body,
                keptBody,
                new[]
                {
                    (RolledShirt, ShirtBlue),
                    (CargoPants, PantsCharcoal),
                }))
        {
            return false;
        }

        return Replace(
            accessories,
            keptAccessories,
            new[]
            {
                NewAccessory(Boots, BootBlack),
                NewAccessory(Belt, BootBlack),
                NewAccessory(Cap, CapBlue),
            });
    }

    /// <summary>
    /// LayerSetting is an IL2CPP value type. Clearing and appending boxed zero-value rows can corrupt
    /// the list; edit existing rows and write each struct back through set_Item instead.
    /// </summary>
    private static bool ReplaceBody(
        object list,
        IReadOnlyList<object?> kept,
        IReadOnlyList<(string Path, Color Color)> additions)
    {
        var desired = kept
            .Select(layer => (
                Path: Gx.Get<string>(layer, "layerPath", string.Empty),
                Color: Gx.Get(layer, "layerTint") is Color color ? color : Color.white))
            .Concat(additions)
            .Take(6)
            .ToArray();

        var count = Gx.Get(list, "Count") as int? ?? 0;
        while (count < desired.Length)
        {
            var template = count > 0 ? Gx.Call(list, "get_Item", new[] { "Int32" }, 0) : null;
            template ??= Gx.New(GameTypes.AvatarLayerSetting);
            if (template is null ||
                !Gx.TryCall(list, "Add", new[] { Gx.Any }, template))
            {
                return false;
            }

            count++;
        }

        for (var i = 0; i < desired.Length; i++)
        {
            var row = Gx.Call(list, "get_Item", new[] { "Int32" }, i);
            if (row is null ||
                !Gx.Set(row, "layerPath", desired[i].Path) ||
                !Gx.Set(row, "layerTint", desired[i].Color) ||
                !Gx.TryCall(list, "set_Item", new[] { "Int32", Gx.Any }, i, row))
            {
                return false;
            }

            var check = Gx.Call(list, "get_Item", new[] { "Int32" }, i);
            if (!string.Equals(
                    Gx.Get<string>(check, "layerPath", string.Empty),
                    desired[i].Path,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        while ((Gx.Get(list, "Count") as int? ?? 0) > desired.Length)
        {
            var last = (Gx.Get(list, "Count") as int? ?? 1) - 1;
            if (!Gx.TryCall(list, "RemoveAt", new[] { "Int32" }, last))
                return false;
        }

        return true;
    }

    private static bool Replace(object list, object?[] kept, object?[] additions)
    {
        if (additions.Any(item => item is null) ||
            !Gx.TryCall(list, "Clear", Array.Empty<string>()))
        {
            return false;
        }

        foreach (var item in kept.Concat(additions).Where(item => item is not null))
        {
            if (!Gx.TryCall(list, "Add", new[] { Gx.Any }, item))
                return false;
        }

        return true;
    }

    private static object? NewAccessory(string path, Color color)
    {
        var accessory = Gx.New(GameTypes.AvatarAccessorySetting);
        if (accessory is null)
            return null;

        return Gx.Set(accessory, "path", path) && Gx.Set(accessory, "color", color)
            ? accessory
            : null;
    }

    private static bool Load(object employee, object settings)
    {
        var avatar = Gx.GetAlive(employee, "Avatar");
        if (avatar is null ||
            !Gx.TryCall(avatar, "LoadAvatarSettings", new[] { "AvatarSettings" }, settings))
        {
            return false;
        }

        var current = Gx.Cast(Gx.Get(avatar, "CurrentSettings"), GameTypes.AvatarSettings);
        return Gx.PointerOf(current) == Gx.PointerOf(settings);
    }

    private static bool IsHeadwear(string path)
    {
        if (!path.Contains("/Head/", StringComparison.OrdinalIgnoreCase))
            return false;

        return !path.Contains("Glasses", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("Oakleys", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("Sunglasses", StringComparison.OrdinalIgnoreCase);
    }

    private static void Destroy(Look look)
    {
        Destroy(look.Dressed);
    }

    private static void Destroy(UnityObject? value)
    {
        if (value != null)
            UnityObject.Destroy(value);
    }

    private sealed record Look(IntPtr EmployeePointer, UnityObject Neutral, UnityObject Dressed);
}
