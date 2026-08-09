using Expansions.Core.Diagnostics;

namespace Expansions.SpecialCustomers.Commands;

/// <summary>
/// Writes a line into the game's own console, so a command's answer appears where it was typed
/// rather than only in the MelonLoader window behind it.
/// <para>
/// Best-effort by design: <c>Console.Log</c> takes an <c>Il2CppSystem.Object</c>, which needs the
/// interop string conversion, and every step of that is allowed to be absent. The MelonLoader log
/// always gets the line regardless.
/// </para>
/// </summary>
internal static class GameConsoleEcho
{
    private const string ConsoleType = "Il2CppScheduleOne.Console";
    private const string Il2CppStringType = "Il2CppSystem.String";

    internal static void Write(string message)
    {
        try
        {
            var consoleType = GameReflection.FindType(ConsoleType);
            if (consoleType is null)
                return;

            var stringType = GameReflection.FindType(Il2CppStringType);
            var convert = stringType?.GetMethod("op_Implicit", new[] { typeof(string) });
            var boxed = convert?.Invoke(null, new object[] { message });

            if (boxed is not null &&
                GameReflection.TryInvoke(consoleType, null, "Log", new[] { boxed, null }, out _, out _))
            {
                return;
            }

            GameReflection.TryInvoke(consoleType, null, "LogCommandError", new object?[] { message }, out _, out _);
        }
        catch
        {
            // Convenience only.
        }
    }
}
