using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Core.Tokens;

namespace Packata.Core;

public interface ILength { }
public interface IMinimum { }
public interface IMaximum { }
public interface IMinimumExclusive { }
public interface IMaximumExclusive { }

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

public abstract class CheckConstraint : Constraint
{ }

public class MinLengthConstraint : CheckConstraint, IMinimum, ILength
{
    public new int Value { get; }
    public MinLengthConstraint(int value)
        => Value = value;
}

public class MaxLengthConstraint : CheckConstraint, IMaximum, ILength
{
    public new int Value { get; }
    public MaxLengthConstraint(int value)
        => Value = value;
}

public class MinimumConstraint : CheckConstraint, IMinimum
{
    public new object Value { get; }
    public MinimumConstraint(object value)
        => Value = value;
}

public class MaximumConstraint : CheckConstraint, IMaximum
{
    public new object Value { get; }
    public MaximumConstraint(object value)
        => Value = value;
}

public class ExclusiveMinimumConstraint : CheckConstraint, IMinimumExclusive
{
    public ExclusiveMinimumConstraint(object value)
        => Value = value;
}

public class ExclusiveMaximumConstraint : CheckConstraint, IMaximumExclusive
{
    public ExclusiveMaximumConstraint(object value)
        => Value = value;
}

public class PatternConstraint : CheckConstraint
{
    public new string Value { get; }
    public PatternConstraint(string value)
        => Value = value;
}
