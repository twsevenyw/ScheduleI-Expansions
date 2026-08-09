namespace Expansions.Updater.Engine;

internal enum UpdateLogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>
/// The engine's only way of saying anything.
/// <para>
/// A delegate rather than a direct <c>MelonLogger</c> call, for one reason that matters: everything in
/// <c>Engine</c> has to be runnable outside the game so the swap, the rollback and the
/// <c>.disabled</c> rules can be tested against a throwaway directory. A static logger dependency
/// would drag MelonLoader into the test harness and the tests would stop being worth much.
/// </para>
/// </summary>
internal sealed class UpdateLog
{
    private readonly Action<UpdateLogLevel, string> _sink;

    internal UpdateLog(Action<UpdateLogLevel, string> sink) => _sink = sink;

    /// <summary>Swallows everything. Used by the unit tests and as a null object.</summary>
    internal static UpdateLog Silent { get; } = new(static (_, _) => { });

    internal void Debug(string message) => Write(UpdateLogLevel.Debug, message);

    internal void Info(string message) => Write(UpdateLogLevel.Info, message);

    internal void Warning(string message) => Write(UpdateLogLevel.Warning, message);

    internal void Error(string message) => Write(UpdateLogLevel.Error, message);

    private void Write(UpdateLogLevel level, string message)
    {
        try
        {
            _sink(level, message);
        }
        catch
        {
            // A logger that throws must not be able to take an update down. There is nowhere left to
            // report this to, which is exactly why it is swallowed.
        }
    }
}
