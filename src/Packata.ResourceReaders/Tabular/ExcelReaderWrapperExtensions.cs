using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ExcelDataReader;
using Packata.Core;

namespace Packata.ResourceReaders.Tabular;
internal static class ExcelReaderWrapperExtensions
{
    public static IDataReader ToDataReader(this ExcelReaderWrapper reader, Func<Task<Stream>> streamFactory)
        => reader.ToDataReader(streamFactory().GetAwaiter().GetResult());
}
