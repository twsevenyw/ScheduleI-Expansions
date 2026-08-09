using Expansions.Core.Configuration;
using MelonLoader;

namespace Expansions.Core.Logging;

/// <summary>
/// Per-module log facade. Every line is prefixed with the module id so three mods sharing one
/// console stay readable.
/// </summary>
public sealed class ModuleLogger
{
    private readonly MelonLogger.Instance _logger;

    public ModuleLogger(string id)
    {
        Id = id;
        _logger = new MelonLogger.Instance($"Expansions/{id}");
    }

    public string Id { get; }

    public void Msg(string message) => _logger.Msg(message);

    public void Warn(string message) => _logger.Warning(message);

    public void Error(string message) => _logger.Error(message);

    public void Error(string message, Exception exception) => _logger.Error(message, exception);

    /// <summary>Dropped unless the shared verbose flag is on.</summary>
    public void Debug(string message)
    {
        if (ExpansionConfig.VerboseLogging)
            _logger.Msg($"[dbg] {message}");
    }
}
