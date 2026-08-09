using System.Text;
using System.Text.RegularExpressions;

namespace Expansions.Core.Diagnostics.Probes;

/// <summary>One shipped NPC's appearance, as read off <c>NPCData.Appearance.AvatarSettings</c>.</summary>
internal sealed class AvatarRecord
{
    internal AvatarRecord(string id, string name, string typeName)
    {
        Id = id;
        Name = name;
        TypeName = typeName;
    }

    internal string Id { get; }

    internal string Name { get; }

    internal string TypeName { get; }

    /// <summary>Raw <c>AvatarSettings.GetJson(false)</c>, or empty if it could not be read.</summary>
    internal string Json { get; set; } = string.Empty;

    /// <summary>(structural slot, asset path) pairs, e.g. <c>("BodyLayer3", "Avatar/Layers/Top/…")</c>.</summary>
    internal List<(string Slot, string Path)> Assets { get; } = new();

    internal string Error { get; set; } = string.Empty;
}

/// <summary>Everything harvested from <c>NPCManager.NPCRegistry</c> in one pass.</summary>
internal sealed class AvatarHarvest
{
    internal List<AvatarRecord> Records { get; } = new();

    internal int RegistryCount { get; set; }

    internal int WithSettings { get; set; }

    internal string Failure { get; set; } = string.Empty;

    internal bool Available => Failure.Length == 0;
}

/// <summary>
/// Walks the NPC registry once per probe run and hands the same harvest to both the avatar-dump and
/// asset-catalogue probes. Reading ~80 NPCs' appearance is the most expensive thing the suite does.
/// </summary>
internal static class AvatarHarvester
{
    private const string NpcManagerType = "Il2CppScheduleOne.NPCs.NPCManager";

    /// <summary>Matches every quoted asset path JsonUtility emits for an <c>AvatarSettings</c>.</summary>
    private static readonly Regex JsonPath = new(
        "\"(layerPath|path|HairPath)\"\\s*:\\s*\"([^\"]*)\"",
        RegexOptions.CultureInvariant);

    private static readonly object Gate = new();

    private static ProbeContext? _cachedFor;
    private static AvatarHarvest? _cached;

    internal static AvatarHarvest Get(ProbeContext context)
    {
        lock (Gate)
        {
            if (ReferenceEquals(_cachedFor, context) && _cached is not null)
                return _cached;
        }

        var harvest = Collect();

        lock (Gate)
        {
            _cachedFor = context;
            _cached = harvest;
        }

        return harvest;
    }

    private static AvatarHarvest Collect()
    {
        var harvest = new AvatarHarvest();

        var managerType = GameReflection.FindType(NpcManagerType);
        if (managerType is null)
        {
            harvest.Failure = $"type '{NpcManagerType}' not found";
            return harvest;
        }

        if (!GameReflection.TryReadStatic(managerType, "NPCRegistry", out var registry, out var failure))
        {
            harvest.Failure = failure;
            return harvest;
        }

        var npcs = GameReflection.Enumerate(registry);
        harvest.RegistryCount = npcs.Count;

        if (npcs.Count == 0)
        {
            harvest.Failure = "NPCManager.NPCRegistry is empty (no save loaded, or NPCs have not registered yet)";
            return harvest;
        }

        foreach (var npc in npcs)
        {
            if (!GameReflection.IsPresent(npc))
                continue;

            var record = new AvatarRecord(
                ProbeHelpers.ReadString(npc, "ID"),
                ProbeHelpers.ReadString(npc, "FullName"),
                npc!.GetType().Name);

            harvest.Records.Add(record);

            if (!GameReflection.TryReadPath(npc, "NPCData.Appearance.AvatarSettings", out var settings, out var settingsFailure))
            {
                record.Error = settingsFailure;
                continue;
            }

            if (!GameReflection.IsPresent(settings))
            {
                record.Error = "AvatarSettings is null";
                continue;
            }

            harvest.WithSettings++;
            ReadAssets(settings, record);

            var settingsType = settings!.GetType();
            if (GameReflection.TryInvoke(settingsType, settings, "GetJson", new object?[] { false }, out var json, out var jsonFailure))
                record.Json = json as string ?? string.Empty;
            else if (record.Error.Length == 0)
                record.Error = jsonFailure;

            // The structural walk above is the reliable path; the JSON is a backstop for builds where
            // the layer lists are shaped differently than expected.
            if (record.Assets.Count == 0 && record.Json.Length > 0)
                ReadAssetsFromJson(record);
        }

        return harvest;
    }

    private static void ReadAssets(object? settings, AvatarRecord record)
    {
        AddLayerList(settings, "BodyLayerSettings", "layerPath", "BodyLayer", record);
        AddLayerList(settings, "FaceLayerSettings", "layerPath", "FaceLayer", record);
        AddLayerList(settings, "AccessorySettings", "path", "Accessory", record);

        if (GameReflection.TryRead(settings, "HairPath", out var hair, out _) && hair is string hairPath && hairPath.Length > 0)
            record.Assets.Add(("Hair", hairPath));
    }

    private static void AddLayerList(object? settings, string listMember, string pathMember, string slotPrefix, AvatarRecord record)
    {
        if (!GameReflection.TryRead(settings, listMember, out var list, out _))
            return;

        var entries = GameReflection.Enumerate(list, cap: 64);
        for (var i = 0; i < entries.Count; i++)
        {
            if (!GameReflection.TryRead(entries[i], pathMember, out var value, out _))
                continue;

            if (value is string path && path.Length > 0)
                record.Assets.Add(($"{slotPrefix}{i + 1}", path));
        }
    }

    private static void ReadAssetsFromJson(AvatarRecord record)
    {
        foreach (Match match in JsonPath.Matches(record.Json))
        {
            var path = match.Groups[2].Value;
            if (path.Length > 0)
                record.Assets.Add(("json:" + match.Groups[1].Value, path));
        }
    }

    /// <summary>Hand-built so the sidecar stays valid JSON without pulling in a serializer.</summary>
    internal static string ToJson(AvatarHarvest harvest)
    {
        var text = new StringBuilder(256 * 1024);
        text.AppendLine("[");

        for (var i = 0; i < harvest.Records.Count; i++)
        {
            var record = harvest.Records[i];
            text.Append("  { \"id\": ").Append(Quote(record.Id));
            text.Append(", \"name\": ").Append(Quote(record.Name));
            text.Append(", \"type\": ").Append(Quote(record.TypeName));

            if (record.Error.Length > 0)
                text.Append(", \"error\": ").Append(Quote(record.Error));

            text.Append(", \"avatarSettings\": ")
                .Append(record.Json.Length > 0 ? record.Json : "null")
                .Append(" }");

            text.AppendLine(i < harvest.Records.Count - 1 ? "," : string.Empty);
        }

        text.AppendLine("]");
        return text.ToString();
    }

    private static string Quote(string? value)
    {
        if (value is null)
            return "null";

        var text = new StringBuilder(value.Length + 2);
        text.Append('"');

        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\r':
                    text.Append("\\r");
                    break;
                case '\t':
                    text.Append("\\t");
                    break;
                default:
                    if (c < ' ')
                        text.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        text.Append(c);
                    break;
            }
        }

        text.Append('"');
        return text.ToString();
    }
}
