using System.Globalization;
using System.Text;

namespace Packata.Application.Generation;

internal static class CSharpNames
{
    private static readonly IReadOnlyDictionary<Type, string> Aliases = new Dictionary<Type, string>
    {
        [typeof(bool)] = "bool", [typeof(byte)] = "byte", [typeof(sbyte)] = "sbyte", [typeof(short)] = "short",
        [typeof(ushort)] = "ushort", [typeof(int)] = "int", [typeof(uint)] = "uint", [typeof(long)] = "long",
        [typeof(ulong)] = "ulong", [typeof(float)] = "float", [typeof(double)] = "double", [typeof(decimal)] = "decimal",
        [typeof(string)] = "string", [typeof(object)] = "object"
    };
    private static readonly HashSet<string> Keywords = new(
        ("abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while record required init file scoped var dynamic global").Split(' '), StringComparer.Ordinal);

    internal static string Identifier(string text, bool type = false)
    {
        var builder = new StringBuilder();
        foreach (var character in text.TrimStart('@'))
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        if (builder.Length == 0) builder.Append('_');
        if (char.IsDigit(builder[0])) builder.Insert(0, '_');
        if (type) builder[0] = char.ToUpper(builder[0], CultureInfo.InvariantCulture);
        var name = builder.ToString();
        return Keywords.Contains(name) ? "@" + name : name;
    }

    internal static string Unique(string text, HashSet<string> used, bool type = false)
    {
        var root = Identifier(text, type);
        var result = root;
        var suffix = 2;
        while (!used.Add(result.TrimStart('@'))) result = root + "_" + suffix++;
        return result;
    }

    internal static string? Namespace(string? value)
    {
        if (value is null) return null;
        if (value.Split('.').Any(p => string.IsNullOrWhiteSpace(p))) throw new ArgumentException("Namespace must have non-empty dot-separated identifiers.");
        return string.Join('.', value.Split('.').Select(p => Identifier(p)));
    }

    internal static string TypeExpression(Type type)
    {
        if (type.IsArray) return TypeExpression(type.GetElementType()!) + "[]";
        return Aliases.TryGetValue(type, out var alias) ? alias : "global::" + type.FullName!.Replace('+', '.');
    }
}
