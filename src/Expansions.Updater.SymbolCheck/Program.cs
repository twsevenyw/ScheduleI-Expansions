using System.Reflection;

// Verifies every MelonLoader symbol Expansions.Updater binds, against the MelonLoader.dll that is
// actually installed - and then verifies the compiled plugin itself: base type, entry points, and the
// MelonInfo attribute's SystemType. A plugin is not a MelonMod, and getting the base class or the
// callback name wrong is silent: MelonLoader simply never runs it.

var game = args.Length > 0
    ? args[0]
    : @"C:\Program Files (x86)\Steam\steamapps\common\Schedule I";

var net6 = Path.Combine(game, "MelonLoader", "net6");

// bin/<config>/ of this project, up two, across to the plugin's output.
var here = AppContext.BaseDirectory;
var src = Path.GetFullPath(Path.Combine(here, "..", "..", ".."));
var plugin = Path.Combine(src, "Expansions.Updater", "bin", "Release", "Expansions.Updater.dll");

if (!Directory.Exists(net6))
{
    Console.Error.WriteLine($"MelonLoader net6 folder not found: {net6}");
    Console.Error.WriteLine("Pass the game directory as the first argument.");
    return 2;
}

if (!File.Exists(plugin))
{
    Console.Error.WriteLine($"Build the plugin first; not found: {plugin}");
    return 2;
}

var paths = new List<string>(Directory.GetFiles(net6, "*.dll")) { plugin };
paths.AddRange(Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll"));

var resolver = new PathAssemblyResolver(paths.Distinct(StringComparer.OrdinalIgnoreCase));
using var mlc = new MetadataLoadContext(resolver, "System.Private.CoreLib");

var melon = mlc.LoadFromAssemblyPath(Path.Combine(net6, "MelonLoader.dll"));
var updater = mlc.LoadFromAssemblyPath(plugin);

var failures = new List<string>();
var checks = 0;

void Check(string what, Func<bool> probe)
{
    checks++;
    bool ok;
    try { ok = probe(); }
    catch (Exception ex) { ok = false; what += $"  [{ex.GetType().Name}]"; }

    Console.WriteLine($"  {(ok ? "OK  " : "MISS")}  {what}");
    if (!ok) failures.Add(what);
}

Type? T(string name) => melon.GetType(name, throwOnError: false);

bool Method(string typeName, string method, params string[] parameterTypes)
{
    var t = T(typeName);
    if (t is null) return false;

    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
    {
        if (m.Name != method) continue;
        var ps = m.GetParameters().Select(p => p.ParameterType.Name).ToArray();
        if (ps.SequenceEqual(parameterTypes)) return true;
    }

    return false;
}

bool StaticStringProperty(string typeName, string property)
{
    var p = T(typeName)?.GetProperty(property, BindingFlags.Public | BindingFlags.Static);
    return p is not null && p.PropertyType.FullName == "System.String" && p.GetMethod is not null;
}

Console.WriteLine($"MelonLoader: {melon.GetName().Version}");
Console.WriteLine($"Plugin     : {updater.GetName().Name} {updater.GetName().Version}");
Console.WriteLine();
Console.WriteLine("MelonLoader surface the plugin binds:");

Check("MelonLoader.MelonPlugin exists and is public abstract",
    () => T("MelonLoader.MelonPlugin") is { IsPublic: true, IsAbstract: true });

Check("MelonPlugin : MelonTypeBase<MelonPlugin> : MelonBase (a plugin is NOT a MelonMod)", () =>
{
    var p = T("MelonLoader.MelonPlugin");
    var b = p?.BaseType;
    return b is not null &&
           b.Name == "MelonTypeBase`1" &&
           b.BaseType?.FullName == "MelonLoader.MelonBase" &&
           p!.FullName != "MelonLoader.MelonMod";
});

Check("MelonPlugin has a protected parameterless constructor", () =>
    T("MelonLoader.MelonPlugin")!
        .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
        .Any(c => c.IsFamily && c.GetParameters().Length == 0));

Check("virtual void MelonPlugin.OnApplicationEarlyStart()  <- the entry point used", () =>
{
    var m = T("MelonLoader.MelonPlugin")!.GetMethod("OnApplicationEarlyStart");
    return m is { IsVirtual: true, IsPublic: true } && m.GetParameters().Length == 0;
});

Check("virtual void MelonBase.OnApplicationQuit()", () =>
{
    var m = T("MelonLoader.MelonBase")!.GetMethod("OnApplicationQuit");
    return m is { IsVirtual: true, IsPublic: true } && m.GetParameters().Length == 0;
});

Check("MelonBase.LoggerInstance is MelonLogger.Instance", () =>
    T("MelonLoader.MelonBase")!.GetProperty("LoggerInstance")?.PropertyType.FullName == "MelonLoader.MelonLogger+Instance");

Check("MelonLogger.Instance.Msg(String)", () => Method("MelonLoader.MelonLogger+Instance", "Msg", "String"));
Check("MelonLogger.Instance.Warning(String)", () => Method("MelonLoader.MelonLogger+Instance", "Warning", "String"));
Check("MelonLogger.Instance.Error(String)", () => Method("MelonLoader.MelonLogger+Instance", "Error", "String"));

Check("MelonInfoAttribute(Type type, String name, String version, String author[, String downloadLink])", () =>
    T("MelonLoader.MelonInfoAttribute")!.GetConstructors().Any(c =>
    {
        var ps = c.GetParameters();
        return ps.Length >= 4 &&
               ps.Take(4).Select(p => p.ParameterType.Name).SequenceEqual(new[] { "Type", "String", "String", "String" }) &&
               ps.Skip(4).All(p => p.IsOptional);
    }));

Check("MelonGameAttribute(String, String)", () =>
    T("MelonLoader.MelonGameAttribute")!.GetConstructors()
        .Any(c => c.GetParameters().Select(p => p.ParameterType.Name).SequenceEqual(new[] { "String", "String" })));

Check("MelonPriorityAttribute(Int32)", () =>
    T("MelonLoader.MelonPriorityAttribute")!.GetConstructors()
        .Any(c => c.GetParameters().Select(p => p.ParameterType.Name).SequenceEqual(new[] { "Int32" })));

foreach (var property in new[] { "GameRootDirectory", "ModsDirectory", "PluginsDirectory", "UserLibsDirectory", "UserDataDirectory" })
    Check($"MelonEnvironment.{property} (static string)", () => StaticStringProperty("MelonLoader.Utils.MelonEnvironment", property));

Console.WriteLine();
Console.WriteLine("The compiled plugin:");

var pluginType = updater.GetType("Expansions.Updater.UpdaterPlugin", throwOnError: false);

Check("Expansions.Updater.UpdaterPlugin exists and is public", () => pluginType is { IsPublic: true, IsAbstract: false });

Check("UpdaterPlugin's base type is MelonLoader.MelonPlugin", () =>
    pluginType?.BaseType?.FullName == "MelonLoader.MelonPlugin");

Check("UpdaterPlugin has a public parameterless constructor (MelonLoader uses Activator.CreateInstance)", () =>
    pluginType!.GetConstructor(Type.EmptyTypes) is not null);

// MetadataLoadContext cannot walk GetBaseDefinition, so an override is identified the way the CLR
// encodes one: virtual, and ReuseSlot rather than NewSlot. A "new" method would be NewSlot, and would
// silently never be called by MelonLoader.
bool Overrides(string method, string declaredBy)
{
    var m = pluginType!.GetMethod(method, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
    if (m is null || !m.IsVirtual || (m.Attributes & MethodAttributes.NewSlot) != 0)
        return false;

    for (var t = pluginType.BaseType; t is not null; t = t.BaseType)
    {
        var inherited = t.GetMethod(method, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (inherited is not null)
            return t.FullName == declaredBy;
    }

    return false;
}

Check("UpdaterPlugin overrides MelonPlugin.OnApplicationEarlyStart",
    () => Overrides("OnApplicationEarlyStart", "MelonLoader.MelonPlugin"));

Check("UpdaterPlugin overrides MelonBase.OnApplicationQuit",
    () => Overrides("OnApplicationQuit", "MelonLoader.MelonBase"));

Check("[assembly: MelonInfo(...)] is present and its SystemType is UpdaterPlugin", () =>
{
    var info = updater.GetCustomAttributesData()
        .FirstOrDefault(a => a.AttributeType.FullName == "MelonLoader.MelonInfoAttribute");

    return info is not null &&
           info.ConstructorArguments.Count >= 2 &&
           (info.ConstructorArguments[0].Value as Type)?.FullName == "Expansions.Updater.UpdaterPlugin";
});

Check("[assembly: MelonGame(\"TVGS\", \"Schedule I\")]", () =>
{
    var g = updater.GetCustomAttributesData()
        .FirstOrDefault(a => a.AttributeType.FullName == "MelonLoader.MelonGameAttribute");

    return g is not null &&
           (string?)g.ConstructorArguments[0].Value == "TVGS" &&
           (string?)g.ConstructorArguments[1].Value == "Schedule I";
});

Check("the plugin references nothing but MelonLoader and the BCL (no S1API, no Unity, no Il2Cpp, no Core)", () =>
{
    var offenders = updater.GetReferencedAssemblies()
        .Select(a => a.Name ?? string.Empty)
        .Where(name =>
            name.StartsWith("Unity", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Il2Cpp", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("S1API", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Expansions.", StringComparison.OrdinalIgnoreCase) ||
            name == "0Harmony")
        .ToList();

    foreach (var offender in offenders)
        Console.WriteLine($"          referenced: {offender}");

    return offenders.Count == 0;
});

Console.WriteLine();
Console.WriteLine($"{checks - failures.Count}/{checks} verified.");

foreach (var failure in failures)
    Console.WriteLine($"  FAILED: {failure}");

return failures.Count == 0 ? 0 : 1;
