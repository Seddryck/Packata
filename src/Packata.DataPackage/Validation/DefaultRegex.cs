using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Validation;
internal static class DefaultRegex
{
    public static string UrlableRegex => "^([-a-zA-Z0-9._])+$";
    public static string PathRegex => "^((?=[^./~])(?!file:)((?!\\/\\.\\.\\/)(?!\\\\)(?!:\\/\\/).)*|(http|ftp)s?:\\/\\/.*)$";
    public static string EmailRegex => @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
}
