using UnityEngine;

namespace Expansions.SpecialCustomers.Archetypes;

/// <summary>
/// The pool of parts one archetype draws a member's appearance from.
/// <para>
/// Modelled on <c>Cartel.GoonPool</c>, which is the game's own runtime-procedural character
/// generator: a base body, a clothing set, a skin tone and a hair colour, each drawn from a small
/// curated array. The difference is granularity — the goon pool picks whole prefabbed
/// <c>AvatarSettings</c>, and there is no shipped set of "biker" ones, so a visitor is composed
/// slot by slot from the harvested asset catalogue instead.
/// </para>
/// <para>
/// Every array here must contain only paths from <see cref="AvatarAssets"/>. The
/// <c>sc.archetypes</c> probe checks that and fails loudly if a future edit slips.
/// </para>
/// </summary>
internal sealed class Wardrobe
{
    private static readonly string[] Empty = Array.Empty<string>();

    /// <summary>How often a member of this group is female. 0 makes an all-male crew.</summary>
    internal float FemaleChance { get; init; }

    internal (float Min, float Max) Height { get; init; } = (0.95f, 1.05f);

    internal (float Min, float Max) Weight { get; init; } = (0.35f, 0.6f);

    internal Color32[] SkinTones { get; init; } = AvatarAssets.Palette.SkinTones;

    internal Color[] HairColors { get; init; } = AvatarAssets.Palette.NaturalHair;

    /// <summary>Second hair palette, drawn from at <see cref="AltHairColorChance"/>.</summary>
    internal Color[] AltHairColors { get; init; } = Array.Empty<Color>();

    internal float AltHairColorChance { get; init; }

    internal string[] MaleHair { get; init; } = Empty;

    internal string[] FemaleHair { get; init; } = Empty;

    /// <summary>Exactly one is always applied — an avatar with no face layer reads as a mannequin.</summary>
    internal string[] Expressions { get; init; } = { AvatarAssets.Face.Neutral };

    internal string[] FacialHair { get; init; } = Empty;

    internal float FacialHairChance { get; init; }

    /// <summary>Freckles, wrinkles, tired eyes: the layer that makes two identical faces differ.</summary>
    internal string[] FaceDetails { get; init; } = Empty;

    internal float FaceDetailChance { get; init; }

    internal float EyeShadowChance { get; init; }

    internal string[] FaceTattoos { get; init; } = Empty;

    internal float FaceTattooChance { get; init; }

    internal string[] Tops { get; init; } = { AvatarAssets.Body.TShirt };

    internal Color[] TopColors { get; init; } = AvatarAssets.Palette.MonochromeShades;

    internal string[] Bottoms { get; init; } = { AvatarAssets.Body.Jeans };

    internal Color[] BottomColors { get; init; } = AvatarAssets.Palette.DenimShades;

    /// <summary>Female members only: swaps trousers for the shipped skirt-plus-underlayer pairing.</summary>
    internal float SkirtChance { get; init; }

    internal Color[] SkirtColors { get; init; } = AvatarAssets.Palette.MonochromeShades;

    /// <summary>Tattoos, gloves, chest hair. Up to two are drawn, each at <see cref="ExtraBodyChance"/>.</summary>
    internal string[] ExtraBodyLayers { get; init; } = Empty;

    internal float ExtraBodyChance { get; init; }

    internal Color[] ExtraBodyColors { get; init; } = AvatarAssets.Palette.MonochromeShades;

    internal string[] Footwear { get; init; } = { AvatarAssets.Accessory.Sneakers };

    internal Color[] FootwearColors { get; init; } = AvatarAssets.Palette.LeatherShades;

    /// <summary>Chest accessory: the vest, blazer or jacket that carries the group's silhouette.</summary>
    internal string[] Outerwear { get; init; } = Empty;

    internal float OuterwearChance { get; init; }

    internal Color[] OuterwearColors { get; init; } = AvatarAssets.Palette.MonochromeShades;

    internal string[] Headwear { get; init; } = Empty;

    internal float HeadwearChance { get; init; }

    internal Color[] HeadwearColors { get; init; } = AvatarAssets.Palette.MonochromeShades;

    internal string[] Eyewear { get; init; } = Empty;

    internal float EyewearChance { get; init; }

    internal Color[] EyewearColors { get; init; } = AvatarAssets.Palette.MonochromeShades;

    internal string[] Neckwear { get; init; } = Empty;

    internal float NeckwearChance { get; init; }

    internal Color[] NeckwearColors { get; init; } = { AvatarAssets.Palette.Gold };

    internal string[] Waistwear { get; init; } = { AvatarAssets.Accessory.Belt };

    internal float WaistwearChance { get; init; } = 0.8f;

    internal Color[] WaistwearColors { get; init; } = AvatarAssets.Palette.LeatherShades;

    internal string[] Handwear { get; init; } = Empty;

    internal float HandwearChance { get; init; }

    internal Color[] HandwearColors { get; init; } = AvatarAssets.Palette.MonochromeShades;

    /// <summary>Held props, one drawn per member. Include the empty string for "empty-handed".</summary>
    internal string[] Props { get; init; } = { AvatarAssets.Equippable.None };

    /// <summary>Every path this wardrobe can produce, for the catalogue check.</summary>
    internal IEnumerable<string> AllPaths()
    {
        foreach (var group in new[]
                 {
                     MaleHair, FemaleHair, Expressions, FacialHair, FaceDetails, FaceTattoos,
                     Tops, Bottoms, ExtraBodyLayers, Footwear, Outerwear, Headwear, Eyewear,
                     Neckwear, Waistwear, Handwear,
                 })
        {
            foreach (var path in group)
                yield return path;
        }

        yield return AvatarAssets.Accessory.MediumSkirt;
        yield return AvatarAssets.Body.FemaleUnderwear;
        yield return AvatarAssets.Face.EyeShadow;
    }
}
