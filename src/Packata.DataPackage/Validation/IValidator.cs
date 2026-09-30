using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.DataPackage.Validation;
internal interface IValidator<T>
{
    bool IsValid(T obj);
}
