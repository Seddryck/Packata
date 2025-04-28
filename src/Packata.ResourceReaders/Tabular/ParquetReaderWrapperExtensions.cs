using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader;

namespace Packata.ResourceReaders.Tabular;
internal static class ParquetReaderWrapperExtensions
{
    public static IDataReader ToDataReader(this ParquetReaderWrapper reader, IEnumerable<Func<Task<Stream>>> streamFactories)
    {
        if (!streamFactories.Any())
            throw new InvalidOperationException(
                "The resource does not contain any paths, but at least one is required to create a DataReader.");

<<<<<<< HEAD
        var streams = streamFactories.Select(factory => factory().ConfigureAwait(false).GetAwaiter().GetResult()).ToArray();
=======
        var streams = streamFactories.Select(factory => factory().Result).ToArray();
>>>>>>> b54efe1b3ea41dc884834ea15bf6d4852c1550cb
        return reader.ToDataReader(streams);
    }

    public static IDataReader ToDataReader(this ParquetReaderWrapper reader, Func<Task<Stream>> streamFactory)
<<<<<<< HEAD
        => reader.ToDataReader(streamFactory().ConfigureAwait(false).GetAwaiter().GetResult());
=======
        => reader.ToDataReader(streamFactory().Result);
>>>>>>> b54efe1b3ea41dc884834ea15bf6d4852c1550cb
}
