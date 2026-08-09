using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using MelonLoader;
using S1API.Console;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CreativeMode;

internal sealed class ItemEntry
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int StackLimit { get; init; } = 1;
}

/// <summary>
/// Builds a complete item list from every source the game exposes.
/// S1API's registry wrapper alone misses a lot of post-update content.
/// </summary>
internal static class ItemCatalog
{
    private static readonly string[] ResourcePaths =
    {
        "",
        "ScriptableObjects",
        "ScriptableObjects/Item Definitions",
        "ScriptableObjects/ItemDefinitions",
        "Item Definitions",
        "ItemDefinitions",
    };

    /// <summary>Known post-update / shroom-update IDs to force into the list if assets aren't loaded yet.</summary>
    private static readonly (string Id, string Name)[] KnownExtraItems =
    {
        ("acunit", "AC Unit"),
        ("airconditioner", "Air Conditioner"),
        ("mushroombed", "Mushroom Bed"),
        ("mushroom_bed", "Mushroom Bed"),
        ("grainbag", "Grain Bag"),
        ("grain_bag", "Grain Bag"),
        ("sporesyringe", "Spore Syringe"),
        ("spore_syringe", "Spore Syringe"),
        ("mushroomsubstrate", "Mushroom Substrate"),
        ("mushroom_substrate", "Mushroom Substrate"),
        ("substrate", "Substrate"),
        ("mushroomspawnstation", "Mushroom Spawn Station"),
        ("mushroom_spawn_station", "Mushroom Spawn Station"),
        ("spawnstation", "Spawn Station"),
        ("shroomspawn", "Shroom Spawn"),
        ("shroom_spawn", "Shroom Spawn"),
        ("mushroomspawn", "Mushroom Spawn"),
        ("shroom", "Shroom"),
        ("ogkush", "OG Kush"),
        ("sourdiesel", "Sour Diesel"),
        ("greencrack", "Green Crack"),
        ("granddaddypurple", "Granddaddy Purple"),
    };

    public static List<ItemEntry> Build(MelonLogger.Instance log)
    {
        var map = new Dictionary<string, ItemEntry>(StringComparer.OrdinalIgnoreCase);

        void Add(string? id, string? name, int stack)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;
            id = id.Trim();
            if (!map.TryGetValue(id, out var existing))
            {
                map[id] = new ItemEntry
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(name) ? id : name.Trim(),
                    StackLimit = Math.Max(1, stack)
                };
                return;
            }

            // Prefer a real display name over the raw id.
            if (!string.IsNullOrWhiteSpace(name) &&
                (string.IsNullOrWhiteSpace(existing.Name) ||
                 existing.Name.Equals(existing.Id, StringComparison.OrdinalIgnoreCase)))
            {
                map[id] = new ItemEntry
                {
                    Id = existing.Id,
                    Name = name.Trim(),
                    StackLimit = Math.Max(existing.StackLimit, stack)
                };
            }
        }

        void AddDef(ItemDefinition? def)
        {
            if (def == null)
                return;
            try
            {
                Add(def.ID, def.Name, def.StackLimit);
            }
            catch (Exception ex)
            {
                log.Warning($"Skip item def: {ex.Message}");
            }
        }

        // 1) Live registry
        try
        {
            if (Registry.Instance != null)
            {
                var all = Registry.Instance.GetAllItems();
                if (all != null)
                {
                    foreach (var def in all)
                        AddDef(def);
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning($"Registry.GetAllItems failed: {ex.Message}");
        }

        // 2) Resources.LoadAll across known paths (pulls unloaded ScriptableObjects)
        var itemType = Il2CppType.Of<ItemDefinition>();
        foreach (var path in ResourcePaths)
        {
            try
            {
                var loaded = Resources.LoadAll(path, itemType);
                if (loaded == null)
                    continue;
                foreach (var obj in loaded)
                {
                    if (obj == null)
                        continue;
                    AddDef(obj.TryCast<ItemDefinition>());
                }
            }
            catch (Exception ex)
            {
                log.Warning($"Resources.LoadAll('{path}') failed: {ex.Message}");
            }
        }

        // 3) Everything currently in memory
        try
        {
            var found = Resources.FindObjectsOfTypeAll(itemType);
            if (found != null)
            {
                foreach (var obj in found)
                {
                    if (obj == null)
                        continue;
                    AddDef(obj.TryCast<ItemDefinition>());
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning($"FindObjectsOfTypeAll(ItemDefinition) failed: {ex.Message}");
        }

        // 4) Product definitions (including undiscovered shroom mixes if loaded)
        try
        {
            var productType = Il2CppType.Of<ProductDefinition>();
            var products = Resources.FindObjectsOfTypeAll(productType);
            if (products != null)
            {
                foreach (var obj in products)
                {
                    if (obj == null)
                        continue;
                    AddDef(obj.TryCast<ItemDefinition>());
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning($"Product FindObjectsOfTypeAll failed: {ex.Message}");
        }

        // 5) Force-known IDs from recent updates (validated against registry when possible)
        foreach (var (id, name) in KnownExtraItems)
        {
            try
            {
                var def = Registry.GetItem(id);
                if (def != null)
                    AddDef(def);
                else
                    Add(id, name, 1);
            }
            catch
            {
                Add(id, name, 1);
            }
        }

        var list = map.Values
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        log.Msg($"Item catalog built: {list.Count} unique items.");
        return list;
    }

    public static void Give(string itemId, int qty, MelonLogger.Instance log)
    {
        // Prefer console give — same path as vanilla cheats, works for buildables/products.
        try
        {
            ConsoleHelper.AddItemToInventory(itemId, qty);
            return;
        }
        catch (Exception ex)
        {
            log.Warning($"AddItemToInventory({itemId}) failed: {ex.Message}; trying Submit give");
        }

        ConsoleHelper.Submit($"give {itemId} {qty}");
    }
}
