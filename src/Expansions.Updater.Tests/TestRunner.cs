namespace Expansions.Updater.Tests;

internal sealed class AssertionFailed : Exception
{
    internal AssertionFailed(string message) : base(message)
    {
    }
}

internal static class Assert
{
    internal static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertionFailed($"{what}: expected '{expected}', got '{actual}'");
    }

    internal static void True(bool condition, string what)
    {
        if (!condition)
            throw new AssertionFailed($"{what}: expected true");
    }

    internal static void False(bool condition, string what)
    {
        if (condition)
            throw new AssertionFailed($"{what}: expected false");
    }
}

internal sealed class TestRunner
{
    private readonly List<string> _failures = new();
    private int _passed;

    internal void Test(string name, Action body)
    {
        try
        {
            body();
            _passed++;
            Console.WriteLine($"  PASS  {name}");
        }
        catch (AssertionFailed ex)
        {
            _failures.Add($"{name}: {ex.Message}");
            Console.WriteLine($"  FAIL  {name}");
            Console.WriteLine($"        {ex.Message}");
        }
        catch (Exception ex)
        {
            _failures.Add($"{name}: threw {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"  ERROR {name}");
            Console.WriteLine($"        {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal int Finish()
    {
        Console.WriteLine();
        Console.WriteLine($"{_passed} passed, {_failures.Count} failed.");

        foreach (var failure in _failures)
            Console.WriteLine($"  - {failure}");

        return _failures.Count == 0 ? 0 : 1;
    }
}
