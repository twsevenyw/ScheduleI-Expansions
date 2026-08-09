using S1API.Economy;
using S1API.Map;
using S1API.Products;
using Assets = Expansions.SpecialCustomers.Archetypes.AvatarAssets;
using Pal = Expansions.SpecialCustomers.Archetypes.AvatarAssets.Palette;

namespace Expansions.SpecialCustomers.Archetypes;

/// <summary>
/// The four groups that visit Hyland Point.
/// <para>
/// Each one is a <see cref="Wardrobe"/> plus an economic profile. The wardrobe is what makes the
/// group read at a glance while no two members are the same person: the silhouette pieces (combat
/// boots and an open vest, a blazer, sandals) are near-certain, and everything else — hair, face,
/// build, skin, shirt colour, hats, glasses, tattoos — is drawn per member.
/// </para>
/// <para>
/// A fifth "party bus" group is deliberately not here. It needs a parked bus with staged passengers,
/// and <c>S1API.Vehicles.LandVehicle</c> exposes spawn, park and colour but no seating or occupancy,
/// so without the bus it would be the hippies with a different palette.
/// </para>
/// </summary>
internal static class ArchetypeCatalog
{
    internal const string Bikers = "bikers";
    internal const string Businessmen = "businessmen";
    internal const string Hippies = "hippies";
    internal const string RockBand = "rock_band";

    private static readonly Archetype[] Catalogue =
    {
        BuildBikers(),
        BuildBusinessmen(),
        BuildHippies(),
        BuildRockBand(),
    };

    internal static IReadOnlyList<Archetype> All => Catalogue;

    internal static Archetype? Find(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        foreach (var archetype in Catalogue)
        {
            if (string.Equals(archetype.Id, id, StringComparison.OrdinalIgnoreCase))
                return archetype;
        }

        return null;
    }

    internal static IReadOnlyList<Archetype> Enabled()
    {
        var enabled = new List<Archetype>(Catalogue.Length);
        foreach (var archetype in Catalogue)
        {
            if (archetype.IsEnabled)
                enabled.Add(archetype);
        }

        return enabled;
    }

    /// <summary>Bikers: the inventory sink. Take anything, pay the least, order the most, at night.</summary>
    private static Archetype BuildBikers() => new(
        id: Bikers,
        displayName: "The Ashfall MC",
        shortName: "Bikers",
        defaultMemberCount: 5,
        orderTime: 2100,
        standards: CustomerStandard.VeryLow,
        preferredDrugs: new[] { DrugType.Methamphetamine, DrugType.Marijuana },
        priceMultiplier: 0.80f,
        quantityMin: 60,
        quantityMax: 90,
        regionWeights: new[]
        {
            (Region.Northtown, 3),
            (Region.Docks, 3),
            (Region.Westville, 2),
            (Region.Downtown, 1),
        },
        voiceId: "redneck",
        voicePitch: 0.92f,
        walkSpeed: 1f,
        clusterRadius: 3f,
        aggressiveness: 0.7f,
        arrivalMessage: "Rolled into {0}. We're only here till sunup — you got crank or not?",
        departureMessage: "We're gone. Maybe next time you'll have something worth the ride.",
        offerMessage: "{0} of {1}. ${2}, cash, no haggling. Take it or don't.",
        wardrobe: new Wardrobe
        {
            FemaleChance = 0.2f,
            Height = (0.98f, 1.1f),
            Weight = (0.6f, 0.95f),
            HairColors = Pal.NaturalHair,
            AltHairColors = Pal.GreyingHair,
            AltHairColorChance = 0.3f,
            MaleHair = new[]
            {
                Assets.Hair.Balding, Assets.Hair.Receding, Assets.Hair.BuzzCut, Assets.Hair.CloseBuzzCut,
                Assets.Hair.LongCurly, Assets.Hair.Mohawk, Assets.Hair.Peaked, Assets.Hair.Tony,
                Assets.Hair.None,
            },
            FemaleHair = new[]
            {
                Assets.Hair.LongCurly, Assets.Hair.ShoulderLength, Assets.Hair.MessyBob,
                Assets.Hair.LowBun, Assets.Hair.FringePonytail,
            },
            Expressions = new[]
            {
                Assets.Face.SmugPout, Assets.Face.Agitated, Assets.Face.NeutralPout,
                Assets.Face.FrownPout, Assets.Face.SlightFrown, Assets.Face.Neutral,
            },
            FacialHair = new[] { Assets.Face.Goatee, Assets.Face.Stubble, Assets.Face.Swirl },
            FacialHairChance = 0.8f,
            FaceDetails = new[] { Assets.Face.OldPersonWrinkles, Assets.Face.TiredEyes, Assets.Face.Freckles },
            FaceDetailChance = 0.7f,
            EyeShadowChance = 0.25f,
            FaceTattoos = new[] { Assets.Face.Teardrop, Assets.Face.FaceTattoos },
            FaceTattooChance = 0.2f,
            Tops = new[]
            {
                Assets.Body.TShirt, Assets.Body.FlannelButtonup, Assets.Body.RolledButtonup,
                Assets.Body.TuckedTShirt, Assets.Body.UpperBodyTattoos, Assets.Body.ChestHair,
            },
            TopColors = new[] { Pal.Black, Pal.Charcoal, Pal.DarkGrey, Pal.Burgundy, Pal.Rust, Pal.Olive, Pal.White },
            Bottoms = new[] { Assets.Body.Jeans, Assets.Body.CargoPants, Assets.Body.Jorts },
            BottomColors = Pal.DenimShades,
            ExtraBodyLayers = new[]
            {
                Assets.Body.UpperBodyTattoos, Assets.Body.LeftArmAlien, Assets.Body.RightArmWeb,
                Assets.Body.FingerlessGloves, Assets.Body.ChestHair,
            },
            ExtraBodyChance = 0.6f,
            ExtraBodyColors = new[] { Pal.Black, Pal.Charcoal, Pal.Brown },
            Footwear = new[] { Assets.Accessory.CombatBoots, Assets.Accessory.CombatBoots, Assets.Accessory.Sneakers },
            FootwearColors = Pal.LeatherShades,
            Outerwear = new[] { Assets.Accessory.OpenVest, Assets.Accessory.OpenVest, Assets.Accessory.CollarJacket, Assets.Accessory.BulletproofVest },
            OuterwearChance = 0.85f,
            OuterwearColors = new[] { Pal.Black, Pal.Charcoal, Pal.DarkGrey, Pal.Brown, Pal.Chestnut },
            Headwear = new[] { Assets.Accessory.Beanie, Assets.Accessory.Cap, Assets.Accessory.CowboyHat, Assets.Accessory.BucketHat },
            HeadwearChance = 0.4f,
            HeadwearColors = new[] { Pal.Black, Pal.Charcoal, Pal.Olive, Pal.Burgundy },
            Eyewear = new[] { Assets.Accessory.LegendSunglasses, Assets.Accessory.Oakleys },
            EyewearChance = 0.55f,
            EyewearColors = new[] { Pal.Black, Pal.Charcoal, Pal.Rust },
            Neckwear = new[] { Assets.Accessory.GoldChain },
            NeckwearChance = 0.4f,
            NeckwearColors = new[] { Pal.Gold, Pal.LightGrey },
            WaistwearChance = 0.9f,
            WaistwearColors = new[] { Pal.Black, Pal.Brown, Pal.Chestnut },
            Props = new[] { Assets.Equippable.Beer, Assets.Equippable.None, Assets.Equippable.None },
        });

    /// <summary>Businessmen: pay closest to market, reject anything below Premium, order mid-afternoon.</summary>
    private static Archetype BuildBusinessmen() => new(
        id: Businessmen,
        displayName: "The Wexler Group",
        shortName: "Businessmen",
        defaultMemberCount: 5,
        orderTime: 1400,
        standards: CustomerStandard.High,
        preferredDrugs: new[] { DrugType.Cocaine },
        priceMultiplier: 0.92f,
        quantityMin: 40,
        quantityMax: 60,
        regionWeights: new[]
        {
            (Region.Uptown, 4),
            (Region.Downtown, 3),
            (Region.Suburbia, 1),
        },
        voiceId: "monotone",
        voicePitch: 1f,
        walkSpeed: 1f,
        clusterRadius: 1.8f,
        aggressiveness: 0.1f,
        arrivalMessage: "We're in {0} through tonight. In the market for premium inventory. Quality is non-negotiable.",
        departureMessage: "Our window has closed. A pleasure regardless.",
        offerMessage: "{0} units of {1}, Premium or better. We're offering ${2}. Standard terms.",
        wardrobe: new Wardrobe
        {
            FemaleChance = 0.4f,
            Height = (0.95f, 1.04f),
            Weight = (0.35f, 0.68f),
            HairColors = Pal.NaturalHair,
            AltHairColors = Pal.GreyingHair,
            AltHairColorChance = 0.35f,
            MaleHair = new[]
            {
                Assets.Hair.Receding, Assets.Hair.Balding, Assets.Hair.Peaked, Assets.Hair.BowlCut,
                Assets.Hair.SidePartBob, Assets.Hair.CloseBuzzCut, Assets.Hair.Tony, Assets.Hair.Franklin,
            },
            FemaleHair = new[]
            {
                Assets.Hair.SidePartBob, Assets.Hair.Bun, Assets.Hair.LowBun, Assets.Hair.MessyBob,
                Assets.Hair.MidFringe, Assets.Hair.DoubleTopKnot,
            },
            Expressions = new[]
            {
                Assets.Face.Neutral, Assets.Face.NeutralPout, Assets.Face.SlightSmile, Assets.Face.SmugPout,
            },
            FacialHair = new[] { Assets.Face.Stubble, Assets.Face.Goatee },
            FacialHairChance = 0.3f,
            FaceDetails = new[] { Assets.Face.OldPersonWrinkles, Assets.Face.Freckles, Assets.Face.TiredEyes },
            FaceDetailChance = 0.6f,
            EyeShadowChance = 0.2f,
            Tops = new[]
            {
                Assets.Body.Buttonup, Assets.Body.Buttonup, Assets.Body.RolledButtonup,
                Assets.Body.TuckedTShirt, Assets.Body.VNeck,
            },
            TopColors = new[] { Pal.White, Pal.Cream, Pal.SkyBlue, Pal.LightGrey, Pal.Charcoal, Pal.Teal },
            Bottoms = new[] { Assets.Body.CargoPants, Assets.Body.Jeans },
            BottomColors = new[] { Pal.Charcoal, Pal.DarkGrey, Pal.Navy, Pal.Black, Pal.Brown },
            SkirtChance = 0.45f,
            SkirtColors = new[] { Pal.Charcoal, Pal.Navy, Pal.Black, Pal.DarkGrey },
            Footwear = new[] { Assets.Accessory.DressShoes, Assets.Accessory.DressShoes, Assets.Accessory.Flats },
            FootwearColors = new[] { Pal.Black, Pal.Charcoal, Pal.Brown, Pal.Chestnut },
            // Navy, brown and denim only. Charcoal on a Blazer is the Police module's federal-agent
            // silhouette, so the two mods never dress the same shape the same colour.
            Outerwear = new[] { Assets.Accessory.Blazer, Assets.Accessory.Blazer, Assets.Accessory.CollarJacket },
            OuterwearChance = 0.85f,
            OuterwearColors = new[] { Pal.Navy, Pal.Brown, Pal.Denim, Pal.Chestnut, Pal.Burgundy },
            Headwear = new[] { Assets.Accessory.PorkpieHat, Assets.Accessory.FlatCap },
            HeadwearChance = 0.15f,
            HeadwearColors = new[] { Pal.Charcoal, Pal.Brown, Pal.Navy },
            Eyewear = new[] { Assets.Accessory.RectangleFrameGlasses, Assets.Accessory.SmallRoundGlasses, Assets.Accessory.Oakleys },
            EyewearChance = 0.6f,
            EyewearColors = new[] { Pal.Black, Pal.DarkGrey, Pal.Gold },
            Neckwear = new[] { Assets.Accessory.GoldChain },
            NeckwearChance = 0.2f,
            WaistwearChance = 0.85f,
            WaistwearColors = new[] { Pal.Black, Pal.Brown, Pal.Chestnut },
            Handwear = new[] { Assets.Accessory.Polex },
            HandwearChance = 0.7f,
            HandwearColors = new[] { Pal.Gold, Pal.LightGrey, Pal.Charcoal },
            Props = new[] { Assets.Equippable.Coffee, Assets.Equippable.PhoneLowered, Assets.Equippable.None },
        });

    /// <summary>Hippies: the only group that asks for a mixed order, and the only saturated palette.</summary>
    private static Archetype BuildHippies() => new(
        id: Hippies,
        displayName: "The Longhaul Caravan",
        shortName: "Hippies",
        defaultMemberCount: 6,
        orderTime: 1100,
        standards: CustomerStandard.Low,
        preferredDrugs: new[] { DrugType.Marijuana, DrugType.Shrooms },
        priceMultiplier: 0.85f,
        quantityMin: 50,
        quantityMax: 80,
        regionWeights: new[]
        {
            (Region.Westville, 3),
            (Region.Northtown, 2),
            (Region.Docks, 2),
            (Region.Suburbia, 1),
        },
        voiceId: "hippie",
        voicePitch: 1.05f,
        walkSpeed: 0.8f,
        clusterRadius: 3.5f,
        aggressiveness: 0f,
        arrivalMessage: "Hey — caravan's parked up in {0} for the day. Looking for green and caps, whatever you've got, man.",
        departureMessage: "Caravan's rolling out. Stay easy.",
        offerMessage: "{0} of {1} would set us right for the road. We can do ${2}, man.",
        wardrobe: new Wardrobe
        {
            FemaleChance = 0.5f,
            Height = (0.92f, 1.03f),
            Weight = (0.25f, 0.6f),
            HairColors = Pal.NaturalHair,
            AltHairColors = Pal.DyedHair,
            AltHairColorChance = 0.2f,
            MaleHair = new[]
            {
                Assets.Hair.LongCurly, Assets.Hair.ShoulderLength, Assets.Hair.Afro, Assets.Hair.BowlCut,
                Assets.Hair.MidFringe, Assets.Hair.Monk, Assets.Hair.DoubleTopKnot, Assets.Hair.Balding,
            },
            FemaleHair = new[]
            {
                Assets.Hair.LongCurly, Assets.Hair.ShoulderLength, Assets.Hair.Afro,
                Assets.Hair.FringePonytail, Assets.Hair.DoubleTopKnot, Assets.Hair.MidFringe,
                Assets.Hair.Bun, Assets.Hair.MessyBob,
            },
            Expressions = new[]
            {
                Assets.Face.SlightSmile, Assets.Face.SlightSmile, Assets.Face.Neutral, Assets.Face.SmugPout,
            },
            FacialHair = new[] { Assets.Face.Swirl, Assets.Face.Goatee, Assets.Face.Stubble },
            FacialHairChance = 0.65f,
            FaceDetails = new[] { Assets.Face.Freckles, Assets.Face.TiredEyes, Assets.Face.OldPersonWrinkles },
            FaceDetailChance = 0.75f,
            EyeShadowChance = 0.3f,
            FaceTattoos = new[] { Assets.Face.FaceTattoos },
            FaceTattooChance = 0.12f,
            Tops = new[]
            {
                Assets.Body.VNeck, Assets.Body.TShirt, Assets.Body.FlannelButtonup,
                Assets.Body.RolledButtonup, Assets.Body.Overalls,
            },
            // The caravan reads because they are the only people in Hyland Point not wearing grey.
            TopColors = new[] { Pal.Lime, Pal.Orange, Pal.DeepPurple, Pal.Mustard, Pal.Teal, Pal.Rust, Pal.Magenta, Pal.Cream },
            Bottoms = new[] { Assets.Body.Jorts, Assets.Body.Jeans, Assets.Body.CargoPants },
            BottomColors = new[] { Pal.Beige, Pal.Tan, Pal.Olive, Pal.FadedDenim, Pal.Brown },
            SkirtChance = 0.5f,
            SkirtColors = new[] { Pal.Rust, Pal.Olive, Pal.Mustard, Pal.DeepPurple, Pal.Beige },
            ExtraBodyLayers = new[]
            {
                Assets.Body.LeftArmAlien, Assets.Body.RightArmWeb, Assets.Body.ChestHair, Assets.Body.FingerlessGloves,
            },
            ExtraBodyChance = 0.4f,
            ExtraBodyColors = new[] { Pal.Brown, Pal.Olive, Pal.Tan },
            Footwear = new[] { Assets.Accessory.Sandals, Assets.Accessory.Sandals, Assets.Accessory.Sneakers, Assets.Accessory.Flats },
            FootwearColors = new[] { Pal.Brown, Pal.Tan, Pal.Beige, Pal.White, Pal.Olive },
            Outerwear = new[] { Assets.Accessory.OpenVest, Assets.Accessory.CollarJacket },
            OuterwearChance = 0.3f,
            OuterwearColors = new[] { Pal.Tan, Pal.Olive, Pal.Rust, Pal.Beige },
            Headwear = new[] { Assets.Accessory.BucketHat, Assets.Accessory.Beanie, Assets.Accessory.MushroomHat, Assets.Accessory.CowboyHat },
            HeadwearChance = 0.45f,
            HeadwearColors = new[] { Pal.DarkGreen, Pal.Mustard, Pal.Beige, Pal.Rust, Pal.White },
            Eyewear = new[] { Assets.Accessory.SmallRoundGlasses, Assets.Accessory.LegendSunglasses },
            EyewearChance = 0.5f,
            EyewearColors = new[] { Pal.Orange, Pal.DeepPurple, Pal.Gold, Pal.Teal },
            Neckwear = new[] { Assets.Accessory.GoldChain },
            NeckwearChance = 0.45f,
            NeckwearColors = new[] { Pal.Gold, Pal.Tan, Pal.LightGrey },
            Waistwear = new[] { Assets.Accessory.Belt, Assets.Accessory.Apron },
            WaistwearChance = 0.5f,
            WaistwearColors = new[] { Pal.Brown, Pal.Tan, Pal.Olive },
            Props = new[] { Assets.Equippable.Joint, Assets.Equippable.Joint, Assets.Equippable.None },
        });

    /// <summary>Rock band: smallest group, latest order, careless with money.</summary>
    private static Archetype BuildRockBand() => new(
        id: RockBand,
        displayName: "Static Sermon",
        shortName: "Rock band",
        defaultMemberCount: 4,
        orderTime: 2300,
        standards: CustomerStandard.Moderate,
        preferredDrugs: new[] { DrugType.Cocaine, DrugType.Shrooms },
        priceMultiplier: 0.88f,
        quantityMin: 45,
        quantityMax: 70,
        regionWeights: new[]
        {
            (Region.Downtown, 3),
            (Region.Uptown, 2),
            (Region.Docks, 2),
        },
        voiceId: "cold",
        voicePitch: 1.05f,
        walkSpeed: 1f,
        clusterRadius: 2.6f,
        aggressiveness: 0.35f,
        arrivalMessage: "Two nights off in {0}. We need enough to get through both. Don't make it complicated.",
        departureMessage: "Bus is loaded. We were never here.",
        offerMessage: "{0} of {1}. ${2}. Just say yes.",
        wardrobe: new Wardrobe
        {
            FemaleChance = 0.35f,
            Height = (0.94f, 1.06f),
            Weight = (0.2f, 0.5f),
            HairColors = Pal.DyedHair,
            AltHairColors = Pal.NaturalHair,
            AltHairColorChance = 0.4f,
            MaleHair = new[]
            {
                Assets.Hair.Mohawk, Assets.Hair.Spiky, Assets.Hair.LongCurly, Assets.Hair.CloseBuzzCut,
                Assets.Hair.ShoulderLength, Assets.Hair.Franklin, Assets.Hair.Peaked,
            },
            FemaleHair = new[]
            {
                Assets.Hair.MessyBob, Assets.Hair.Mohawk, Assets.Hair.ShoulderLength,
                Assets.Hair.DoubleTopKnot, Assets.Hair.FringePonytail, Assets.Hair.LongCurly,
            },
            Expressions = new[]
            {
                Assets.Face.Agitated, Assets.Face.SmugPout, Assets.Face.NeutralPout, Assets.Face.SlightFrown,
            },
            FacialHair = new[] { Assets.Face.Stubble, Assets.Face.Swirl, Assets.Face.Goatee },
            FacialHairChance = 0.5f,
            FaceDetails = new[] { Assets.Face.TiredEyes, Assets.Face.TiredEyes, Assets.Face.Freckles },
            FaceDetailChance = 0.8f,
            EyeShadowChance = 0.75f,
            FaceTattoos = new[] { Assets.Face.Teardrop, Assets.Face.FaceTattoos },
            FaceTattooChance = 0.3f,
            Tops = new[]
            {
                Assets.Body.TShirt, Assets.Body.TShirt, Assets.Body.VNeck,
                Assets.Body.RolledButtonup, Assets.Body.UpperBodyTattoos,
            },
            TopColors = new[] { Pal.Black, Pal.Black, Pal.Charcoal, Pal.Crimson, Pal.DeepPurple, Pal.White },
            Bottoms = new[] { Assets.Body.Jeans, Assets.Body.Jeans, Assets.Body.CargoPants },
            BottomColors = new[] { Pal.Black, Pal.Charcoal, Pal.DarkGrey, Pal.Denim },
            SkirtChance = 0.4f,
            SkirtColors = new[] { Pal.Black, Pal.Crimson, Pal.DeepPurple },
            ExtraBodyLayers = new[]
            {
                Assets.Body.FingerlessGloves, Assets.Body.Gloves, Assets.Body.LeftArmAlien,
                Assets.Body.RightArmWeb, Assets.Body.UpperBodyTattoos,
            },
            ExtraBodyChance = 0.55f,
            ExtraBodyColors = new[] { Pal.Black, Pal.Charcoal },
            Footwear = new[] { Assets.Accessory.CombatBoots, Assets.Accessory.CombatBoots, Assets.Accessory.Sneakers },
            FootwearColors = new[] { Pal.Black, Pal.Charcoal, Pal.White },
            Outerwear = new[] { Assets.Accessory.OpenVest, Assets.Accessory.CollarJacket },
            OuterwearChance = 0.55f,
            OuterwearColors = new[] { Pal.Black, Pal.Charcoal, Pal.Crimson, Pal.DeepPurple },
            Headwear = new[] { Assets.Accessory.Beanie, Assets.Accessory.Cap, Assets.Accessory.PorkpieHat },
            HeadwearChance = 0.35f,
            HeadwearColors = new[] { Pal.Black, Pal.Charcoal, Pal.Crimson },
            Eyewear = new[] { Assets.Accessory.Oakleys, Assets.Accessory.LegendSunglasses, Assets.Accessory.RectangleFrameGlasses },
            EyewearChance = 0.6f,
            EyewearColors = new[] { Pal.Black, Pal.DeepPurple, Pal.Crimson },
            Neckwear = new[] { Assets.Accessory.GoldChain },
            NeckwearChance = 0.6f,
            NeckwearColors = new[] { Pal.LightGrey, Pal.Gold, Pal.Charcoal },
            WaistwearChance = 0.8f,
            WaistwearColors = new[] { Pal.Black, Pal.Charcoal },
            Props = new[] { Assets.Equippable.Beer, Assets.Equippable.None, Assets.Equippable.None },
        });
}
