namespace Expansions.Core;

/// <summary>
/// One toggleable expansion feature. Core owns registration, config, Harmony scoping and the
/// per-frame fan-out; implementations only supply behaviour.
/// <para>
/// Every hook except <see cref="OnRegistered"/> runs only while the module is enabled, so
/// implementations never need to check a flag first.
/// </para>
/// Prefer deriving from <see cref="ExpansionModule"/> over implementing this directly.
/// </summary>
public interface IExpansionModule
{
    /// <summary>
    /// Stable identity. Drives the Harmony instance id, the default config category and dedup in
    /// the registry — renaming it orphans the user's saved settings.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// MelonPreferences category this module's settings live in. Must be unique, must not be the
    /// shared category, and must not contain a dot. Renaming it orphans the user's saved settings.
    /// </summary>
    string ConfigCategory { get; }

    /// <summary>Title on the module's card in the toggle menu.</summary>
    string DisplayName { get; }

    /// <summary>One line, shown under the title on the card.</summary>
    string Description { get; }

    string Version { get; }

    /// <summary>Runs once, before the first enable. The context is live from here on.</summary>
    void OnRegistered(ModuleContext context);

    void OnEnabled();

    void OnDisabled();

    void OnUpdate();

    void OnFixedUpdate();

    void OnLateUpdate();

    void OnGui();

    void OnSceneLoaded(int buildIndex, string sceneName);

    void OnSceneUnloaded(int buildIndex, string sceneName);

    void OnApplicationQuit();
}
