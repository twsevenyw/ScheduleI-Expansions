using Expansions.Core.Logging;

namespace Expansions.HireableDrivers;

/// <summary>
/// Logger for the static machinery the module owns.
/// <para>
/// The driver registry, the clock and the Harmony patch bodies all outlive a single enable/disable
/// cycle — a patch body can run one frame after <c>UnpatchSelf</c> started — so they cannot borrow
/// the module's context-owned logger. This is the same sink under the same prefix, with no lifetime
/// attached.
/// </para>
/// </summary>
internal static class DriverLog
{
    internal static ModuleLogger Instance { get; } = new(HireableDriversModule.ModuleId);

    internal static void Msg(string message) => Instance.Msg(message);

    internal static void Warn(string message) => Instance.Warn(message);

    internal static void Error(string message) => Instance.Error(message);

    internal static void Error(string message, Exception exception) => Instance.Error(message, exception);

    internal static void Debug(string message) => Instance.Debug(message);

    /// <summary>Debug line that also honours the module's own <c>debug_logging</c> switch.</summary>
    internal static void Trace(string message)
    {
        if (Config.DriverSettings.DebugLogging)
            Instance.Msg($"[trace] {message}");
        else
            Instance.Debug(message);
    }
}
