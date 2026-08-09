using UnityEngine;

namespace Expansions.SpecialCustomers.Archetypes;

/// <summary>
/// One complete appearance, ready to be written into a cloned <c>AvatarSettings</c>.
/// <para>
/// The caps below are what the avatar actually composites. Face and accessories come straight from
/// the game (<c>AvatarSettings</c> exposes six face slots and <c>Avatar.MAX_ACCESSORIES</c>); the
/// body cap is measured — no shipped NPC in the 127-character harvest carries more than six body
/// layers, so six is where the recipes stop. <see cref="Trim"/> enforces all three rather than
/// letting the avatar drop whichever layer it happens to overflow on.
/// </para>
/// </summary>
internal sealed class ArchetypeLook
{
    private const int MaxFaceLayers = 6;
    private const int MaxBodyLayers = 6;
    private const int MaxAccessories = 9;

    private readonly List<Layer> _face = new();
    private readonly List<Layer> _body = new();
    private readonly List<Layer> _accessories = new();

    internal float Gender { get; set; }

    internal float Height { get; set; } = 1f;

    internal float Weight { get; set; } = 0.5f;

    internal Color32 SkinColor { get; set; } = new(200, 160, 128, 255);

    internal string HairPath { get; set; } = string.Empty;

    internal Color HairColor { get; set; } = AvatarAssets.Palette.Charcoal;

    internal float EyebrowScale { get; set; } = 0.85f;

    internal float EyebrowThickness { get; set; } = 0.6f;

    internal float PupilDilation { get; set; } = 0.65f;

    /// <summary>Held prop, applied through <c>NPC.SetEquippable</c> rather than a layer list.</summary>
    internal string EquippablePath { get; set; } = string.Empty;

    internal IReadOnlyList<Layer> FaceLayers => _face;

    internal IReadOnlyList<Layer> BodyLayers => _body;

    internal IReadOnlyList<Layer> AccessoryLayers => _accessories;

    internal ArchetypeLook Face(string? path, Color color) => Add(_face, path, color);

    internal ArchetypeLook Body(string? path, Color color) => Add(_body, path, color);

    internal ArchetypeLook Accessory(string? path, Color color) => Add(_accessories, path, color);

    /// <summary>Every path in the look, for the wardrobe self-check.</summary>
    internal IEnumerable<string> Paths()
    {
        if (HairPath.Length > 0)
            yield return HairPath;

        foreach (var layer in _face)
            yield return layer.Path;

        foreach (var layer in _body)
            yield return layer.Path;

        foreach (var layer in _accessories)
            yield return layer.Path;
    }

    /// <summary>Drops anything past the game's per-family cap, oldest kept, and reports what went.</summary>
    internal int Trim()
    {
        return Cut(_face, MaxFaceLayers) + Cut(_body, MaxBodyLayers) + Cut(_accessories, MaxAccessories);

        static int Cut(List<Layer> layers, int cap)
        {
            if (layers.Count <= cap)
                return 0;

            var dropped = layers.Count - cap;
            layers.RemoveRange(cap, dropped);
            return dropped;
        }
    }

    /// <summary>Short signature of the whole look, for probes and for spotting duplicate members.</summary>
    internal string Signature()
    {
        unchecked
        {
            var hash = 2166136261u;

            foreach (var path in Paths())
            {
                foreach (var c in path)
                    hash = (hash ^ c) * 16777619u;
            }

            hash = (hash ^ (uint)(SkinColor.r << 16 | SkinColor.g << 8 | SkinColor.b)) * 16777619u;
            hash = (hash ^ (uint)Mathf.RoundToInt(HairColor.r * 255f)) * 16777619u;
            hash = (hash ^ (uint)Mathf.RoundToInt(Weight * 255f)) * 16777619u;

            return hash.ToString("x8");
        }
    }

    private ArchetypeLook Add(List<Layer> layers, string? path, Color color)
    {
        if (!string.IsNullOrEmpty(path))
            layers.Add(new Layer(path!, color));

        return this;
    }

    internal readonly struct Layer
    {
        internal Layer(string path, Color color)
        {
            Path = path;
            Color = color;
        }

        internal string Path { get; }

        internal Color Color { get; }
    }
}
