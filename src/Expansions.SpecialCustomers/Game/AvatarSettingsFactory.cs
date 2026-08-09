using Expansions.Core.Diagnostics;
using Expansions.SpecialCustomers.Archetypes;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace Expansions.SpecialCustomers.Game;

/// <summary>
/// Builds a complete <c>AvatarSettings</c> for one archetype member by cloning the visitor's own
/// settings and replacing the layer lists wholesale.
/// <para>
/// Cloning rather than editing in place is what keeps the baked distance impostor: the texture is a
/// field on the asset, and <c>Instantiate</c> carries it across. Replacing rather than appending is
/// what keeps a pool member that has been re-dressed four times from overflowing the game's 6 face /
/// 8 body / 9 accessory caps.
/// </para>
/// </summary>
internal static class AvatarSettingsFactory
{
    /// <summary>
    /// Duplicates a live settings asset. Marked <c>HideAndDontSave</c> so Unity does not collect it
    /// on the next scene load — an archetype look outlives the scene it was built in.
    /// </summary>
    internal static UnityObject? Clone(object donor, out string failure)
    {
        if (donor is not UnityObject unityDonor || unityDonor == null)
        {
            failure = "the donor settings asset is null or not a Unity object";
            return null;
        }

        try
        {
            var clone = UnityObject.Instantiate(unityDonor);
            clone.hideFlags = HideFlags.HideAndDontSave;
            failure = string.Empty;
            return clone;
        }
        catch (Exception ex)
        {
            failure = Describe.Of(ex);
            return null;
        }
    }

    /// <summary>Writes a recipe into a cloned settings asset. Partial success is still applied.</summary>
    internal static bool Apply(object settings, ArchetypeLook look, out string failure)
    {
        var problems = new List<string>();

        Write(settings, "Gender", look.Gender, problems);
        Write(settings, "Height", look.Height, problems);
        Write(settings, "Weight", look.Weight, problems);
        Write(settings, "SkinColor", (Color)look.SkinColor, problems);
        Write(settings, "HairPath", look.HairPath, problems);
        Write(settings, "HairColor", look.HairColor, problems);

        // Small per-member variation in the parts of the face that are numbers rather than layers.
        Write(settings, "EyebrowScale", look.EyebrowScale, problems);
        Write(settings, "EyebrowThickness", look.EyebrowThickness, problems);
        Write(settings, "PupilDilation", look.PupilDilation, problems);

        // Eyelids follow skin or the face reads as a mask at close range.
        Write(settings, "LeftEyeLidColor", (Color)look.SkinColor, problems);
        Write(settings, "RightEyeLidColor", (Color)look.SkinColor, problems);

        // A settings asset flagged for the combined layer ignores the individual layer lists, and a
        // donor that happened to be flagged would silently swallow the whole recipe.
        Write(settings, "UseCombinedLayer", false, problems);

        ReplaceLayers(settings, "FaceLayerSettings", GameTypes.LayerSetting, "layerPath", "layerTint", look.FaceLayers, problems);
        ReplaceLayers(settings, "BodyLayerSettings", GameTypes.LayerSetting, "layerPath", "layerTint", look.BodyLayers, problems);
        ReplaceLayers(settings, "AccessorySettings", GameTypes.AccessorySetting, "path", "color", look.AccessoryLayers, problems);

        failure = problems.Count == 0 ? string.Empty : string.Join("; ", problems);
        return problems.Count == 0;
    }

    private static void Write(object settings, string member, object value, List<string> problems)
    {
        try
        {
            var property = settings.GetType().GetProperty(member);
            if (property is null || !property.CanWrite)
            {
                problems.Add($"{member} is not writable");
                return;
            }

            property.SetValue(settings, value);
        }
        catch (Exception ex)
        {
            problems.Add($"{member}: {Describe.Of(ex)}");
        }
    }

    private static void ReplaceLayers(
        object settings,
        string listMember,
        string elementTypeName,
        string pathMember,
        string colorMember,
        IReadOnlyList<ArchetypeLook.Layer> layers,
        List<string> problems)
    {
        var elementType = GameReflection.FindType(elementTypeName);
        if (elementType is null)
        {
            problems.Add($"'{elementTypeName}' not found");
            return;
        }

        var property = settings.GetType().GetProperty(listMember);
        if (property is null)
        {
            problems.Add($"{listMember} not found");
            return;
        }

        object? list;
        try
        {
            list = property.GetValue(settings);
        }
        catch (Exception ex)
        {
            problems.Add($"{listMember}: {Describe.Of(ex)}");
            return;
        }

        if (!GameReflection.IsPresent(list))
        {
            list = CreateList(property.PropertyType, problems, listMember);
            if (list is null)
                return;

            try
            {
                property.SetValue(settings, list);
            }
            catch (Exception ex)
            {
                problems.Add($"{listMember} assign: {Describe.Of(ex)}");
                return;
            }
        }

        if (!GameReflection.TryInvoke(list!.GetType(), list, "Clear", Array.Empty<object?>(), out _, out var clearFailure))
        {
            problems.Add($"{listMember}.Clear: {clearFailure}");
            return;
        }

        foreach (var layer in layers)
        {
            object element;
            try
            {
                element = Activator.CreateInstance(elementType)!;
            }
            catch (Exception ex)
            {
                problems.Add($"{elementType.Name}: {Describe.Of(ex)}");
                return;
            }

            Write(element, pathMember, layer.Path, problems);
            Write(element, colorMember, layer.Color, problems);

            if (!GameReflection.TryInvoke(list.GetType(), list, "Add", new[] { element }, out _, out var addFailure))
            {
                problems.Add($"{listMember}.Add: {addFailure}");
                return;
            }
        }
    }

    private static object? CreateList(Type listType, List<string> problems, string listMember)
    {
        try
        {
            return Activator.CreateInstance(listType);
        }
        catch (Exception ex)
        {
            problems.Add($"{listMember} could not be created: {Describe.Of(ex)}");
            return null;
        }
    }
}
