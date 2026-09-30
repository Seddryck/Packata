using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.DataPackage;
public class DuplicatedConstraintException : Exception
{
    public DuplicatedConstraintException(Constraint constraint)
        : base($"Duplicated constraint: {constraint.GetType().Name}")
    { }
}
