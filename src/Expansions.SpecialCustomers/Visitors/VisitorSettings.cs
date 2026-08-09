using Expansions.Core.Configuration;
using UnityEngine;

namespace Expansions.SpecialCustomers.Visitors;

/// <summary>
/// Static mirror of the module's settings.
/// <para>
/// <c>ConfigurePrefab</c> runs on an uninitialized instance owned by S1API, so it cannot reach the
/// module's <c>Config</c> handle. It reads this instead: a plain static that holds usable defaults
/// before <c>OnRegistered</c> binds anything, and never throws.
/// </para>
/// </summary>
internal static class VisitorSettings
{
    /// <summary>Northtown motel forecourt — the patch of tarmac the player wakes up next to.</summary>
    private static readonly Vector3 DefaultSpawnOrigin = new(-53.5701f, 1.065f, 67.7955f);

    private static ConfigValue<float>? _spawnX;
    private static ConfigValue<float>? _spawnY;
    private static ConfigValue<float>? _spawnZ;
    private static ConfigValue<bool>? _announceOnLoad;

    /// <summary>
    /// Where the pool lives when no group is in town: slot 01 stands here, the rest are parked
    /// hidden a few metres along. Only consulted when the prefab is built, so editing it moves
    /// visitors in new saves and in saves that have never seen them — an already-saved visitor keeps
    /// the position the game persisted for it.
    /// </summary>
    internal static Vector3 SpawnOrigin => new(
        _spawnX?.Value ?? DefaultSpawnOrigin.x,
        _spawnY?.Value ?? DefaultSpawnOrigin.y,
        _spawnZ?.Value ?? DefaultSpawnOrigin.z);

    internal static bool AnnounceOnLoad => _announceOnLoad?.Value ?? true;

    internal static void Bind(ModuleConfig config)
    {
        _spawnX = config.Bind(
            "visitor_01_spawn_x",
            DefaultSpawnOrigin.x,
            "Visitor pool home X",
            "World X of the visitor pool's home point. Applied when the NPC prefabs are built, so it only moves visitors that have not been saved yet.");

        _spawnY = config.Bind("visitor_01_spawn_y", DefaultSpawnOrigin.y, "Visitor pool home Y", "World Y of the visitor pool's home point.");
        _spawnZ = config.Bind("visitor_01_spawn_z", DefaultSpawnOrigin.z, "Visitor pool home Z", "World Z of the visitor pool's home point.");

        _announceOnLoad = config.Bind(
            "announce_visitor_on_load",
            true,
            "Announce the scout on load",
            "Log the scout's name and coordinates once per save load, so you can find him without opening the menu.");
    }
}
