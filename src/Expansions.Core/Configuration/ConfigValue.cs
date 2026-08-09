using MelonLoader;

namespace Expansions.Core.Configuration;

/// <summary>
/// Typed handle to a single MelonPreferences entry. Assigning <see cref="Value"/> writes the file
/// immediately — toggles come from the main menu, so there is no later save point to batch into.
/// </summary>
public sealed class ConfigValue<T>
{
    private readonly MelonPreferences_Entry<T> _entry;
    private readonly Action _persist;

    internal ConfigValue(MelonPreferences_Entry<T> entry, Action persist)
    {
        _entry = entry;
        _persist = persist;

        // The C# event on the entry is obsolete-as-error in ML 0.7; the MelonEvent field is the
        // supported path. It also fires when the file is edited or reloaded from disk.
        _entry.OnEntryValueChanged.Subscribe(HandleEntryChanged);
    }

    public event Action<T, T>? Changed;

    public string Id => _entry.Identifier;

    public T DefaultValue => _entry.DefaultValue;

    public T Value
    {
        get => _entry.Value;
        set
        {
            if (EqualityComparer<T>.Default.Equals(_entry.Value, value))
                return;

            _entry.Value = value;
            _persist();
        }
    }

    public void ResetToDefault() => Value = _entry.DefaultValue;

    private void HandleEntryChanged(T previous, T current) => Changed?.Invoke(previous, current);
}
