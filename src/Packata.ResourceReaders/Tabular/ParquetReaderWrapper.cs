using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ExcelDataReader;
using Packata.Core;

namespace Packata.ResourceReaders.Tabular;
internal class ParquetReaderWrapper
{
    public IDataReader ToDataReader(Stream stream)
    {
        var reader = ParquetDataReader.CreateAsync(new[] { stream }).Result;
        return reader;
    }

    public IDataReader ToDataReader(IEnumerable<Stream> streams)
    {
        var reader = ParquetDataReader.CreateAsync(streams)
            .ConfigureAwait(false).GetAwaiter().GetResult();
        return reader;
    }
}
