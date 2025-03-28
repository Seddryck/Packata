using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
public class DuplicatedConstraintException : ApplicationException
{
    public DuplicatedConstraintException(Constraint constraint)
        : base($"Duplicated constraint: {constraint.GetType().Name}")
    { }
}
