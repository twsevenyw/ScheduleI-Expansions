using UnityEngine;

namespace Expansions.SpecialCustomers.Archetypes;

/// <summary>
/// Composes one visitor's whole appearance from their archetype's <see cref="Wardrobe"/>.
/// <para>
/// Every draw comes from a single <see cref="LookRandom"/> seeded on
/// <c>(archetype, pool slot, visit)</c>, which is what makes a member individually distinct without
/// being unstable: the same three inputs always rebuild the same person, so a group looks identical
/// after a save/load and identical on a co-op guest's screen, and a different one next time they
/// come to town.
/// </para>
/// <para>
/// Layer tints follow the harvested data rather than taste. Face layers in the shipped game are
/// always pure black with the strength carried in the alpha — a tinted face layer reads as a
/// painted-on mask — and the tattoo sheets are pre-coloured, so they take white.
/// </para>
/// </summary>
internal static class VisitorLookFactory
{
    /// <summary>Tattoo sheets carry their own colour; anything but white recolours the artwork.</summary>
    private static readonly Color TattooTint = Color.white;

    private static readonly HashSet<string> TattooLayers = new(StringComparer.Ordinal)
    {
        AvatarAssets.Body.UpperBodyTattoos,
        AvatarAssets.Body.LeftArmAlien,
        AvatarAssets.Body.RightArmWeb,
    };

    /// <summary>Tops that tuck in, so they cannot be worn under the skirt accessory.</summary>
    private static readonly HashSet<string> TuckedTops = new(StringComparer.Ordinal)
    {
        AvatarAssets.Body.TuckedTShirt,
        AvatarAssets.Body.Overalls,
        AvatarAssets.Body.HazmatSuit,
    };

    internal static ArchetypeLook Build(Archetype archetype, int slotIndex, int visitSeed)
    {
        var wardrobe = archetype.Wardrobe;
        var rng = new LookRandom(archetype.Id, slotIndex, visitSeed);

        var female = rng.Chance(wardrobe.FemaleChance);

        var look = new ArchetypeLook
        {
            // A blend rather than a hard 0 or 1: the shipped characters sit anywhere on the slider,
            // and two members on the same setting have visibly the same build.
            Gender = female ? rng.Range(0.72f, 1f) : rng.Range(0f, 0.28f),
            Height = rng.Range(wardrobe.Height.Min, wardrobe.Height.Max),
            Weight = rng.Range(wardrobe.Weight.Min, wardrobe.Weight.Max),
            SkinColor = rng.Pick(wardrobe.SkinTones),
            EyebrowScale = rng.Range(0.75f, 1.05f),
            EyebrowThickness = rng.Range(0.45f, 0.8f),
            PupilDilation = rng.Range(0.55f, 0.8f),
        };

        var hairColor = wardrobe.AltHairColors.Length > 0 && rng.Chance(wardrobe.AltHairColorChance)
            ? rng.Pick(wardrobe.AltHairColors)
            : rng.Pick(wardrobe.HairColors);

        look.HairColor = hairColor;
        look.HairPath = rng.PickOrDefault(female ? wardrobe.FemaleHair : wardrobe.MaleHair) ?? AvatarAssets.Hair.None;

        BuildFace(look, wardrobe, female, hairColor, ref rng);
        BuildBody(look, wardrobe, female, ref rng);
        BuildAccessories(look, wardrobe, female, ref rng);

        look.EquippablePath = rng.PickOrDefault(wardrobe.Props) ?? AvatarAssets.Equippable.None;
        return look;
    }

    private static void BuildFace(ArchetypeLook look, Wardrobe wardrobe, bool female, Color hairColor, ref LookRandom rng)
    {
        look.Face(rng.PickOrDefault(wardrobe.Expressions), FaceTint(1f));

        if (!female && wardrobe.FacialHair.Length > 0 && rng.Chance(wardrobe.FacialHairChance))
        {
            // Facial hair is the one face layer that is not pure black in spirit: it has to agree
            // with the hair, or a black-bearded blond reads as a texture bug.
            var strength = Mathf.Clamp01(1f - ((hairColor.r + hairColor.g + hairColor.b) / 3f) + 0.35f);
            look.Face(rng.PickOrDefault(wardrobe.FacialHair), FaceTint(Mathf.Clamp(strength, 0.45f, 1f)));
        }

        if (wardrobe.FaceDetails.Length > 0 && rng.Chance(wardrobe.FaceDetailChance))
        {
            var detail = rng.PickOrDefault(wardrobe.FaceDetails);
            look.Face(detail, FaceTint(DetailStrength(detail, ref rng)));
        }

        // Eyeshadow is by far the most-used face layer in the shipped game (96 of 127 characters),
        // so it is the cheapest way to make two members of the same group read differently.
        var eyeShadowChance = female ? Mathf.Min(1f, wardrobe.EyeShadowChance + 0.35f) : wardrobe.EyeShadowChance;
        if (rng.Chance(eyeShadowChance))
            look.Face(AvatarAssets.Face.EyeShadow, FaceTint(rng.Range(0.75f, 1f)));

        if (wardrobe.FaceTattoos.Length > 0 && rng.Chance(wardrobe.FaceTattooChance))
            look.Face(rng.PickOrDefault(wardrobe.FaceTattoos), FaceTint(1f));
    }

    private static void BuildBody(ArchetypeLook look, Wardrobe wardrobe, bool female, ref LookRandom rng)
    {
        var wearingSkirt = female && rng.Chance(wardrobe.SkirtChance);

        var top = rng.PickOrDefault(wardrobe.Tops);
        if (wearingSkirt && top is not null && TuckedTops.Contains(top))
        {
            // The wiki's long-standing complaint is tucked shirts clipping through the skirt mesh.
            // Rather than dropping the skirt from the wardrobe, the top falls back to one that hangs.
            top = AvatarAssets.Body.TShirt;
        }

        look.Body(top, rng.Pick(wardrobe.TopColors));

        if (wearingSkirt)
        {
            // The shipped pairing: every skirted character in the harvest also carries the
            // underlayer, because the skirt accessory does not cover the legs on its own.
            look.Body(AvatarAssets.Body.FemaleUnderwear, AvatarAssets.Palette.White);
        }
        else
        {
            look.Body(rng.PickOrDefault(wardrobe.Bottoms), rng.Pick(wardrobe.BottomColors));
        }

        for (var i = 0; i < 2; i++)
        {
            if (wardrobe.ExtraBodyLayers.Length == 0 || !rng.Chance(wardrobe.ExtraBodyChance))
                continue;

            var extra = rng.PickOrDefault(wardrobe.ExtraBodyLayers);
            if (extra is null || look.BodyLayers.Any(layer => string.Equals(layer.Path, extra, StringComparison.Ordinal)))
                continue;

            look.Body(extra, TattooLayers.Contains(extra) ? TattooTint : rng.Pick(wardrobe.ExtraBodyColors));
        }

        if (wearingSkirt)
            look.Accessory(AvatarAssets.Accessory.MediumSkirt, rng.Pick(wardrobe.SkirtColors));
    }

    private static void BuildAccessories(ArchetypeLook look, Wardrobe wardrobe, bool female, ref LookRandom rng)
    {
        look.Accessory(rng.PickOrDefault(wardrobe.Footwear), rng.Pick(wardrobe.FootwearColors));

        Maybe(look, wardrobe.Outerwear, wardrobe.OuterwearChance, wardrobe.OuterwearColors, ref rng);
        Maybe(look, wardrobe.Headwear, wardrobe.HeadwearChance, wardrobe.HeadwearColors, ref rng);
        Maybe(look, wardrobe.Eyewear, wardrobe.EyewearChance, wardrobe.EyewearColors, ref rng);
        Maybe(look, wardrobe.Neckwear, wardrobe.NeckwearChance, wardrobe.NeckwearColors, ref rng);
        Maybe(look, wardrobe.Waistwear, wardrobe.WaistwearChance, wardrobe.WaistwearColors, ref rng);
        Maybe(look, wardrobe.Handwear, wardrobe.HandwearChance, wardrobe.HandwearColors, ref rng);

        // Chevron is a moustache mesh rather than a garment, so it is drawn last and only for
        // members the face pass left clean-shaven.
        if (!female &&
            wardrobe.FacialHair.Length > 0 &&
            rng.Chance(0.15f) &&
            !look.FaceLayers.Any(layer => layer.Path.StartsWith("Avatar/Layers/Face/FacialHair", StringComparison.Ordinal)))
        {
            look.Accessory(AvatarAssets.Accessory.Chevron, rng.Pick(AvatarAssets.Palette.NaturalHair));
        }
    }

    private static void Maybe(ArchetypeLook look, string[] pool, float chance, Color[] colors, ref LookRandom rng)
    {
        if (pool.Length == 0 || !rng.Chance(chance))
            return;

        look.Accessory(rng.PickOrDefault(pool), rng.Pick(colors));
    }

    /// <summary>Alpha bands taken from what the shipped characters actually use for each layer.</summary>
    private static float DetailStrength(string? path, ref LookRandom rng) => path switch
    {
        AvatarAssets.Face.Freckles => rng.Range(0.38f, 0.62f),
        AvatarAssets.Face.OldPersonWrinkles => rng.Range(0.5f, 1f),
        AvatarAssets.Face.TiredEyes => rng.Range(0.68f, 1f),
        _ => 1f,
    };

    private static Color FaceTint(float alpha) => new(0f, 0f, 0f, Mathf.Clamp01(alpha));
}
