using Expansions.Core.Logging;
using Il2CppTMPro;
using UnityEngine;

namespace Expansions.Core.UI.Native;

/// <summary>
/// Binds the game's own loaded <c>TMP_FontAsset</c>s by name. Nothing is ever shipped: there is no
/// <c>ui/</c> Resources path and no asset bundle in this build, so a font can only come from what the
/// game already has in memory.
/// <para>
/// The 15 runtime asset names are verified against <c>research-ext/raw/dump-tmpfonts/</c>. Each role
/// carries a fallback chain so a renamed asset degrades to a close relative instead of to no text.
/// </para>
/// </summary>
internal static class GameFonts
{
    public const string Title = "OpenSans-SemiBold SDF";
    public const string Description = "OpenSans-SemiBoldItalic SDF";

    private static readonly string[] TitleChain =
    {
        "OpenSans-SemiBold SDF", "OpenSans-Bold SDF", "OpenSans-Medium SDF",
        "OpenSans-Regular SDF", "LiberationSans SDF",
    };

    private static readonly string[] DescriptionChain =
    {
        "OpenSans-SemiBoldItalic SDF", "OpenSans-BoldItalic SDF", "OpenSans-MediumItalic SDF",
        "OpenSans-SemiBold SDF", "LiberationSans SDF",
    };

    private static readonly Dictionary<string, TMP_FontAsset> Cache = new(StringComparer.Ordinal);
    private static readonly ModuleLogger Log = new("UI");

    private static Material? _titleMaterial;
    private static bool _harvested;

    /// <summary>Names actually found, for the one-line diagnostic on first build.</summary>
    public static int Count => Cache.Count;

    /// <summary>
    /// Rebuilds the cache from the current scene. <c>FindObjectsOfTypeAll</c> walks the whole IL2CPP
    /// heap for the type, so this runs once per screen build — never per frame.
    /// </summary>
    public static void Harvest()
    {
        _harvested = true;

        try
        {
            var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            for (var i = 0; i < all.Length; i++)
            {
                var font = all[i];
                if (font == null || string.IsNullOrEmpty(font.name))
                    continue;

                // Replaces an entry whose native side has since been unloaded, rather than skipping
                // on name alone and keeping a dead handle.
                if (Cache.TryGetValue(font.name, out var existing) && existing != null)
                    continue;

                Cache[font.name] = font;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Font harvest failed ({ex.GetType().Name}: {ex.Message}); TMP defaults will be used.");
        }

        HarvestTitleMaterial();
    }

    /// <summary>Preferred asset, else its role's chain, else any Open Sans, else anything at all.</summary>
    public static TMP_FontAsset? Get(string preferred)
    {
        if (!_harvested)
            Harvest();

        if (Cache.TryGetValue(preferred, out var exact) && exact != null)
            return exact;

        var chain = string.Equals(preferred, Description, StringComparison.Ordinal) ? DescriptionChain : TitleChain;
        foreach (var name in chain)
        {
            if (Cache.TryGetValue(name, out var alternate) && alternate != null)
                return alternate;
        }

        foreach (var pair in Cache)
        {
            if (pair.Key.StartsWith("OpenSans", StringComparison.Ordinal) && pair.Value != null)
                return pair.Value;
        }

        foreach (var pair in Cache)
        {
            if (pair.Value != null)
                return pair.Value;
        }

        return null;
    }

    /// <summary>
    /// Applies the game's SDF material preset, which carries the face-dilate/outline settings that
    /// make text look like the game's. Guarded on the font name: each <c>TMP_FontAsset</c> owns
    /// exactly one atlas, and a material from another font renders garbage.
    /// </summary>
    public static void ApplyMaterial(TMP_Text text)
    {
        if (_titleMaterial == null || text.font == null)
            return;

        if (string.Equals(text.font.name, Title, StringComparison.Ordinal))
            text.fontSharedMaterial = _titleMaterial;
    }

    internal static void Forget()
    {
        Cache.Clear();
        _titleMaterial = null;
        _harvested = false;
    }

    private static void HarvestTitleMaterial()
    {
        if (_titleMaterial != null)
            return;

        try
        {
            var texts = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
            for (var i = 0; i < texts.Length; i++)
            {
                var text = texts[i];
                if (text == null || text.font == null || text.fontSharedMaterial == null)
                    continue;

                // Prefabs and imported assets carry editor-time material state; only take the preset
                // off text that is really on screen.
                if (!text.gameObject.scene.IsValid())
                    continue;

                if (!string.Equals(text.font.name, Title, StringComparison.Ordinal))
                    continue;

                _titleMaterial = text.fontSharedMaterial;
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read the game's TMP material preset: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
