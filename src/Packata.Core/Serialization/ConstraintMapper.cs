using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Serialization;
internal class ConstraintMapper
{
    public Constraint Map(string name, object value)
    {
        try
        {
            return name switch
            {
                "required" => new RequiredConstraint(Convert.ToBoolean(value)),
                "unique" => new UniqueConstraint(Convert.ToBoolean(value)),
                "minLength" => new MinLengthConstraint(Convert.ToInt32(value)),
                "maxLength" => new MaxLengthConstraint(Convert.ToInt32(value)),
                "minimum" => new MinimumConstraint(value),
                "maximum" => new MaximumConstraint(value),
                "exclusiveMinimum" => new ExclusiveMinimumConstraint(value),
                "exclusiveMaximum" => new ExclusiveMaximumConstraint(value),
                "pattern" => new PatternConstraint((string)value),
                _ => throw new NotSupportedException($"The constraint '{name}' is not supported.")
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new ArgumentException($"Invalid value format for constraint '{name}': {value}", nameof(value), ex);
        }
    }
}
