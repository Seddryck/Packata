using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader;
using Packata.Core;
using Packata.Core.ResourceReading;

namespace Packata.ResourceReaders.Tabular;
internal class DelimitedReader : IResourceReader
{
    private CsvReader CsvReader { get; }

    public DelimitedReader(CsvReader csvReader)
        => CsvReader = csvReader;

    public IDataReader ToDataReader(Resource resource)
    {
        if (resource.Paths.Count == 0)
            throw new InvalidOperationException(
                "The resource does not contain any paths, but at least one is required to create a DataReader.");

        var stream = resource.Paths[0].ToStream();
        return CsvReader.ToDataReader(stream);
    }
}
