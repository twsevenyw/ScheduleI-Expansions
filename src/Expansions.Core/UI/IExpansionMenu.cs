namespace Expansions.Core.UI;

/// <summary>
/// Contract for whatever renders the module toggle screen. Core never assumes IMGUI: the shipping
/// implementation is a native uGUI/TMP main-menu screen installed with <c>ExpansionMenu.Use</c>,
/// which replaces the placeholder without touching module code.
/// </summary>
public interface IExpansionMenu
{
    /// <summary>Logged on swap so it is obvious which implementation is live.</summary>
    string Name { get; }

    bool IsOpen { get; }

    void Open();

    void Close();

    /// <summary>This menu just became the active implementation.</summary>
    void OnAttached();

    /// <summary>This menu was replaced. Release anything it spawned.</summary>
    void OnDetached();

    /// <summary>Every frame, open or not — input and cursor handling live here.</summary>
    void OnUpdate();

    /// <summary>IMGUI draw pass. A uGUI implementation leaves this empty.</summary>
    void OnGui();

    void OnSceneChanged(int buildIndex, string sceneName);
}
