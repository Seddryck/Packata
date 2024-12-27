using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader;
using PocketCsvReader.Configuration;

namespace Packata.Core.ResourceReading;
internal class TableDelimitedReader : IResourceReader
{
    private readonly CsvReader _csvReader;

    public TableDelimitedReader(CsvReader csvReader)
        => (_csvReader) = (csvReader);

    public IDataReader ToDataReader(Resource resource)
    {
        var stream = resource.Paths[0].ToStream();
        return _csvReader.ToDataReader(stream);
    }
}
