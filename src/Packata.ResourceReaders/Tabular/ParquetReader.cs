using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;
using Packata.Core.ResourceReading;

namespace Packata.ResourceReaders.Tabular;
internal class ParquetReader : IResourceReader
{
    private ParquetReaderWrapper Reader { get; }

    public ParquetReader(ParquetReaderWrapper reader)
        => Reader = reader;

    public IDataReader ToDataReader(Resource resource)
    {
        if (resource.Paths.Count == 0)
            throw new InvalidOperationException(
                "The resource does not contain any paths, but at least one is required to create a DataReader.");

        return resource.Paths.Count > 1
            ? Reader.ToDataReader(resource.Paths.Select(p => p.ToStream()))
            : Reader.ToDataReader(resource.Paths[0].ToStream());
    }
}
