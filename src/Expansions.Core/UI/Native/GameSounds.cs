using Expansions.Core.Diagnostics;
using Expansions.Core.Logging;
using UnityEngine;

namespace Expansions.Core.UI.Native;

/// <summary>
/// Plays the game's own menu hover and click clips.
/// <para>
/// The clips' asset <em>names</em> were never recovered — only their pathIDs (4434 hover, 3787 click,
/// unanimous across all 61 <c>ButtonSound</c> components in the menu scene). So they are read straight
/// off a live <c>ButtonSound</c> instead of looked up by name, which also means a renamed asset costs
/// nothing.
/// </para>
/// </summary>
internal static class GameSounds
{
    /// <summary>Verified type name; the other two are cheap insurance against a namespace move.</summary>
    private static readonly string[] ButtonSoundTypes =
    {
        "Il2CppScheduleOne.Audio.ButtonSound",
        "Il2CppScheduleOne.UI.ButtonSound",
        "Il2CppScheduleOne.ButtonSound",
    };

    private static readonly ModuleLogger Log = new("UI");

    private static AudioClip? _hover;
    private static AudioClip? _click;

    // §6.2: the values carried by the standard menu buttons (those with _playSoundOnClickStart off).
    private static float _hoverVolume = 0.4f;
    private static float _clickVolume = 0.3f;

    private static GameObject? _host;
    private static AudioSource? _source;
    private static bool _harvested;

    public static bool HasClips => _hover != null || _click != null;

    /// <summary>Call on scene change. Cheap: it stops at the first component carrying both clips.</summary>
    public static void Harvest()
    {
        _harvested = true;

        if (_hover != null && _click != null)
            return;

        Type? buttonSound = null;
        foreach (var name in ButtonSoundTypes)
        {
            buttonSound = GameReflection.FindType(name);
            if (buttonSound is not null)
                break;
        }

        if (buttonSound is null)
        {
            Log.Debug("No ButtonSound type on this build; the toggle screen will be silent.");
            return;
        }

        foreach (var instance in InteropObjects.FindInScene(buttonSound))
        {
            var hover = InteropObjects.Read(instance, buttonSound, "_hoverClip") as AudioClip;
            var click = InteropObjects.Read(instance, buttonSound, "_clickClip") as AudioClip;

            if (hover != null)
                _hover = hover;

            if (click != null)
                _click = click;

            if (InteropObjects.Read(instance, buttonSound, "_hoverVolume") is float hoverVolume && hoverVolume > 0f)
                _hoverVolume = hoverVolume;

            if (InteropObjects.Read(instance, buttonSound, "_clickVolume") is float clickVolume && clickVolume > 0f)
                _clickVolume = clickVolume;

            if (_hover != null && _click != null)
                break;
        }
    }

    public static void PlayHover() => Play(_hover, _hoverVolume);

    public static void PlayClick() => Play(_click, _clickVolume);

    /// <summary>Drops the audio host. The clips belong to the game and are left alone.</summary>
    public static void Release()
    {
        if (InteropObjects.Alive(_host))
            UnityEngine.Object.Destroy(_host);

        _host = null;
        _source = null;
        _hover = null;
        _click = null;
        _harvested = false;
    }

    private static void Play(AudioClip? clip, float volume)
    {
        if (clip == null)
            return;

        var source = EnsureSource();
        if (source == null)
            return;

        try
        {
            source.PlayOneShot(clip, volume);
        }
        catch (Exception ex)
        {
            Log.Debug($"UI sound failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static AudioSource? EnsureSource()
    {
        if (!_harvested)
            Harvest();

        if (InteropObjects.Alive(_source))
            return _source;

        try
        {
            _host = new GameObject("Expansions_UIAudio") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(_host);

            _source = _host.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            // The screen is usable while the game is paused, and menu SFX should not duck with the
            // world volume slider the way positional sources do.
            _source.ignoreListenerPause = true;
            _source.ignoreListenerVolume = false;
            return _source;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not create the UI audio source ({ex.GetType().Name}); the screen will be silent.");
            _source = null;
            return null;
        }
    }
}
