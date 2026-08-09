using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Relation;
using MelonLoader;
using S1API.Console;
using UnityEngine;

namespace CreativeMode;

internal sealed class NpcEntry
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsSupplier { get; init; }
    public bool Unlocked { get; init; }
    public float Relationship { get; init; }
}

/// <summary>
/// Live NPC / supplier discovery + unlock / relationship helpers.
/// </summary>
internal static class NpcCatalog
{
    private static readonly (string Id, string Name, bool Supplier)[] FallbackNpcs =
    {
        ("albert_hoover", "Albert Hoover", true),
        ("geraldine_pills", "Geraldine Pills", true),
        ("oscar_fellowship", "Oscar Fellowship", true),
        ("sam_turner", "Sam Turner", false),
        ("uncle_nelson", "Uncle Nelson", false),
    };

    public static List<NpcEntry> Build(MelonLogger.Instance log)
    {
        var map = new Dictionary<string, NpcEntry>(StringComparer.OrdinalIgnoreCase);

        void Add(string? id, string? name, bool supplier, bool unlocked, float relationship)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;
            id = id.Trim();
            if (map.ContainsKey(id))
                return;
            map[id] = new NpcEntry
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(name) ? id : name.Trim(),
                IsSupplier = supplier,
                Unlocked = unlocked,
                Relationship = relationship
            };
        }

        try
        {
            var reg = NPCManager.NPCRegistry;
            if (reg != null)
            {
                for (var i = 0; i < reg.Count; i++)
                {
                    var npc = reg[i];
                    if (npc == null || npc.WasCollected)
                        continue;
                    TryAddNpc(npc, Add);
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning($"NPCManager.NPCRegistry failed: {ex.Message}");
        }

        try
        {
            var found = Resources.FindObjectsOfTypeAll(Il2CppType.Of<NPC>());
            if (found != null)
            {
                foreach (var obj in found)
                {
                    var npc = obj.TryCast<NPC>();
                    if (npc == null || npc.WasCollected)
                        continue;
                    TryAddNpc(npc, Add);
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning($"Resources.FindObjectsOfTypeAll<NPC> failed: {ex.Message}");
        }

        foreach (var (id, name, supplier) in FallbackNpcs)
            Add(id, name, supplier, false, 0f);

        return map.Values
            .OrderByDescending(n => n.IsSupplier)
            .ThenBy(n => n.Unlocked)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void Unlock(string npcId, MelonLogger.Instance log)
    {
        if (string.IsNullOrWhiteSpace(npcId))
            return;

        try { ConsoleHelper.Submit($"setunlocked {npcId}"); }
        catch (Exception ex) { log.Warning($"setunlocked failed for {npcId}: {ex.Message}"); }

        try
        {
            var npc = FindNative(npcId);
            if (npc?.RelationData != null)
                npc.RelationData.Unlock(NPCRelationData.EUnlockType.DirectApproach, true);
        }
        catch (Exception ex)
        {
            log.Warning($"Native Unlock failed for {npcId}: {ex.Message}");
        }
    }

    public static void SetRelationship(string npcId, float level, MelonLogger.Instance log)
    {
        if (string.IsNullOrWhiteSpace(npcId))
            return;

        level = Mathf.Clamp(level, 0f, 5f);
        try { ConsoleHelper.SetNpcRelationship(npcId, level); }
        catch (Exception ex) { log.Warning($"SetNpcRelationship failed for {npcId}: {ex.Message}"); }

        try
        {
            ConsoleHelper.Submit(
                $"setrelationship {npcId} {level.ToString(CultureInfo.InvariantCulture)}");
        }
        catch (Exception ex)
        {
            log.Warning($"setrelationship submit failed for {npcId}: {ex.Message}");
        }

        try
        {
            var npc = FindNative(npcId);
            npc?.RelationData?.SetRelationship(level);
        }
        catch (Exception ex)
        {
            log.Warning($"Native SetRelationship failed for {npcId}: {ex.Message}");
        }
    }

    public static void UnlockAndMax(string npcId, MelonLogger.Instance log)
    {
        Unlock(npcId, log);
        SetRelationship(npcId, 5f, log);
    }

    public static void UnlockAll(IEnumerable<NpcEntry> npcs, MelonLogger.Instance log)
    {
        foreach (var n in npcs)
            Unlock(n.Id, log);
    }

    public static void MaxAllRelationships(IEnumerable<NpcEntry> npcs, MelonLogger.Instance log)
    {
        foreach (var n in npcs)
            SetRelationship(n.Id, 5f, log);
    }

    public static void UnlockAndMaxAll(IEnumerable<NpcEntry> npcs, MelonLogger.Instance log)
    {
        foreach (var n in npcs)
            UnlockAndMax(n.Id, log);
    }

    private static void TryAddNpc(NPC npc, Action<string?, string?, bool, bool, float> add)
    {
        string id = "";
        try { id = npc.ID ?? ""; } catch { /* ignore */ }
        if (string.IsNullOrWhiteSpace(id))
            return;

        string name = "";
        try { name = npc.FullName ?? ""; } catch { /* ignore */ }
        if (string.IsNullOrWhiteSpace(name))
        {
            try
            {
                var first = npc.FirstName ?? "";
                var last = npc.LastName ?? "";
                name = $"{first} {last}".Trim();
            }
            catch { /* ignore */ }
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            try { name = npc.name ?? id; } catch { name = id; }
        }

        var unlocked = false;
        var relationship = 0f;
        try
        {
            var rd = npc.RelationData;
            if (rd != null)
            {
                unlocked = rd.Unlocked;
                relationship = rd.RelationDelta;
            }
        }
        catch { /* ignore */ }

        var supplier = false;
        try
        {
            if (npc is Supplier)
                supplier = true;
            else if (npc.TryCast<Supplier>() != null)
                supplier = true;
            else if (npc.gameObject != null && npc.gameObject.GetComponent<Supplier>() != null)
                supplier = true;
        }
        catch { /* ignore */ }

        add(id, name, supplier, unlocked, relationship);
    }

    private static NPC? FindNative(string npcId)
    {
        try
        {
            var reg = NPCManager.NPCRegistry;
            if (reg != null)
            {
                for (var i = 0; i < reg.Count; i++)
                {
                    var npc = reg[i];
                    if (npc == null || npc.WasCollected)
                        continue;
                    if (string.Equals(npc.ID, npcId, StringComparison.OrdinalIgnoreCase))
                        return npc;
                }
            }
        }
        catch { /* ignore */ }

        return null;
    }
}
