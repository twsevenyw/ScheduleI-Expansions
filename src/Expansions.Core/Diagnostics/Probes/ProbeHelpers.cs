using System.Reflection;

namespace Expansions.Core.Diagnostics.Probes;

/// <summary>Shared rendering used by more than one probe.</summary>
internal static class ProbeHelpers
{
    private const BindingFlags PublicDeclared =
        BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Instance;

    /// <summary>
    /// Reads a named set of statics off one type. Returns the rows plus how many resolved, so the
    /// caller can decide between OK and NOT_FOUND.
    /// </summary>
    internal static (List<IReadOnlyList<string>> Rows, int Found) ReadStatics(string typeName, IReadOnlyList<string> members)
    {
        var rows = new List<IReadOnlyList<string>>(members.Count);
        var found = 0;

        var type = GameReflection.FindType(typeName);
        if (type is null)
        {
            foreach (var member in members)
                rows.Add(new[] { member, "-", "type not found" });

            return (rows, 0);
        }

        foreach (var member in members)
        {
            if (GameReflection.TryReadStatic(type, member, out var value, out var failure))
            {
                found++;
                rows.Add(new[] { member, GameReflection.TypeNameOf(value), GameReflection.Format(value) });
            }
            else
            {
                rows.Add(new[] { member, "-", failure });
            }
        }

        return (rows, found);
    }

    /// <summary>Emits a "Type | Member | Value" block for one declaring type.</summary>
    internal static int StaticSection(ProbeResult result, string typeName, IReadOnlyList<string> members)
    {
        var (rows, found) = ReadStatics(typeName, members);

        result.Heading($"`{typeName}` — {found}/{members.Count} resolved");
        result.Table(new[] { "Member", "CLR type", "Value" }, rows);

        return found;
    }

    /// <summary>
    /// Dumps a type's public declared surface. Il2CppInterop's private
    /// <c>NativeMethodInfoPtr_*</c> plumbing is excluded because only public members are asked for.
    /// </summary>
    internal static void PublicSurface(ProbeResult result, Type type, bool readStaticValues)
    {
        var properties = type.GetProperties(PublicDeclared)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToArray();

        var propertyRows = new List<IReadOnlyList<string>>(properties.Length);
        foreach (var property in properties)
        {
            var accessor = property.GetGetMethod(true) ?? property.GetSetMethod(true);
            var isStatic = accessor?.IsStatic ?? false;
            var value = "-";

            if (isStatic && readStaticValues && property.GetIndexParameters().Length == 0)
            {
                value = GameReflection.TryReadStatic(type, property.Name, out var read, out var failure)
                    ? GameReflection.Format(read)
                    : failure;
            }

            propertyRows.Add(new[]
            {
                isStatic ? "static" : "instance",
                Simple(property.PropertyType),
                property.Name,
                property.GetSetMethod(true) is not null ? "get/set" : "get",
                value,
            });
        }

        result.Heading($"Properties and fields ({propertyRows.Count})");
        if (propertyRows.Count == 0)
            result.Bullet("None declared.");
        else
            result.Table(new[] { "Scope", "Type", "Name", "Access", "Value" }, propertyRows);

        var methods = type.GetMethods(PublicDeclared)
            .Where(m => !m.IsSpecialName)
            .Select(m => $"{(m.IsStatic ? "static " : string.Empty)}{Simple(m.ReturnType)} {m.Name}(" +
                         string.Join(", ", m.GetParameters().Select(p => Simple(p.ParameterType) + " " + p.Name)) + ")")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        result.Heading($"Methods ({methods.Length})");
        if (methods.Length == 0)
            result.Bullet("None declared.");
        else
            result.Code(methods, "csharp");
    }

    /// <summary>Trims interop type names down to something a table can hold.</summary>
    internal static string Simple(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name;
        var tick = name.IndexOf('`');
        if (tick > 0)
            name = name[..tick];

        return name + "<" + string.Join(", ", type.GetGenericArguments().Select(Simple)) + ">";
    }

    internal static string ReadString(object? instance, string member)
    {
        if (!GameReflection.TryRead(instance, member, out var value, out var failure))
            return "<" + failure + ">";

        return value as string ?? GameReflection.Format(value);
    }

    internal static string ReadFormatted(object? instance, string member)
    {
        if (!GameReflection.TryRead(instance, member, out var value, out var failure))
            return "<" + failure + ">";

        return GameReflection.Format(value);
    }

    internal static bool? ReadBool(object? instance, string member) =>
        GameReflection.TryRead(instance, member, out var value, out _) && value is bool flag ? flag : null;

    internal static int? ReadInt(object? instance, string member) =>
        GameReflection.TryRead(instance, member, out var value, out _) && value is int number ? number : null;

    internal static string Yes(bool? value) => value switch
    {
        true => "yes",
        false => "no",
        _ => "?",
    };
}
