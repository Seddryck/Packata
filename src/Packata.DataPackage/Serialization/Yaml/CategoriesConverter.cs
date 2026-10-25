using System.Globalization;
using Packata.Core.Parsing;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Packata.DataPackage.Serialization.Yaml;

internal class CategoriesConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(List<ICategory>) || type == typeof(CategoryCollection);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (parser.Current is MappingStart && type == typeof(CategoryCollection))
            return new CategoryCollection((CategoryTableReference)new CategoryMetadataConverter().ReadYaml(parser, typeof(CategoryTableReference), rootDeserializer));
        var result = ReadInline(parser);
        return type == typeof(CategoryCollection) ? new CategoryCollection(result) : (object)result;
    }

    private static List<ICategory> ReadInline(IParser parser)
    {
        parser.Consume<SequenceStart>();
        var result = new List<ICategory>();
        bool? objects = null;
        while (!parser.TryConsume<SequenceEnd>(out _))
        {
            var index = result.Count;
            var isObject = parser.Current is MappingStart;
            if (objects is not null && objects != isObject)
                throw Invalid(parser, index, "Categories must contain either scalar values or objects, not both.");
            objects = isObject;
            result.Add(ReadCategory(parser, index));
        }
        return result;
    }

    private static ICategory ReadCategory(IParser parser, int index)
    {
        if (parser.TryConsume<Scalar>(out var scalar))
        {
            var value = ReadValue(scalar, index);
            return value is string text ? new CategoryLabel(text) : new CategoryInteger((long)value);
        }
        if (!parser.TryConsume<MappingStart>(out _)) throw Invalid(parser, index, "Expected a scalar or category object.");
        object? categoryValue = null;
        string? label = null;
        var keys = new HashSet<string>();
        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>();
            if (!keys.Add(key.Value)) throw Invalid(parser, index, $"Duplicate property {key.Value}.");
            switch (key.Value)
            {
                case "value": categoryValue = ReadValue(parser.Consume<Scalar>(), index); break;
                case "label": label = ReadLabel(parser, index); break;
                default: parser.SkipThisAndNestedEvents(); break;
            }
        }
        if (categoryValue is null) throw Invalid(parser, index, "A category object requires value.");
        return new Category(categoryValue, label);
    }

    private static string ReadLabel(IParser parser, int index)
    {
        if (ReadValue(parser.Consume<Scalar>(), index) is string text) return text;
        throw Invalid(parser, index, "label must be a string.");
    }

    internal static object ReadValue(Scalar scalar, int index)
    {
        var typedNumber = scalar.Tag == "tag:yaml.org,2002:int" || scalar.Tag == "tag:yaml.org,2002:float";
        var typedOther = scalar.Tag == "tag:yaml.org,2002:bool" || scalar.Tag == "tag:yaml.org,2002:null";
        if (scalar.Tag == "tag:yaml.org,2002:str" || !typedNumber && !typedOther &&
            scalar.Style is ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted or ScalarStyle.Literal or ScalarStyle.Folded)
            return scalar.Value;
        if (ScalarSyntax.IsInteger(scalar.Value))
        {
            if (long.TryParse(scalar.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)) return number;
            throw new YamlException(scalar.Start, scalar.End, $"categories[{index}].value: Integer is outside the signed 64-bit range.");
        }
        if (scalar.Tag == "tag:yaml.org,2002:int" || scalar.Tag == "tag:yaml.org,2002:bool" || scalar.Tag == "tag:yaml.org,2002:float" ||
            scalar.Tag == "tag:yaml.org,2002:null" || scalar.Value.ToLowerInvariant() is "true" or "false" or "null" or "~" or "" or ".inf" or "+.inf" or "-.inf" or ".nan" ||
            ScalarSyntax.IsNumber(scalar.Value))
            throw new YamlException(scalar.Start, scalar.End, $"categories[{index}].value: Expected a string or signed 64-bit integer; quote string codes.");
        return scalar.Value;
    }

    private static YamlException Invalid(IParser parser, int index, string message)
        => new(parser.Current?.Start ?? Mark.Empty, parser.Current?.End ?? Mark.Empty, $"categories[{index}]: {message}");

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        if (value is null) { emitter.Emit(new Scalar("null")); return; }
        if (value is CategoryCollection { Reference: not null } collection)
        { new CategoryMetadataConverter().WriteYaml(emitter, collection.Reference, typeof(CategoryTableReference), serializer); return; }
        emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Block));
        foreach (var category in (IEnumerable<ICategory>)value)
        {
            if (category is Category)
            {
                emitter.Emit(new MappingStart());
                emitter.Emit(new Scalar("value")); WriteValue(emitter, category.Value);
                if (category.Label is not null)
                { emitter.Emit(new Scalar("label")); WriteValue(emitter, category.Label); }
                emitter.Emit(new MappingEnd());
            }
            else WriteValue(emitter, category.Value);
        }
        emitter.Emit(new SequenceEnd());
    }

    private static void WriteValue(IEmitter emitter, object value)
        => emitter.Emit(value is string text
            ? new Scalar(null, null, text, ScalarStyle.DoubleQuoted, true, false)
            : new Scalar(Convert.ToString(value, CultureInfo.InvariantCulture)!));
}
