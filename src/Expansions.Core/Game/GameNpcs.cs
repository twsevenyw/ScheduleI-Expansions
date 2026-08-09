using Expansions.Core.Diagnostics;
using UnityEngine;

namespace Expansions.Core.Game;

/// <summary>
/// Finds NPCs and moves the local player to them.
/// <para>
/// <c>NPCManager</c> has no lookup beyond a static <c>List&lt;NPC&gt; NPCRegistry</c> plus
/// <c>GetNPC(id)</c>, and nothing is visible to either until whoever created it has added it to that
/// list — which is exactly why enumerating the registry is the honest answer to "who is in the world".
/// </para>
/// </summary>
internal static class GameNpcs
{
    /// <summary>Stand this far in front of the NPC rather than inside them.</summary>
    private const float Standoff = 1.6f;

    /// <summary>Enough for every NPC on the map several times over; a guard, not a limit.</summary>
    private const int RegistryCap = 512;

    /// <summary>
    /// Walking the registry means five reflective reads per NPC, and the Actions page asks whether a
    /// given NPC exists on every frame the screen is open. The window is generous because the one caller
    /// that needs live data — the picker — asks for a <see cref="Refresh"/> first.
    /// </summary>
    private static readonly TimedCache<IReadOnlyList<NpcRecord>> Registry = new(2f, Snapshot);

    /// <summary>One NPC, flattened to the strings and the position the picker and the teleport need.</summary>
    internal sealed class NpcRecord
    {
        internal NpcRecord(string id, string name, string region, Vector3 position, bool hasPosition)
        {
            Id = id;
            Name = name;
            Region = region;
            Position = position;
            HasPosition = hasPosition;
        }

        internal string Id { get; }

        internal string Name { get; }

        internal string Region { get; }

        internal Vector3 Position { get; }

        /// <summary>False when the transform could not be read, which makes it un-teleportable.</summary>
        internal bool HasPosition { get; }
    }

    /// <summary>
    /// Everyone in <c>NPCManager.NPCRegistry</c>, sorted by name. Empty outside a loaded save. Cached for
    /// a fraction of a second; positions are re-read fresh at teleport time.
    /// </summary>
    internal static IReadOnlyList<NpcRecord> All() => Registry.Value;

    /// <summary>Drops the snapshot, so the next <see cref="All"/> reads the world as it is now.</summary>
    internal static void Refresh() => Registry.Invalidate();

    /// <summary>
    /// How many NPCs are registered, without materialising the records. This is the cheap answer the
    /// availability predicate needs, and it is one static read plus one <c>Count</c>.
    /// </summary>
    internal static int RegistryCount()
    {
        if (!GameReflection.TryReadStatic(GameTypeNames.NpcManager, "NPCRegistry", out var registry, out _) ||
            registry is null)
        {
            return 0;
        }

        return GameReflection.TryRead(registry, "Count", out var count, out _) && count is int value ? value : 0;
    }

    private static IReadOnlyList<NpcRecord> Snapshot()
    {
        var records = new List<NpcRecord>();

        if (!GameReflection.TryReadStatic(GameTypeNames.NpcManager, "NPCRegistry", out var registry, out var failure) ||
            registry is null)
        {
            ExpansionHost.Log.Debug($"NPCManager.NPCRegistry is unavailable ({failure}).");
            return records;
        }

        foreach (var entry in GameReflection.Enumerate(registry, RegistryCap))
        {
            var record = Describe(entry);
            if (record is not null)
                records.Add(record);
        }

        records.Sort(static (a, b) =>
        {
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.CompareOrdinal(a.Id, b.Id);
        });

        return records;
    }

    /// <summary>
    /// Presence check against the cached registry snapshot only. This is what an availability predicate
    /// wants: a list scan with no reflection, cheap enough to answer on every frame the screen is open.
    /// </summary>
    internal static bool IsKnown(string id)
    {
        foreach (var record in All())
        {
            if (string.Equals(record.Id, id, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// One NPC by save id, then by exact name, then by name substring. Case-insensitive throughout:
    /// the owner is typing this into a search box, not quoting metadata.
    /// </summary>
    internal static NpcRecord? Find(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName))
            return null;

        var needle = idOrName.Trim();

        // GetNPC is the game's own index and answers instantly for a real id.
        var direct = ById(needle);
        if (direct is not null)
            return direct;

        var all = All();

        foreach (var record in all)
        {
            if (string.Equals(record.Id, needle, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(record.Name, needle, StringComparison.OrdinalIgnoreCase))
            {
                return record;
            }
        }

        foreach (var record in all)
        {
            if (record.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                record.Id.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return record;
            }
        }

        return null;
    }

    /// <summary>
    /// The local player's position, or null. Read once by a caller building a whole list rather than per
    /// entry: resolving it goes through a singleton lookup.
    /// </summary>
    internal static Vector3? PlayerPosition() => TryPlayerPosition(out var position) ? position : null;

    /// <summary>
    /// Moves the local player next to <paramref name="record"/>.
    /// <para>
    /// This is a client-side move, so there is no authority check: a co-op guest is allowed to walk
    /// over and look at an NPC the host spawned.
    /// </para>
    /// </summary>
    internal static (bool Ok, string Message) TeleportTo(NpcRecord record)
    {
        if (!record.HasPosition)
            return (false, $"{record.Name} is in the registry but has no readable position, so there is nowhere to go.");

        var destination = record.Position + (Offset(record.Position) * Standoff);

        if (!GameReflection.TryGetSingleton(GameTypeNames.PlayerMovement, out var movement, out var movementFailure))
            return FallbackTeleport(record, destination, movementFailure);

        if (!GameReflection.TryInvokeExact(
                movement!.GetType(),
                movement,
                "Teleport",
                new[] { typeof(Vector3), typeof(bool) },
                new object?[] { destination, true },
                out _,
                out var teleportFailure))
        {
            return FallbackTeleport(record, destination, teleportFailure);
        }

        return (true, $"Teleported to {record.Name} ({record.Id}) at {Format(record.Position)} in {record.Region}.");
    }

    /// <summary>
    /// Writing the transform directly skips the feet-alignment and ground snap
    /// <c>PlayerMovement.Teleport</c> does, so it is a fallback and says so — landing slightly inside
    /// the floor is recoverable, being unable to move at all is not.
    /// </summary>
    private static (bool Ok, string Message) FallbackTeleport(NpcRecord record, Vector3 destination, string reason)
    {
        if (!TryPlayerTransform(out var transform) || transform == null)
            return (false, $"No local player to move ({reason}). Load a save first.");

        try
        {
            transform.position = destination;
        }
        catch (Exception ex)
        {
            return (false, $"Could not move the player ({GameReflection.Unwrap(ex)}); PlayerMovement.Teleport said: {reason}.");
        }

        return (true,
            $"Moved to {record.Name} ({record.Id}) at {Format(record.Position)} by writing the transform, because " +
            $"PlayerMovement.Teleport was unavailable ({reason}). You may need to jump to settle onto the ground.");
    }

    /// <summary>
    /// A horizontal step back towards where the player already is, so the camera faces the NPC rather
    /// than being inside them. Falls back to world forward when the two are on top of each other.
    /// </summary>
    private static Vector3 Offset(Vector3 target)
    {
        if (!TryPlayerPosition(out var player))
            return Vector3.forward;

        var away = player - target;
        away.y = 0f;
        return away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
    }

    private static NpcRecord? ById(string id)
    {
        var managerType = GameReflection.FindType(GameTypeNames.NpcManager);
        if (managerType is null)
            return null;

        if (!GameReflection.TryInvokeExact(
                managerType, null, "GetNPC", new[] { typeof(string) }, new object?[] { id }, out var npc, out _))
        {
            return null;
        }

        return GameReflection.IsPresent(npc) ? Describe(npc) : null;
    }

    private static NpcRecord? Describe(object? npc)
    {
        if (!GameReflection.IsPresent(npc))
            return null;

        var id = Read(npc, "ID");
        var name = Read(npc, "FullName");

        if (name.Length == 0)
        {
            var first = Read(npc, "FirstName");
            var last = Read(npc, "LastName");
            name = $"{first} {last}".Trim();
        }

        if (name.Length == 0)
            name = id.Length > 0 ? id : "unnamed NPC";

        if (id.Length == 0)
            id = name;

        var region = Read(npc, "Region");
        if (region.Length == 0)
            region = "unknown region";

        var position = Vector3.zero;
        var hasPosition = false;

        // NPC derives from NetworkBehaviour, so it is a Component and the interop wrapper really is the
        // UnityEngine.Component that Core references.
        if (npc is Component component && component != null)
        {
            try
            {
                position = component.transform.position;
                hasPosition = true;
            }
            catch
            {
                // A destroyed native side; the record stays listed but not teleportable.
            }
        }

        return new NpcRecord(id, name, region, position, hasPosition);
    }

    private static string Read(object? instance, string member) =>
        GameReflection.TryRead(instance, member, out var value, out _) && value is not null
            ? GameReflection.Format(value)
            : string.Empty;

    private static bool TryPlayerPosition(out Vector3 position)
    {
        position = Vector3.zero;

        if (!TryPlayerTransform(out var transform) || transform == null)
            return false;

        try
        {
            position = transform.position;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The local player's transform, preferring <c>PlayerMovement</c>'s singleton and falling back to
    /// the static <c>Player.Local</c>.
    /// </summary>
    private static bool TryPlayerTransform(out Transform? transform)
    {
        transform = null;

        if (GameReflection.TryGetSingleton(GameTypeNames.PlayerMovement, out var movement, out _) &&
            movement is Component movementComponent && movementComponent != null)
        {
            transform = movementComponent.transform;
            return true;
        }

        if (GameReflection.TryReadStatic(GameTypeNames.Player, "Local", out var local, out _) &&
            local is Component player && player != null)
        {
            transform = player.transform;
            return true;
        }

        return false;
    }

    internal static string Format(Vector3 value) =>
        $"({value.x:0.#}, {value.y:0.#}, {value.z:0.#})";
}
