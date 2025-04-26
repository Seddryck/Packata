using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader;

namespace Packata.ResourceReaders.Tabular;
internal static class CsvReaderExtensions
{
    public static IDataReader ToDataReader(this CsvReader reader, IEnumerable<Func<Task<Stream>>> streamFactories)
    {
        if (!streamFactories.Any())
            throw new InvalidOperationException(
                "The resource does not contain any paths, but at least one is required to create a DataReader.");

        var streams = streamFactories.Select(factory => factory().Result).ToArray();
        return reader.ToDataReader(streams);
    }

    public static IDataReader ToDataReader(this CsvReader reader, Func<Task<Stream>> streamFactory)
        => reader.ToDataReader(streamFactory().Result);
}
