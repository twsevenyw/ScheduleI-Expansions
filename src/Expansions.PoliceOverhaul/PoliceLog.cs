using System.Reflection;
using Expansions.Core.Logging;

namespace Expansions.PoliceOverhaul;

/// <summary>
/// Static reach-through to the module's logger.
/// <para>
/// Harmony patch bodies have to be static and the runtime services are reached from them, so passing
/// a logger down every call chain would mean threading it through code whose only job is to read a
/// field. Before the module registers, and after it is disposed, every call here is a no-op.
/// </para>
/// </summary>
internal static class PoliceLog
{
    private static ModuleLogger? _log;

    /// <summary>Mirrors the module's <c>debug_logging</c> setting; gates the chatty per-tick lines.</summary>
    internal static bool Verbose { get; set; }

    internal static void Attach(ModuleLogger log) => _log = log;

    internal static void Msg(string message) => _log?.Msg(message);

    internal static void Warn(string message) => _log?.Warn(message);

    internal static void Error(string message) => _log?.Error(message);

    internal static void Error(string message, Exception exception) => _log?.Error(message, exception);

    /// <summary>Only reaches the log when the module's own verbose flag is on.</summary>
    internal static void Detail(string message)
    {
        if (Verbose)
            _log?.Msg("[police] " + message);
    }

    /// <summary>
    /// One-line rendering of an exception, unwrapping the reflection layer. Reflection is how every
    /// game call in this module is made, so without this every message would read
    /// <c>TargetInvocationException: Exception has been thrown by the target</c>.
    /// </summary>
    internal static string Describe(Exception exception)
    {
        var inner = exception is TargetInvocationException { InnerException: { } target } ? target : exception;
        return $"{inner.GetType().Name}: {inner.Message}";
    }
}
