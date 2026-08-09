using System.Reflection;
using Expansions.Core.Logging;

namespace Expansions.Tweaks.Runtime;

/// <summary>
/// Static log facade so patch bodies and helpers can report without being handed the module.
/// Silent until <see cref="Attach"/> runs, which happens during registration.
/// </summary>
internal static class TweakLog
{
    private static ModuleLogger? _log;

    internal static bool Verbose { get; set; }

    internal static void Attach(ModuleLogger log) => _log = log;

    internal static void Msg(string message) => _log?.Msg(message);

    internal static void Warn(string message) => _log?.Warn(message);

    internal static void Error(string message, Exception exception) => _log?.Error(message, exception);

    /// <summary>Per-station and per-frame chatter. Costs nothing unless the owner asked for it.</summary>
    internal static void Detail(string message)
    {
        if (Verbose)
            _log?.Msg("[dbg] " + message);
    }

    /// <summary>
    /// One line, with the reflection wrapper peeled off: a <c>TargetInvocationException</c> tells the
    /// reader nothing they can act on, and its inner exception tells them everything.
    /// </summary>
    internal static string Describe(Exception exception)
    {
        var inner = exception is TargetInvocationException { InnerException: { } target } ? target : exception;
        return $"{inner.GetType().Name}: {inner.Message}";
    }
}
