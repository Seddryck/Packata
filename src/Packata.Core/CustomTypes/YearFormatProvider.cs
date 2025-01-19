using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.CustomTypes;
internal class YearFormatProvider
{
    public static IFormatProvider Instance
    {
        get => CultureInfo.InvariantCulture;
    }
}
