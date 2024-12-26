using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Validation;
internal interface IValidator<T>
{
    bool IsValid(T obj);
}
