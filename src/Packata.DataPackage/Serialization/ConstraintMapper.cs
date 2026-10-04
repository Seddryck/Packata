using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.DataPackage.Serialization;
internal class ConstraintMapper
{
    public Constraint Map(string name, object? value)
    {
        try
        {
            return name switch
            {
                "required" => new RequiredConstraint(Convert.ToBoolean(RequireValue(name, value))),
                "unique" => new UniqueConstraint(Convert.ToBoolean(RequireValue(name, value))),
                "minLength" => new MinLengthConstraint(Convert.ToInt32(RequireValue(name, value))),
                "maxLength" => new MaxLengthConstraint(Convert.ToInt32(RequireValue(name, value))),
                "minimum" => new MinimumConstraint(RequireValue(name, value)),
                "maximum" => new MaximumConstraint(RequireValue(name, value)),
                "exclusiveMinimum" => new ExclusiveMinimumConstraint(RequireValue(name, value)),
                "exclusiveMaximum" => new ExclusiveMaximumConstraint(RequireValue(name, value)),
                "pattern" => new PatternConstraint(Convert.ToString(RequireValue(name, value))!),
                "enum" => new EnumConstraint(AsList(name, value)),
                "jsonSchema" => new JsonSchemaConstraint(AsDictionary(name, value)),
                _ => throw new NotSupportedException($"The constraint '{name}' is not supported.")
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new ArgumentException($"Invalid value format for constraint '{name}': {value}", nameof(value), ex);
        }
    }

    private static object RequireValue(string name, object? value)
        => value ?? throw new ArgumentException($"Constraint '{name}' cannot be null.", nameof(value));

    private static IReadOnlyList<object?> AsList(string name, object? value)
        => value switch
        {
            IReadOnlyList<object?> list => list,
            IEnumerable<object?> values => [.. values],
            _ => throw new ArgumentException($"Constraint '{name}' must be an array.", nameof(value))
        };

    private static IReadOnlyDictionary<string, object?> AsDictionary(string name, object? value)
        => value switch
        {
            IReadOnlyDictionary<string, object?> dictionary => dictionary,
            IDictionary<object, object> dictionary => dictionary.ToDictionary(
                pair => Convert.ToString(pair.Key)!, pair => (object?)pair.Value),
            _ => throw new ArgumentException($"Constraint '{name}' must be an object.", nameof(value))
        };
}
