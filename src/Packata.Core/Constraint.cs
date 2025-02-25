using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
public class Constraint
{ }

public class RequiredConstraint : Constraint
{
    public bool Value { get; set; }
}

public class UniqueConstraint : Constraint
{
    public bool Value { get; set; }
}

public class MinLengthConstraint : Constraint
{
    public int Value { get; set; }
}

public class MaxLengthConstraint : Constraint
{
    public int Value { get; set; }
}

public class MinimumConstraint : Constraint
{
    public object? Value { get; set; }
}

public class MaximumConstraint : Constraint
{
    public object? Value { get; set; }
}

public class ExclusiveMinimumConstraint : Constraint
{
    public object? Value { get; set; }
}

public class ExclusiveMaximumConstraint : Constraint
{
    public object? Value { get; set; }
}

public class PatternConstraint : Constraint
{
    public string? Value { get; set; }
}
