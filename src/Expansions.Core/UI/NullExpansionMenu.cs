namespace Expansions.Core.UI;

/// <summary>
/// Does nothing. Install it to suppress all built-in UI, e.g. when the toggles are surfaced by
/// something outside Core.
/// </summary>
public sealed class NullExpansionMenu : IExpansionMenu
{
    /// <summary>Also what <see cref="ExpansionMenu.Use"/> parks on while it swaps implementations.</summary>
    public static readonly NullExpansionMenu Instance = new();

    public string Name => "None";

    public bool IsOpen => false;

    public void Open()
    {
    }

    public void Close()
    {
    }

    public void OnAttached()
    {
    }

    public void OnDetached()
    {
    }

    public void OnUpdate()
    {
    }

    public void OnGui()
    {
    }

    public void OnSceneChanged(int buildIndex, string sceneName)
    {
    }
}
