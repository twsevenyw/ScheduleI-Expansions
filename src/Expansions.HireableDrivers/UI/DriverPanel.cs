using Expansions.HireableDrivers.Config;
using Expansions.HireableDrivers.Game;
using Expansions.HireableDrivers.Runtime;
using UnityEngine;

namespace Expansions.HireableDrivers.UI;

/// <summary>
/// A read-only look at what the drivers are doing.
/// <para>
/// This used to be the mod's route editor. It is not any more: hiring happens at the employee-hiring
/// NPC and routes are edited on the management clipboard, which is where every other employee is
/// managed and which brings real in-world endpoint picking with it. What a clipboard cannot show is
/// why a driver is standing still, so that is all this is now — the roster, each driver's state, the
/// route rows as the transport loop currently reads them, and the resolution failures behind them.
/// </para>
/// <para>
/// Still IMGUI and still absolute-rect: this build strips <c>GUILayout.TextField</c> and breaks
/// <c>GUI.DrawTexture</c>, and a diagnostic view has no business owning a canvas.
/// </para>
/// </summary>
internal static class DriverPanel
{
    private const float Width = 720f;
    private const float Height = 520f;
    private const float Margin = 12f;
    private const float Row = 22f;

    private static bool _attached;
    private static string _message = string.Empty;
    private static float _messageUntil;
    private static Vector2 _scroll;

    internal static bool IsOpen { get; private set; }

    /// <summary>Counted for the tutorial chapter's diagnostics objective.</summary>
    internal static int TimesOpened { get; private set; }

    internal static void Attach() => _attached = true;

    internal static void Detach()
    {
        _attached = false;
        Close();
    }

    internal static void Open()
    {
        if (IsOpen)
            return;

        IsOpen = true;
        TimesOpened++;
        _scroll = Vector2.zero;
        EndpointCatalog.Refresh();
    }

    internal static void Close() => IsOpen = false;

    internal static void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

    /// <summary>A one-line notice from elsewhere in the mod, shown for a few seconds.</summary>
    internal static void Say(string message)
    {
        _message = message;
        _messageUntil = Time.realtimeSinceStartup + 8f;
    }

    internal static void OnUpdate()
    {
        if (!_attached)
            return;

        try
        {
            if (Input.GetKeyDown(DriverSettings.PanelHotkey))
                Toggle();

            if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
                Close();

            if (!IsOpen)
                return;

            // The game re-locks the mouse every frame through its own state stack, so this has to be
            // reasserted rather than set once.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        catch
        {
            // A stripped input backend costs the hotkey, nothing else.
        }
    }

    internal static void OnGui()
    {
        if (!_attached || !IsOpen)
            return;

        try
        {
            Draw();
        }
        catch (Exception ex)
        {
            // A throw inside OnGUI repeats every frame; closing is the only way to stay readable.
            DriverLog.Error("The drivers panel threw while drawing and has been closed.", ex);
            Close();
        }
    }

    private static void Draw()
    {
        var panel = new Rect(
            (Screen.width - Width) / 2f,
            (Screen.height - Height) / 2f,
            Width,
            Height);

        GUI.Box(panel, "Drivers — diagnostics");

        var y = panel.y + 26f;
        var width = panel.width - (Margin * 2f);

        GUI.Label(new Rect(panel.x + Margin, y, width - 140f, Row), Headline());

        if (GUI.Button(new Rect(panel.xMax - Margin - 128f, y - 2f, 60f, Row), "Rescan"))
        {
            EndpointCatalog.Refresh();
            Say("Rebuilt the endpoint catalogue.");
        }

        if (GUI.Button(new Rect(panel.xMax - Margin - 64f, y - 2f, 52f, Row), "Close"))
        {
            Close();
            return;
        }

        y += Row + 2f;

        GUI.Label(new Rect(panel.x + Margin, y, width, Row), WhereToManage());
        y += Row;

        if (_message.Length > 0 && Time.realtimeSinceStartup < _messageUntil)
        {
            GUI.Label(new Rect(panel.x + Margin, y, width, Row), _message);
            y += Row;
        }

        var view = new Rect(panel.x + Margin, y, width, panel.yMax - y - Margin);
        var lines = Lines();
        var content = new Rect(0f, 0f, view.width - 20f, Math.Max(view.height, lines.Count * Row));

        _scroll = GUI.BeginScrollView(view, _scroll, content);

        var rowY = 0f;
        foreach (var line in lines)
        {
            GUI.Label(new Rect(0f, rowY, content.width, Row), line);
            rowY += Row;
        }

        GUI.EndScrollView();
    }

    private static string Headline()
    {
        var drivers = DriverRegistry.Count;
        var path = TransportPath.LastChosen?.ToString() ?? "undecided";
        var persistence = DriverStore.IsPersistent ? "persisting" : "NOT persisting";
        return $"{drivers} driver(s) · transport path {path} · {persistence} · {EndpointCatalog.Count} endpoint(s)";
    }

    private static string WhereToManage()
    {
        if (!HiringDesk.IsAttached)
        {
            return HiringDesk.LastFailure.Length > 0
                ? $"Hiring is not on the NPC: {HiringDesk.LastFailure}. Use the Expansions menu's repair path."
                : "Still looking for the employee-hiring NPC in this scene.";
        }

        var picker = RoutePicker.IsAvailable(out var reason)
            ? "the drop-off button lists every destination"
            : $"the drop-off button falls back to the game's own picker ({reason})";

        return $"Hire at {HiringDesk.Location}. Bed and routes on the management clipboard — {picker}. " +
               "Vehicle, departure size and \"set off now\" are on the driver's own dialogue.";
    }

    private static List<string> Lines()
    {
        var lines = new List<string>();

        foreach (var property in WorldApi.OwnedProperties())
        {
            var code = WorldApi.PropertyCode(property);
            lines.Add($"{WorldApi.PropertyName(property)} — {DriverCapacity.Describe(code)}");
        }

        lines.Add(string.Empty);

        var drivers = DriverRegistry.Drivers;
        if (drivers.Count == 0)
        {
            lines.Add("No drivers hired yet.");
            return lines;
        }

        foreach (var driver in drivers)
        {
            lines.Add(driver.Describe());
            lines.Add($"    home: {ClipboardRoutes.HomeName(driver)} · {driver.StatusNote}");
            lines.Add(
                $"    clipboard: {(ClipboardApi.RouteField(driver.Employee) is null ? "NO route field" : ClipboardApi.Routes(driver.Employee).Count + " row(s)")}" +
                $" · own dialogue: {(DriverDesk.IsAttached(driver.Record.EmployeeId) ? "attached" : "NOT attached")}" +
                $" · sets off with {DriverDesk.DescribeThreshold(driver.Record.DepartAtUnits)}" +
                $"{(ClipboardApi.IsBeingConfigured(driver.Employee) ? " · being configured right now" : string.Empty)}");

            foreach (var issue in EmployeeApi.WorkIssues(driver.Employee))
                lines.Add($"    the game is telling you: {issue}");

            for (var i = 0; i < driver.Record.Routes.Count; i++)
            {
                var route = driver.Record.Routes[i];
                if (!route.Source.IsSet && !route.Destination.IsSet)
                    continue;

                lines.Add($"    route {i + 1}: {(route.Enabled ? "on " : "off")} {route.Describe()}{Resolution(route)}");
            }

            lines.Add(string.Empty);
        }

        var failures = Gx.Failures;
        if (failures.Count > 0)
        {
            lines.Add($"Binding failures ({failures.Count}) — these disable parts of the loop:");
            foreach (var failure in failures)
                lines.Add("    " + failure);
        }

        return lines;
    }

    /// <summary>Why a route that looks complete still is not running.</summary>
    private static string Resolution(Persistence.DriverRoute route)
    {
        if (!route.IsComplete)
            return "  [incomplete]";

        if (EndpointCatalog.Resolve(route.Source) is null)
            return "  [source not found in the world]";

        if (EndpointCatalog.Resolve(route.Destination) is null)
            return "  [destination not found in the world]";

        return string.Empty;
    }
}
