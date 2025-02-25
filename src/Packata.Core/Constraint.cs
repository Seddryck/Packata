using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Core.Tokens;

namespace Packata.Core;
public class Constraint
{
    public object? Value { get; init; }
}

public class UnknownConstraint : Constraint
{
    public string Name { get; }

    public UnknownConstraint(string name, object? value)
        => (Name, Value) = (name, value);
}

public class RequiredConstraint : Constraint
{
    public new bool Value { get; }
    public RequiredConstraint(bool value = false)
        => Value = value;
}

public class UniqueConstraint : Constraint
{
    public new bool Value { get; }
    public UniqueConstraint(bool value = false)
        => Value = value;
}

public class MinLengthConstraint : Constraint
{
    public new int Value { get; }
    public MinLengthConstraint(int value)
        => Value = value;
}

public class MaxLengthConstraint : Constraint
{
    public new int Value { get; }
    public MaxLengthConstraint(int value)
        => Value = value;
}

public class MinimumConstraint : Constraint
{
    public MinimumConstraint(object value)
        => Value = value;
}

public class MaximumConstraint : Constraint
{
    public MaximumConstraint(object value)
        => Value = value;
}

public class ExclusiveMinimumConstraint : Constraint
{
    public ExclusiveMinimumConstraint(object value)
        => Value = value;
}

public class ExclusiveMaximumConstraint : Constraint
{
    public ExclusiveMaximumConstraint(object value)
        => Value = value;
}

public class PatternConstraint : Constraint
{
    public new string Value { get; }
    public PatternConstraint(string value)
        => Value = value;
}
