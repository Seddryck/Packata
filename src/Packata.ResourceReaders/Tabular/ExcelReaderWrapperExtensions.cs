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
<<<<<<< HEAD
        => reader.ToDataReader(streamFactory().GetAwaiter().GetResult());
=======
        => reader.ToDataReader(streamFactory().Result);
>>>>>>> b54efe1b3ea41dc884834ea15bf6d4852c1550cb
}
