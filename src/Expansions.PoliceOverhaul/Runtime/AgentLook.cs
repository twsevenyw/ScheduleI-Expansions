using Expansions.Core.Diagnostics;
using UnityEngine;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Turns a uniformed officer into a plain-clothes federal agent, by subtraction.
/// <para>
/// No new art. The shipped officer reads as local PD because of three things — the cap, the marked
/// vest and the cluttered duty belt — so taking those off and putting a charcoal blazer, shades and
/// dress shoes on is the whole trick. Every path below is one the shipped game already uses, taken
/// from a live avatar harvest rather than guessed, so a missing asset is not a failure mode.
/// </para>
/// </summary>
internal static class AgentLook
{
    private const string Blazer = "Avatar/Accessories/Chest/Blazer/Blazer";
    private const string Oakleys = "Avatar/Accessories/Head/Oakleys/Oakleys";
    private const string DressShoes = "Avatar/Accessories/Feet/DressShoes/DressShoes";
    private const string PlainBelt = "Avatar/Accessories/Waist/Belt/Belt";

    /// <summary>
    /// The PD markers, matched on substring so a build that renames the folder around the asset still
    /// strips the right thing.
    /// </summary>
    private static readonly string[] Strip =
    {
        "PoliceCap",
        "BulletproofVest_Police",
        "BulletProofVest_Police",
        "PoliceBelt",
        "/Feet/",
    };

    /// <summary>
    /// Charcoal, reserved across this mod suite for federal agents so they never read as the
    /// Special Customers businessman archetype, which uses navy and brown.
    /// </summary>
    private static readonly Color Charcoal = new(0.149f, 0.149f, 0.149f, 1f);

    private static readonly Color NearBlack = new(0.04f, 0.04f, 0.04f, 1f);

    /// <summary>
    /// Re-dresses one officer in place. Returns false without touching anything if the avatar is not
    /// reachable, which is the expected answer on a clone whose components have not woken yet.
    /// </summary>
    internal static bool Apply(object officer)
    {
        var avatar = Members.ReadPath(officer, "Avatar");
        if (avatar is null)
            return false;

        var source = Members.ReadPath(avatar, "CurrentSettings");
        if (source is null)
            return false;

        var settings = CloneSettings(source);
        if (settings is null)
            return false;

        if (!RewriteAccessories(source, settings))
            return false;

        // Per-aspect application rather than LoadAvatarSettings: the wholesale load is the call the
        // community has seen leave a freshly instantiated avatar invisible, and shape keys are not
        // being changed here anyway.
        Members.Invoke(avatar, "ApplyBodySettings", settings);
        Members.Invoke(avatar, "ApplyBodyLayerSettings", settings, -1);
        Members.Invoke(avatar, "ApplyFaceLayerSettings", settings);
        Members.Invoke(avatar, "ApplyAccessorySettings", settings);
        Members.Invoke(avatar, "ApplyHairSettings", settings);
        Members.Invoke(avatar, "ApplyHairColorSettings", settings);
        Members.Invoke(avatar, "ApplyEyeBallSettings", settings);
        Members.Invoke(avatar, "ApplyEyeLidSettings", settings);
        Members.Invoke(avatar, "ApplyEyeLidColorSettings", settings);
        Members.Invoke(avatar, "ApplyEyebrowSettings", settings);
        Members.TryWrite(avatar, "CurrentSettings", settings);

        // The duty belt is gone, but if a build keeps one on the clone, at least drop the less-lethal
        // options: an agent with only a sidearm reads correctly before doing anything.
        var belt = Members.ReadPath(officer, "belt");
        if (belt is not null)
        {
            Members.Invoke(belt, "SetBatonVisible", false);
            Members.Invoke(belt, "SetTaserVisible", false);
            Members.Invoke(belt, "SetGunVisible", true);
        }

        return true;
    }

    /// <summary>
    /// A private copy of the officer's settings. The original is a shared designer ScriptableObject
    /// reached from the NPC data, so editing it in place would re-dress every officer in the game.
    /// </summary>
    private static object? CloneSettings(object source)
    {
        var type = GameReflection.FindType(GameTypes.AvatarSettings);
        if (type is null)
            return null;

        try
        {
            var created = ScriptableObject.CreateInstance(Il2CppInterop.Runtime.Il2CppType.From(type));
            if (created is null)
                return null;

            // CreateInstance hands back a wrapper typed as ScriptableObject even though the native
            // object is an AvatarSettings, and reflection follows the wrapper. Rebuilding the wrapper
            // from the pointer through the interop type's IntPtr constructor is what makes the
            // derived members visible.
            var clone = Activator.CreateInstance(type, created.Pointer);
            if (clone is null)
                return null;

            foreach (var member in new[]
                     {
                         "SkinColor", "Height", "Gender", "Weight", "HairPath", "HairColor",
                         "EyebrowScale", "EyebrowThickness", "EyebrowRestingHeight", "EyebrowRestingAngle",
                         "LeftEyeLidColor", "RightEyeLidColor", "LeftEyeRestingState", "RightEyeRestingState",
                         "EyeballMaterialIdentifier", "EyeBallTint", "PupilDilation",
                         "FaceLayerSettings", "BodyLayerSettings", "UseCombinedLayer", "CombinedLayer",
                         "ImpostorTexture",
                     })
            {
                if (GameReflection.TryRead(source, member, out var value, out _))
                    Members.TryWrite(clone, member, value);
            }

            return clone;
        }
        catch (Exception ex)
        {
            PoliceLog.Warn($"Could not clone avatar settings for a federal agent: {PoliceLog.Describe(ex)}");
            return null;
        }
    }

    private static bool RewriteAccessories(object source, object target)
    {
        if (!GameReflection.TryRead(source, "AccessorySettings", out var original, out _) || original is null)
            return false;

        var list = GameBridge.NewListLike(original);
        if (list is null)
            return false;

        var accessoryType = GameReflection.FindType(GameTypes.AccessorySetting);
        if (accessoryType is null)
            return false;

        foreach (var entry in GameReflection.Enumerate(original, 16))
        {
            var path = Members.Read(entry, "path", string.Empty);
            if (path.Length == 0 || Strip.Any(marker => path.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                continue;

            Members.Invoke(list, "Add", entry);
        }

        Add(list, accessoryType, Blazer, Charcoal);
        Add(list, accessoryType, Oakleys, NearBlack);
        Add(list, accessoryType, DressShoes, NearBlack);
        Add(list, accessoryType, PlainBelt, NearBlack);

        return Members.TryWrite(target, "AccessorySettings", list);
    }

    private static void Add(object list, Type accessoryType, string path, Color color)
    {
        try
        {
            var entry = Activator.CreateInstance(accessoryType);
            if (entry is null)
                return;

            Members.TryWrite(entry, "path", path);
            Members.TryWrite(entry, "color", color);
            Members.Invoke(list, "Add", entry);
        }
        catch (Exception ex)
        {
            PoliceLog.Detail($"Could not add accessory '{path}': {PoliceLog.Describe(ex)}");
        }
    }
}
