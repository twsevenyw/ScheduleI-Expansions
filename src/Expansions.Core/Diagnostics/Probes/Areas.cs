namespace Expansions.Core.Diagnostics.Probes;

/// <summary>
/// Report section names. Prefixed with a digit so the registry's ordinal sort groups them the way a
/// reader expects rather than alphabetically.
/// </summary>
internal static class Areas
{
    internal const string Vehicles = "1. Vehicles, drivers and employees";
    internal const string Police = "2. Police";
    internal const string Npcs = "3. NPCs, spawning and appearance";
    internal const string Version = "4. Version and detection baselines";
    internal const string Identity = "5. Persistence and identity";
    internal const string Constants = "6. Runtime constants";
    internal const string Environment = "7. Environment";
}
