using MelonLoader;

namespace Expansions.Core.Configuration;

/// <summary>
/// One MelonPreferences category, owned by a single module. Every expansion category is written to
/// the same shared file so users have one place to edit.
/// </summary>
public sealed class ModuleConfig
{
    private readonly MelonPreferences_Category _category;

    internal ModuleConfig(string identifier, string displayName)
    {
        _category = MelonPreferences.CreateCategory(identifier, displayName);
        _category.SetFilePath(ExpansionConfig.FilePath, autoload: true, printmsg: false);
    }

    public string Identifier => _category.Identifier;

    /// <summary>
    /// Declares a setting and returns a live handle to it. Safe to call twice with the same id —
    /// the existing entry is reused so a re-registered module keeps the user's value.
    /// </summary>
    public ConfigValue<T> Bind<T>(string id, T defaultValue, string? displayName = null, string? description = null)
    {
        var entry = _category.HasEntry(id)
            ? _category.GetEntry<T>(id)
            : _category.CreateEntry(id, defaultValue, displayName, description);

        return new ConfigValue<T>(entry, Save);
    }

    public void Save() => _category.SaveToFile(false);
}
