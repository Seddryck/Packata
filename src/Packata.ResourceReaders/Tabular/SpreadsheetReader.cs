using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;
using Packata.Core.ResourceReading;

namespace Packata.ResourceReaders.Tabular;
internal class SpreadsheetReader : IResourceReader
{
    private ExcelReaderWrapper Reader { get; }

    public SpreadsheetReader(ExcelReaderWrapper reader)
        => Reader = reader;

    public IDataReader ToDataReader(Resource resource)
    {
        if (resource.Paths.Count == 0)
            throw new InvalidOperationException(
                "The resource does not contain any paths, but at least one is required to create a DataReader.");

        if (resource.Paths.Count > 1)
            throw new InvalidOperationException(
                "The resource contains more than a single path, cannot create a DataReader for multiple file when using the spreadsheet reader.");

        var path = resource.Paths.First();
        if (!path.ExistsAsync().Result)
            throw new FileNotFoundException($"The path '{path.RelativePath}' doesn't exist.");
        try
        {
            return Reader.ToDataReader(path.OpenAsync);
        }
        catch (AggregateException ex)
        {
            if (ex.InnerException is not null)
                throw ex.InnerException;
            else
                throw;
        }
    }
}
