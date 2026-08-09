using UnityEngine;

namespace Expansions.SpecialCustomers.Archetypes;

/// <summary>
/// The avatar asset paths every visitor look is built from.
/// <para>
/// This is exactly the catalogue harvested from the running game — 127 shipped NPCs, 93 distinct
/// non-empty paths plus "no hair" — recorded in <c>research/AVATAR-ASSET-CATALOGUE.md</c>. Nothing
/// else is allowed in here. A path that is merely plausible is not an exception at runtime, it is a
/// silently missing garment, so the group would arrive half-dressed with nothing in the log to say
/// why. <see cref="All"/> is the whole set and <see cref="Contains"/> is what the diagnostics probe
/// checks every wardrobe entry against.
/// </para>
/// <para>
/// Folder casing follows the harvest, not S1API's mirrored constants: where the two disagree (hair
/// and arm tattoos, which S1API spells lower-case) the harvest is what the shipped data actually
/// contains.
/// </para>
/// </summary>
internal static class AvatarAssets
{
    /// <summary>Face-layer paths. 16 harvested, 6 slots on the avatar.</summary>
    internal static class Face
    {
        internal const string Neutral = "Avatar/Layers/Face/Face_Neutral";
        internal const string SlightSmile = "Avatar/Layers/Face/Face_SlightSmile";
        internal const string SlightFrown = "Avatar/Layers/Face/Face_SlightFrown";
        internal const string SmugPout = "Avatar/Layers/Face/Face_SmugPout";
        internal const string NeutralPout = "Avatar/Layers/Face/Face_NeutralPout";
        internal const string FrownPout = "Avatar/Layers/Face/Face_FrownPout";
        internal const string Agitated = "Avatar/Layers/Face/Face_Agitated";

        internal const string Goatee = "Avatar/Layers/Face/FacialHair_Goatee";
        internal const string Stubble = "Avatar/Layers/Face/FacialHair_Stubble";
        internal const string Swirl = "Avatar/Layers/Face/FacialHair_Swirl";

        internal const string EyeShadow = "Avatar/Layers/Face/EyeShadow";
        internal const string TiredEyes = "Avatar/Layers/Face/TiredEyes";
        internal const string Freckles = "Avatar/Layers/Face/Freckles";
        internal const string OldPersonWrinkles = "Avatar/Layers/Face/OldPersonWrinkles";

        internal const string FaceTattoos = "Avatar/Layers/Face/FaceTattoos1";
        internal const string Teardrop = "Avatar/Layers/Tattoos/Face/Face_Teardrop";
    }

    /// <summary>Body-layer paths. 21 harvested; no shipped NPC uses more than 6 at once.</summary>
    internal static class Body
    {
        internal const string TShirt = "Avatar/Layers/Top/T-Shirt";
        internal const string TuckedTShirt = "Avatar/Layers/Top/Tucked T-Shirt";
        internal const string FastFoodTShirt = "Avatar/Layers/Top/FastFood T-Shirt";
        internal const string Buttonup = "Avatar/Layers/Top/Buttonup";
        internal const string RolledButtonup = "Avatar/Layers/Top/RolledButtonup";
        internal const string FlannelButtonup = "Avatar/Layers/Top/FlannelButtonup";
        internal const string VNeck = "Avatar/Layers/Top/V-Neck";
        internal const string Overalls = "Avatar/Layers/Top/Overalls";
        internal const string HazmatSuit = "Avatar/Layers/Top/HazmatSuit";
        internal const string ChestHair = "Avatar/Layers/Top/ChestHair1";
        internal const string Nipples = "Avatar/Layers/Top/Nipples";
        internal const string UpperBodyTattoos = "Avatar/Layers/Top/UpperBodyTattoos";

        internal const string Jeans = "Avatar/Layers/Bottom/Jeans";
        internal const string CargoPants = "Avatar/Layers/Bottom/CargoPants";
        internal const string Jorts = "Avatar/Layers/Bottom/Jorts";
        internal const string FemaleUnderwear = "Avatar/Layers/Bottom/FemaleUnderwear";
        internal const string MaleUnderwear = "Avatar/Layers/Bottom/MaleUnderwear";

        internal const string Gloves = "Avatar/Layers/Accessories/Gloves";
        internal const string FingerlessGloves = "Avatar/Layers/Accessories/FingerlessGloves";

        internal const string RightArmWeb = "Avatar/Layers/Tattoos/RightArm/RightArm_Web";
        internal const string LeftArmAlien = "Avatar/Layers/Tattoos/LeftArm/LeftArm_Alien";
    }

    /// <summary>Accessory paths. 35 harvested, 9 slots on the avatar.</summary>
    internal static class Accessory
    {
        internal const string OpenVest = "Avatar/Accessories/Chest/OpenVest/OpenVest";
        internal const string Blazer = "Avatar/Accessories/Chest/Blazer/Blazer";
        internal const string CollarJacket = "Avatar/Accessories/Chest/CollarJacket/CollarJacket";
        internal const string BulletproofVest = "Avatar/Accessories/Chest/BulletproofVest/BulletproofVest";

        /// <summary>Police issue. Listed for completeness; no visitor wardrobe draws from it.</summary>
        internal const string BulletproofVestPolice = "Avatar/Accessories/Chest/BulletproofVest/BulletproofVest_Police";

        internal const string CombatBoots = "Avatar/Accessories/Feet/CombatBoots/CombatBoots";
        internal const string Sneakers = "Avatar/Accessories/Feet/Sneakers/Sneakers";
        internal const string DressShoes = "Avatar/Accessories/Feet/DressShoes/DressShoes";
        internal const string Sandals = "Avatar/Accessories/Feet/Sandals/Sandals";
        internal const string Flats = "Avatar/Accessories/Feet/Flats/Flats";

        internal const string RectangleFrameGlasses = "Avatar/Accessories/Head/RectangleFrameGlasses/RectangleFrameGlasses";
        internal const string SmallRoundGlasses = "Avatar/Accessories/Head/SmallRoundGlasses/SmallRoundGlasses";
        internal const string LegendSunglasses = "Avatar/Accessories/Head/LegendSunglasses/LegendSunglasses";
        internal const string Oakleys = "Avatar/Accessories/Head/Oakleys/Oakleys";
        internal const string Cap = "Avatar/Accessories/Head/Cap/Cap";
        internal const string CapFastFood = "Avatar/Accessories/Head/Cap/Cap_FastFood";
        internal const string BucketHat = "Avatar/Accessories/Head/BucketHat/BucketHat";
        internal const string Beanie = "Avatar/Accessories/Head/Beanie/Beanie";
        internal const string FlatCap = "Avatar/Accessories/Head/FlatCap/FlatCap";
        internal const string PorkpieHat = "Avatar/Accessories/Head/PorkpieHat/PorkpieHat";
        internal const string CowboyHat = "Avatar/Accessories/Head/CowboyHat/CowboyHat";
        internal const string MushroomHat = "Avatar/Accessories/Head/MushroomHat/MushroomHat";
        internal const string TrashCrown = "Avatar/Accessories/Head/TrashCrown/TrashCrown";
        internal const string ChefHat = "Avatar/Accessories/Head/ChefHat/ChefHat";
        internal const string Saucepan = "Avatar/Accessories/Head/Saucepan/Saucepan";
        internal const string Respirator = "Avatar/Accessories/Head/Respirator/Respirator";
        internal const string PoliceCap = "Avatar/Accessories/Head/PoliceCap/PoliceCap";

        internal const string Belt = "Avatar/Accessories/Waist/Belt/Belt";
        internal const string Apron = "Avatar/Accessories/Waist/Apron/Apron";
        internal const string PriestGown = "Avatar/Accessories/Waist/PriestGown/PriestGown";
        internal const string PoliceBelt = "Avatar/Accessories/Waist/PoliceBelt/PoliceBelt";

        internal const string GoldChain = "Avatar/Accessories/Neck/GoldChain/GoldChain";
        internal const string Polex = "Avatar/Accessories/Hands/Polex/Polex";
        internal const string Chevron = "Avatar/Accessories/FacialHair/Chevron/Chevron";

        /// <summary>Worn over <see cref="Body.FemaleUnderwear"/>, never over a tucked shirt.</summary>
        internal const string MediumSkirt = "Avatar/Accessories/Bottom/MediumSkirt/MediumSkirt";
    }

    /// <summary>Hair paths. 21 harvested; the empty string is the shipped "no hair" value.</summary>
    internal static class Hair
    {
        internal const string None = "";

        internal const string BowlCut = "Avatar/Hair/BowlCut/BowlCut";
        internal const string Peaked = "Avatar/Hair/Peaked/Peaked";
        internal const string Spiky = "Avatar/Hair/Spiky/Spiky";
        internal const string BuzzCut = "Avatar/Hair/BuzzCut/BuzzCut";
        internal const string CloseBuzzCut = "Avatar/Hair/CloseBuzzCut/CloseBuzzCut";
        internal const string ShoulderLength = "Avatar/Hair/ShoulderLength/ShoulderLength";
        internal const string MidFringe = "Avatar/Hair/MidFringe/MidFringe";
        internal const string DoubleTopKnot = "Avatar/Hair/DoubleTopKnot/DoubleTopKnot";
        internal const string Balding = "Avatar/Hair/Balding/Balding";
        internal const string Bun = "Avatar/Hair/Bun/Bun";
        internal const string LowBun = "Avatar/Hair/LowBun/LowBun";
        internal const string Receding = "Avatar/Hair/Receding/Receding";
        internal const string Afro = "Avatar/Hair/Afro/Afro";
        internal const string MessyBob = "Avatar/Hair/MessyBob/MessyBob";
        internal const string SidePartBob = "Avatar/Hair/SidePartBob/SidePartBob";
        internal const string FringePonytail = "Avatar/Hair/FringePonytail/FringePonytail";
        internal const string LongCurly = "Avatar/Hair/LongCurly/LongCurly";
        internal const string Franklin = "Avatar/Hair/Franklin/Franklin";
        internal const string Mohawk = "Avatar/Hair/Mohawk/Mohawk";
        internal const string Tony = "Avatar/Hair/Tony/Tony";
        internal const string Monk = "Avatar/Hair/Monk/Monk";
    }

    /// <summary>
    /// Held props, applied through <c>NPC.SetEquippable</c> rather than through a layer list.
    /// <para>
    /// These are not in the avatar catalogue, which only covers layers and accessories, so they are
    /// taken verbatim from S1API's own <c>Equippables.Misc</c> constants rather than typed by hand.
    /// </para>
    /// </summary>
    internal static class Equippable
    {
        internal const string None = "";
        internal const string Beer = S1API.Entities.Equippables.Misc.Beer;
        internal const string Coffee = S1API.Entities.Equippables.Misc.Coffee;
        internal const string Joint = S1API.Entities.Equippables.Misc.Joint;
        internal const string PhoneLowered = S1API.Entities.Equippables.Misc.Phone_Lowered;
    }

    /// <summary>Every harvested path, for the wardrobe self-check in <c>sc.archetypes</c>.</summary>
    internal static IReadOnlyCollection<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Face.Neutral, Face.SlightSmile, Face.SlightFrown, Face.SmugPout, Face.NeutralPout, Face.FrownPout,
        Face.Agitated, Face.Goatee, Face.Stubble, Face.Swirl, Face.EyeShadow, Face.TiredEyes, Face.Freckles,
        Face.OldPersonWrinkles, Face.FaceTattoos, Face.Teardrop,

        Body.TShirt, Body.TuckedTShirt, Body.FastFoodTShirt, Body.Buttonup, Body.RolledButtonup,
        Body.FlannelButtonup, Body.VNeck, Body.Overalls, Body.HazmatSuit, Body.ChestHair, Body.Nipples,
        Body.UpperBodyTattoos, Body.Jeans, Body.CargoPants, Body.Jorts, Body.FemaleUnderwear,
        Body.MaleUnderwear, Body.Gloves, Body.FingerlessGloves, Body.RightArmWeb, Body.LeftArmAlien,

        Accessory.OpenVest, Accessory.Blazer, Accessory.CollarJacket, Accessory.BulletproofVest,
        Accessory.BulletproofVestPolice, Accessory.CombatBoots, Accessory.Sneakers, Accessory.DressShoes,
        Accessory.Sandals, Accessory.Flats, Accessory.RectangleFrameGlasses, Accessory.SmallRoundGlasses,
        Accessory.LegendSunglasses, Accessory.Oakleys, Accessory.Cap, Accessory.CapFastFood,
        Accessory.BucketHat, Accessory.Beanie, Accessory.FlatCap, Accessory.PorkpieHat, Accessory.CowboyHat,
        Accessory.MushroomHat, Accessory.TrashCrown, Accessory.ChefHat, Accessory.Saucepan,
        Accessory.Respirator, Accessory.PoliceCap, Accessory.Belt, Accessory.Apron, Accessory.PriestGown,
        Accessory.PoliceBelt, Accessory.GoldChain, Accessory.Polex, Accessory.Chevron, Accessory.MediumSkirt,

        Hair.BowlCut, Hair.Peaked, Hair.Spiky, Hair.BuzzCut, Hair.CloseBuzzCut, Hair.ShoulderLength,
        Hair.MidFringe, Hair.DoubleTopKnot, Hair.Balding, Hair.Bun, Hair.LowBun, Hair.Receding, Hair.Afro,
        Hair.MessyBob, Hair.SidePartBob, Hair.FringePonytail, Hair.LongCurly, Hair.Franklin, Hair.Mohawk,
        Hair.Tony, Hair.Monk,
    };

    /// <summary>True for the empty string too: "no hair" is a legitimate harvested value.</summary>
    internal static bool Contains(string? path) =>
        string.IsNullOrEmpty(path) || All.Contains(path!);

    /// <summary>
    /// Colours. Not harvested — the game stores an arbitrary <c>Color</c> per layer, so these are
    /// chosen, not discovered. Grouped so a wardrobe can hand a whole family to the randomiser.
    /// </summary>
    internal static class Palette
    {
        internal static readonly Color Black = new(0.06f, 0.06f, 0.07f);
        internal static readonly Color Charcoal = new(0.15f, 0.15f, 0.16f);
        internal static readonly Color DarkGrey = new(0.26f, 0.26f, 0.28f);
        internal static readonly Color Grey = new(0.45f, 0.46f, 0.48f);
        internal static readonly Color LightGrey = new(0.72f, 0.73f, 0.75f);
        internal static readonly Color White = new(0.94f, 0.94f, 0.93f);
        internal static readonly Color Cream = new(0.91f, 0.87f, 0.76f);

        /// <summary>Charcoal on a <c>Blazer</c> is reserved for the Police module's federal agents.</summary>
        internal static readonly Color Navy = new(0.11f, 0.16f, 0.31f);

        internal static readonly Color Denim = new(0.19f, 0.23f, 0.31f);
        internal static readonly Color FadedDenim = new(0.38f, 0.45f, 0.55f);
        internal static readonly Color SkyBlue = new(0.62f, 0.76f, 0.89f);
        internal static readonly Color Teal = new(0.13f, 0.42f, 0.44f);

        internal static readonly Color Brown = new(0.31f, 0.21f, 0.13f);
        internal static readonly Color Chestnut = new(0.43f, 0.27f, 0.15f);
        internal static readonly Color Tan = new(0.66f, 0.51f, 0.35f);
        internal static readonly Color Beige = new(0.78f, 0.72f, 0.57f);
        internal static readonly Color Olive = new(0.35f, 0.36f, 0.21f);
        internal static readonly Color DarkGreen = new(0.16f, 0.29f, 0.18f);
        internal static readonly Color Lime = new(0.55f, 0.76f, 0.24f);

        internal static readonly Color Gold = new(0.85f, 0.70f, 0.22f);
        internal static readonly Color Mustard = new(0.76f, 0.60f, 0.16f);
        internal static readonly Color Rust = new(0.55f, 0.26f, 0.12f);
        internal static readonly Color Orange = new(0.85f, 0.47f, 0.15f);
        internal static readonly Color Crimson = new(0.60f, 0.08f, 0.12f);
        internal static readonly Color Burgundy = new(0.32f, 0.07f, 0.13f);
        internal static readonly Color DeepPurple = new(0.33f, 0.16f, 0.45f);
        internal static readonly Color Magenta = new(0.66f, 0.16f, 0.42f);

        /// <summary>Six tones spanning the range the shipped NPCs use.</summary>
        internal static readonly Color32[] SkinTones =
        {
            new(232, 197, 168, 255),
            new(207, 168, 134, 255),
            new(178, 137, 104, 255),
            new(148, 108, 80, 255),
            new(110, 78, 56, 255),
            new(76, 53, 39, 255),
        };

        internal static readonly Color[] NaturalHair =
        {
            new(0.06f, 0.05f, 0.05f),
            new(0.13f, 0.10f, 0.08f),
            new(0.22f, 0.14f, 0.09f),
            new(0.34f, 0.21f, 0.11f),
            new(0.47f, 0.32f, 0.16f),
            new(0.62f, 0.48f, 0.24f),
            new(0.45f, 0.19f, 0.08f),
        };

        internal static readonly Color[] GreyingHair =
        {
            new(0.55f, 0.54f, 0.53f),
            new(0.70f, 0.70f, 0.69f),
            new(0.84f, 0.84f, 0.82f),
        };

        internal static readonly Color[] DyedHair =
        {
            new(0.62f, 0.07f, 0.12f),
            new(0.38f, 0.13f, 0.52f),
            new(0.10f, 0.42f, 0.55f),
            new(0.58f, 0.76f, 0.22f),
            new(0.87f, 0.44f, 0.10f),
            new(0.92f, 0.92f, 0.90f),
        };

        internal static readonly Color[] DenimShades = { Denim, FadedDenim, Charcoal, Black, DarkGrey };

        internal static readonly Color[] EarthShades = { Brown, Chestnut, Tan, Beige, Olive, Rust };

        internal static readonly Color[] MonochromeShades = { Black, Charcoal, DarkGrey, Grey, LightGrey, White };

        internal static readonly Color[] LeatherShades = { Black, Charcoal, Brown, Chestnut, DarkGrey };
    }
}
