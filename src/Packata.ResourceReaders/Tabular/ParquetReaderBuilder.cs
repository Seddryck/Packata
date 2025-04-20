using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using DubUrl.Querying.Dialects;
using Packata.Core;
using Packata.Core.ResourceReading;
using Packata.Core.Validation;

namespace Packata.ResourceReaders.Tabular;
public class ParquetReaderBuilder : IResourceReaderBuilder
{
    public void Configure(Resource resource)
    { }

    public IResourceReader Build()
    {
        var wrapper = new ParquetReaderWrapper();
        return new ParquetReader(wrapper);
    }
}
