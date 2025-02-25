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
                "required" => new RequiredConstraint { Value = Convert.ToBoolean(value) },
                "unique" => new UniqueConstraint { Value = Convert.ToBoolean(value) },
                "minLength" => new MinLengthConstraint { Value = Convert.ToInt32(value) },
                "maxLength" => new MaxLengthConstraint { Value = Convert.ToInt32(value) },
                "minimum" => new MinimumConstraint { Value = value },
                "maximum" => new MaximumConstraint { Value = value },
                "exclusiveMinimum" => new ExclusiveMinimumConstraint { Value = value },
                "exclusiveMaximum" => new ExclusiveMaximumConstraint { Value = value },
                "pattern" => new PatternConstraint { Value = (string)value },
                _ => throw new NotSupportedException($"The constraint '{name}' is not supported.")
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new ArgumentException($"Invalid value format for constraint '{name}': {value}", nameof(value), ex);
        }
    }
}
