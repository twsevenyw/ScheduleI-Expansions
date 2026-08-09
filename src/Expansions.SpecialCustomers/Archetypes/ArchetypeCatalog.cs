using S1API.Economy;
using S1API.Map;
using S1API.Products;
using UnityEngine;
using Assets = Expansions.SpecialCustomers.Archetypes.AvatarAssets;
using Pal = Expansions.SpecialCustomers.Archetypes.AvatarAssets.Palette;

namespace Expansions.SpecialCustomers.Archetypes;

/// <summary>
/// The four groups that visit Hyland Point.
/// <para>
/// Party Bus is deliberately absent: it needs a parked bus with staged passengers, and
/// <c>S1API.Vehicles.LandVehicle</c> exposes spawn, park and colour but no seating or occupancy.
/// Without the bus it is hippies with a different palette, which is worse than not shipping it.
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
        equippablePath: Assets.Equippable.Beer,
        walkSpeed: 1f,
        clusterRadius: 3f,
        aggressiveness: 0.7f,
        arrivalMessage: "Rolled into {0}. We're only here till sunup — you got crank or not?",
        departureMessage: "We're gone. Maybe next time you'll have something worth the ride.",
        offerMessage: "{0} of {1}. ${2}, cash, no haggling. Take it or don't.",
        buildLook: BikerLook);

    private static ArchetypeLook BikerLook(int member)
    {
        var rng = new LookRandom(Bikers, member);
        var shirtless = rng.Chance(0.35f);

        var look = new ArchetypeLook
        {
            Gender = 0f,
            Height = rng.Range(1f, 1.1f),
            Weight = rng.Range(0.75f, 0.9f),
            SkinColor = rng.Pick(Pal.SkinTones[0], Pal.SkinTones[1], Pal.SkinTones[2], Pal.SkinTones[3]),
            HairPath = rng.Pick(Assets.Hair.LongSlicked, Assets.Hair.Balding, Assets.Hair.BuzzCut),
            HairColor = Pal.Charcoal,
        };

        look.Face(Assets.Face.SmugPout, Pal.Black)
            .Face(Assets.Face.Goatee, Pal.Charcoal)
            .Body(Assets.Body.UpperBodyTattoos, Pal.DeepBlue)
            .Body(Assets.Body.LeftArmWeb, Pal.Charcoal)
            .Body(Assets.Body.RightArmAlien, Pal.Charcoal)
            .Body(Assets.Body.Jeans, Pal.DarkGrey);

        // The shirtless variant is what makes the tattoo layers read at all; the rest keep a tee
        // under the vest so the group is not five identical torsos.
        if (!shirtless)
            look.Body(Assets.Body.TShirt, Pal.Black);

        look.Accessory(Assets.Accessory.OpenVest, Pal.Charcoal)
            .Accessory(Assets.Accessory.CombatBoots, Pal.Black)
            .Accessory(Assets.Accessory.LegendSunglasses, Pal.Black)
            .Accessory(Assets.Accessory.Belt, Pal.Brown);

        if (rng.Chance(0.4f))
            look.Accessory(Assets.Accessory.GoldChain, Pal.Gold);

        return look;
    }

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
        equippablePath: Assets.Equippable.Coffee,
        walkSpeed: 1f,
        clusterRadius: 1.8f,
        aggressiveness: 0.1f,
        arrivalMessage: "We're in {0} through tonight. In the market for premium inventory. Quality is non-negotiable.",
        departureMessage: "Our window has closed. A pleasure regardless.",
        offerMessage: "{0} units of {1}, Premium or better. We're offering ${2}. Standard terms.",
        buildLook: BusinessmanLook);

    private static ArchetypeLook BusinessmanLook(int member)
    {
        var rng = new LookRandom(Businessmen, member);

        var look = new ArchetypeLook
        {
            Gender = rng.Chance(0.3f) ? 1f : 0f,
            Height = rng.Range(0.97f, 1.03f),
            Weight = rng.Range(0.45f, 0.65f),
            SkinColor = rng.Pick(Pal.SkinTones),
            HairColor = Pal.DarkGrey,
        };

        look.HairPath = look.Gender > 0.5f
            ? rng.Pick(Assets.Hair.SidePartBob, Assets.Hair.MessyBob)
            : rng.Pick(Assets.Hair.Receding, Assets.Hair.Peaked, Assets.Hair.BowlCut);

        look.Face(Assets.Face.SlightSmile, Pal.Black)
            .Face(Assets.Face.OldPersonWrinkles, Pal.Tan)
            .Body(Assets.Body.Buttonup, rng.Chance(0.5f) ? Pal.SkyBlue : Pal.White)
            .Body(Assets.Body.CargoPants, Pal.DarkGrey)
            // Navy or brown, never charcoal: charcoal Blazer is reserved for the Police module's
            // federal agents so the two mods never dress the same silhouette.
            .Accessory(Assets.Accessory.Blazer, rng.Chance(0.6f) ? Pal.Navy : Pal.Brown)
            .Accessory(Assets.Accessory.DressShoes, Pal.Brown)
            .Accessory(Assets.Accessory.Belt, Pal.Brown)
            .Accessory(Assets.Accessory.Polex, Pal.Gold);

        if (rng.Chance(0.6f))
            look.Accessory(Assets.Accessory.RectangleFrameGlasses, Pal.DarkGrey);

        return look;
    }

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
        equippablePath: Assets.Equippable.Joint,
        walkSpeed: 0.8f,
        clusterRadius: 3.5f,
        aggressiveness: 0f,
        arrivalMessage: "Hey — caravan's parked up in {0} for the day. Looking for green and caps, whatever you've got, man.",
        departureMessage: "Caravan's rolling out. Stay easy.",
        offerMessage: "{0} of {1} would set us right for the road. We can do ${2}, man.",
        buildLook: HippieLook);

    private static ArchetypeLook HippieLook(int member)
    {
        var rng = new LookRandom(Hippies, member);
        var female = rng.Chance(0.45f);

        var look = new ArchetypeLook
        {
            Gender = female ? 1f : 0f,
            Height = rng.Range(0.93f, 1.0f),
            Weight = rng.Range(0.3f, 0.5f),
            SkinColor = rng.Pick(Pal.SkinTones),
            HairPath = rng.Pick(Assets.Hair.LongCurly, Assets.Hair.ShoulderLength, Assets.Hair.Afro, Assets.Hair.MidFringe),
            HairColor = Pal.Brown,
        };

        look.Face(Assets.Face.SlightSmile, Pal.Black)
            .Face(Assets.Face.TiredEyes, Pal.Brown)
            .Face(Assets.Face.Freckles, Pal.Tan);

        if (!female)
            look.Face(Assets.Face.Swirl, Pal.Brown);

        // One saturated shirt colour per member: the group reads because they are the only people
        // in Hyland Point not wearing grey.
        look.Body(Assets.Body.VNeck, rng.Pick(Pal.Lime, Pal.Orange, Pal.DeepPurple))
            .Body(Assets.Body.LeftArmPeace, Pal.DeepPurple)
            .Body(Assets.Body.RightArmWeed, Pal.DarkGreen)
            // Skirts are documented as clipping through every tucked shirt, so both variants keep
            // the trouser layer until that is confirmed fixed in-game.
            .Body(Assets.Body.Jorts, Pal.Beige)
            .Accessory(Assets.Accessory.Sandals, Pal.Brown)
            .Accessory(Assets.Accessory.GoldChain, Pal.Tan);

        if (rng.Chance(0.5f))
            look.Accessory(Assets.Accessory.SmallRoundGlasses, Pal.Orange);

        if (rng.Chance(0.4f))
            look.Accessory(Assets.Accessory.BucketHat, Pal.DarkGreen);

        return look;
    }

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
        equippablePath: Assets.Equippable.Beer,
        walkSpeed: 1f,
        clusterRadius: 2.6f,
        aggressiveness: 0.35f,
        arrivalMessage: "Two nights off in {0}. We need enough to get through both. Don't make it complicated.",
        departureMessage: "Bus is loaded. We were never here.",
        offerMessage: "{0} of {1}. ${2}. Just say yes.",
        buildLook: RockBandLook);

    private static ArchetypeLook RockBandLook(int member)
    {
        var rng = new LookRandom(RockBand, member);

        var look = new ArchetypeLook
        {
            Gender = rng.Chance(0.35f) ? 1f : 0f,
            Height = rng.Range(0.95f, 1.05f),
            Weight = rng.Range(0.25f, 0.45f),
            SkinColor = rng.Pick(Pal.SkinTones),
            HairPath = rng.Pick(Assets.Hair.Mohawk, Assets.Hair.Spiky, Assets.Hair.LongCurly, Assets.Hair.MessyBob),
            HairColor = rng.Pick(Pal.Crimson, Pal.DeepPurple, Pal.LightGrey, Pal.Black),
        };

        look.Face(rng.Chance(0.5f) ? Assets.Face.Agitated : Assets.Face.SmugPout, Pal.Black)
            .Face(Assets.Face.EyeShadow, Pal.Black);

        // Exactly one teardrop in the band, chosen by index rather than by chance so the group never
        // arrives with four of them or none.
        if (member == 0)
            look.Face(Assets.Face.Teardrop, Pal.Black);

        look.Body(Assets.Body.TShirt, Pal.Black)
            .Body(Assets.Body.FingerlessGloves, Pal.Black)
            .Body(Assets.Body.Jeans, Pal.Black)
            .Body(Assets.Body.LeftArmHeart, Pal.Crimson)
            .Accessory(Assets.Accessory.Oakleys, Pal.Black)
            .Accessory(Assets.Accessory.CombatBoots, Pal.Black)
            .Accessory(Assets.Accessory.Belt, Pal.Black)
            .Accessory(Assets.Accessory.GoldChain, Pal.LightGrey);

        return look;
    }
}
