using System.Globalization;
using System.Text;

namespace Expansions.Core.Updates;

internal enum JsonKind
{
    Null,
    Boolean,
    Number,
    String,
    Array,
    Object,
}

/// <summary>
/// One node of a parsed JSON document.
/// <para>
/// Deliberately a read-by-name tree rather than a mapping onto typed classes, because the manifest
/// contract requires unknown <em>fields</em> to be ignored: a reader that only asks for the members it
/// knows tolerates an additive schema change for free, whereas a strict deserialiser has to be told to.
/// Every accessor takes a fallback and never throws, so a missing or mistyped member degrades to a
/// default instead of taking the update check down.
/// </para>
/// </summary>
internal sealed class JsonValue
{
    internal static readonly JsonValue Null = new(JsonKind.Null, null, false, null, null);

    private static readonly JsonValue[] NoItems = Array.Empty<JsonValue>();
    private static readonly JsonValue True = new(JsonKind.Boolean, "true", true, null, null);
    private static readonly JsonValue False = new(JsonKind.Boolean, "false", false, null, null);

    private readonly Dictionary<string, JsonValue>? _members;
    private readonly JsonValue[]? _items;
    private readonly string? _text;
    private readonly bool _boolean;

    private JsonValue(
        JsonKind kind,
        string? text,
        bool boolean,
        JsonValue[]? items,
        Dictionary<string, JsonValue>? members)
    {
        Kind = kind;
        _text = text;
        _boolean = boolean;
        _items = items;
        _members = members;
    }

    internal JsonKind Kind { get; }

    internal bool IsObject => Kind == JsonKind.Object;

    internal bool IsArray => Kind == JsonKind.Array;

    /// <summary>Array elements, empty for anything else.</summary>
    internal IReadOnlyList<JsonValue> Items => _items ?? NoItems;

    internal static JsonValue FromBoolean(bool value) => value ? True : False;

    internal static JsonValue FromNumber(string raw) => new(JsonKind.Number, raw, false, null, null);

    internal static JsonValue FromString(string value) => new(JsonKind.String, value, false, null, null);

    internal static JsonValue FromItems(JsonValue[] items) => new(JsonKind.Array, null, false, items, null);

    internal static JsonValue FromMembers(Dictionary<string, JsonValue> members) =>
        new(JsonKind.Object, null, false, null, members);

    /// <summary>The named member, or null when it is absent. JSON names are case-sensitive.</summary>
    internal JsonValue? Member(string name) =>
        _members is not null && _members.TryGetValue(name, out var value) ? value : null;

    internal bool Has(string name) => Member(name) is not null;

    /// <summary>This node's text. Numbers give their raw form; anything else gives the empty string.</summary>
    internal string AsString() => Kind switch
    {
        JsonKind.String or JsonKind.Number => _text ?? string.Empty,
        JsonKind.Boolean => _boolean ? "true" : "false",
        _ => string.Empty,
    };

    internal string StringOr(string name, string fallback = "")
    {
        var member = Member(name);
        return member is null || member.Kind != JsonKind.String
            ? fallback
            : member._text ?? fallback;
    }

    internal long IntegerOr(string name, long fallback)
    {
        var member = Member(name);
        if (member is null || member.Kind != JsonKind.Number)
            return fallback;

        var text = member._text ?? string.Empty;

        // Written as an integer by the publisher, but a JSON number is allowed a fraction and an
        // exponent, so parse the general form and truncate rather than refusing "1.0e3".
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
            return whole;

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var real)
            ? (long)real
            : fallback;
    }

    internal bool BooleanOr(string name, bool fallback)
    {
        var member = Member(name);
        return member is null || member.Kind != JsonKind.Boolean ? fallback : member._boolean;
    }

    /// <summary>The named array's elements. Empty when the member is absent or is not an array.</summary>
    internal IReadOnlyList<JsonValue> ArrayOr(string name)
    {
        var member = Member(name);
        return member is null || member.Kind != JsonKind.Array ? NoItems : member.Items;
    }
}

/// <summary>
/// A small, allocation-conscious JSON reader.
/// <para>
/// Core has no JSON dependency and adding one is not free here: the only libraries guaranteed to be in
/// the process are MelonLoader's own, and binding to its private copy of Newtonsoft would couple the
/// suite's update path to a loader implementation detail. The manifest is a few kilobytes of fixed
/// shape, so a reader that is fully verifiable at compile time is the cheaper answer.
/// </para>
/// <para>
/// Hardened for content fetched off the internet: input length, nesting depth and element counts are
/// all capped, so a hostile or truncated response fails fast with a reason instead of exhausting the
/// stack or the heap.
/// </para>
/// </summary>
internal static class Json
{
    /// <summary>Roughly two hundred times the size of a real manifest.</summary>
    internal const int MaxLength = 512 * 1024;

    private const int MaxDepth = 24;
    private const int MaxElements = 4096;

    internal static bool TryParse(string? text, out JsonValue value, out string failure)
    {
        value = JsonValue.Null;

        if (string.IsNullOrWhiteSpace(text))
        {
            failure = "the response was empty";
            return false;
        }

        if (text!.Length > MaxLength)
        {
            failure = $"the response is {text.Length} characters, past the {MaxLength} the reader accepts";
            return false;
        }

        var reader = new Reader(text);

        try
        {
            reader.SkipWhitespace();
            value = reader.ReadValue(0);
            reader.SkipWhitespace();

            if (!reader.AtEnd)
            {
                failure = $"unexpected trailing content at offset {reader.Offset}";
                value = JsonValue.Null;
                return false;
            }

            failure = string.Empty;
            return true;
        }
        catch (FormatException ex)
        {
            failure = ex.Message;
            value = JsonValue.Null;
            return false;
        }
    }

    /// <summary>Escapes <paramref name="text"/> for use as a JSON string body, quotes excluded.</summary>
    internal static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(text!.Length + 8);

        foreach (var c in text)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (c < ' ')
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    private struct Reader
    {
        private readonly string _text;
        private int _offset;

        internal Reader(string text)
        {
            _text = text;
            _offset = 0;
        }

        internal int Offset => _offset;

        internal bool AtEnd => _offset >= _text.Length;

        internal void SkipWhitespace()
        {
            while (_offset < _text.Length && _text[_offset] is ' ' or '\t' or '\r' or '\n')
                _offset++;
        }

        internal JsonValue ReadValue(int depth)
        {
            if (depth > MaxDepth)
                throw Bad($"nesting is deeper than {MaxDepth} levels");

            if (AtEnd)
                throw Bad("the document ended where a value was expected");

            return _text[_offset] switch
            {
                '{' => ReadObject(depth),
                '[' => ReadArray(depth),
                '"' => JsonValue.FromString(ReadString()),
                't' => ReadKeyword("true", JsonValue.FromBoolean(true)),
                'f' => ReadKeyword("false", JsonValue.FromBoolean(false)),
                'n' => ReadKeyword("null", JsonValue.Null),
                _ => ReadNumber(),
            };
        }

        private JsonValue ReadObject(int depth)
        {
            _offset++; // '{'
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal);

            SkipWhitespace();
            if (!AtEnd && _text[_offset] == '}')
            {
                _offset++;
                return JsonValue.FromMembers(members);
            }

            while (true)
            {
                SkipWhitespace();

                if (AtEnd || _text[_offset] != '"')
                    throw Bad($"a member name was expected at offset {_offset}");

                var name = ReadString();

                SkipWhitespace();
                if (AtEnd || _text[_offset] != ':')
                    throw Bad($"a colon was expected after '{name}'");

                _offset++;
                SkipWhitespace();

                // Last writer wins on a duplicate name, which is what every mainstream reader does.
                members[name] = ReadValue(depth + 1);

                if (members.Count > MaxElements)
                    throw Bad($"an object has more than {MaxElements} members");

                SkipWhitespace();
                if (AtEnd)
                    throw Bad("the document ended inside an object");

                if (_text[_offset] == ',')
                {
                    _offset++;
                    continue;
                }

                if (_text[_offset] == '}')
                {
                    _offset++;
                    return JsonValue.FromMembers(members);
                }

                throw Bad($"a comma or closing brace was expected at offset {_offset}");
            }
        }

        private JsonValue ReadArray(int depth)
        {
            _offset++; // '['
            var items = new List<JsonValue>();

            SkipWhitespace();
            if (!AtEnd && _text[_offset] == ']')
            {
                _offset++;
                return JsonValue.FromItems(items.ToArray());
            }

            while (true)
            {
                SkipWhitespace();
                items.Add(ReadValue(depth + 1));

                if (items.Count > MaxElements)
                    throw Bad($"an array has more than {MaxElements} elements");

                SkipWhitespace();
                if (AtEnd)
                    throw Bad("the document ended inside an array");

                if (_text[_offset] == ',')
                {
                    _offset++;
                    continue;
                }

                if (_text[_offset] == ']')
                {
                    _offset++;
                    return JsonValue.FromItems(items.ToArray());
                }

                throw Bad($"a comma or closing bracket was expected at offset {_offset}");
            }
        }

        private string ReadString()
        {
            _offset++; // opening quote
            var builder = new StringBuilder();

            while (true)
            {
                if (AtEnd)
                    throw Bad("the document ended inside a string");

                var c = _text[_offset++];

                if (c == '"')
                    return builder.ToString();

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (AtEnd)
                    throw Bad("the document ended inside an escape sequence");

                var escape = _text[_offset++];
                switch (escape)
                {
                    case '"':
                        builder.Append('"');
                        break;
                    case '\\':
                        builder.Append('\\');
                        break;
                    case '/':
                        builder.Append('/');
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        builder.Append(ReadUnicodeEscape());
                        break;
                    default:
                        throw Bad($"'\\{escape}' is not a JSON escape");
                }
            }
        }

        private char ReadUnicodeEscape()
        {
            if (_offset + 4 > _text.Length)
                throw Bad("a \\u escape ran off the end of the document");

            var hex = _text.Substring(_offset, 4);
            if (!ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                throw Bad($"'\\u{hex}' is not four hex digits");

            _offset += 4;
            return (char)code;
        }

        private JsonValue ReadNumber()
        {
            var start = _offset;

            if (!AtEnd && (_text[_offset] == '-' || _text[_offset] == '+'))
                _offset++;

            while (!AtEnd && (char.IsDigit(_text[_offset]) || _text[_offset] is '.' or 'e' or 'E' or '-' or '+'))
                _offset++;

            if (_offset == start)
                throw Bad($"'{_text[start]}' does not start a JSON value");

            return JsonValue.FromNumber(_text[start.._offset]);
        }

        private JsonValue ReadKeyword(string keyword, JsonValue value)
        {
            if (_offset + keyword.Length > _text.Length ||
                string.CompareOrdinal(_text, _offset, keyword, 0, keyword.Length) != 0)
            {
                throw Bad($"'{keyword}' was expected at offset {_offset}");
            }

            _offset += keyword.Length;
            return value;
        }

        private static FormatException Bad(string reason) => new(reason);
    }
}
