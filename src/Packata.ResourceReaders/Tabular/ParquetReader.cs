using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;
using Packata.Core.ResourceReading;
using Packata.Core.Storage;

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

        return resource.Paths.Count == 1
        ? Reader.ToDataReader(ExistsThenOpen(resource.Paths[0]))
        : Reader.ToDataReader(resource.Paths.Select<IPath, Func<Task<Stream>>>(p => ExistsThenOpen(p)));

        static Func<Task<Stream>> ExistsThenOpen(IPath path)
        {
            return async () =>
            {
                if (!await path.ExistsAsync())
                    throw new FileNotFoundException($"The path '{path.Value}' doesn't exist.");
                return await path.OpenAsync();
            };
        }
    }
}
