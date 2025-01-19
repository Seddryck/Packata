using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.CustomTypes;
internal class YearMonthFormatProvider
{
    public static IFormatProvider Instance
    {
        get
        {
            var format = (CultureInfo.InvariantCulture.DateTimeFormat.Clone() as DateTimeFormatInfo)!;
            format.YearMonthPattern = "yyyy-MM";
            return format;
        }
    }
}



