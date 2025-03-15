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
        var stream = resource.Paths[0].ToStream();
        return CsvReader.ToDataReader(stream);
    }
}
