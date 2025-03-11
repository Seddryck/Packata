using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using DubUrl;
using DubUrl.Registering;

namespace Packata.Core.ResourceReading;
public class TableDatabaseReaderBuilder : IResourceReaderBuilder
{
    private readonly ProviderFactoriesRegistrator providerFactories;

    public TableDatabaseReaderBuilder()
        : this(new ProviderFactoriesRegistrator())
    { }

    public TableDatabaseReaderBuilder(ProviderFactoriesRegistrator providerFactories)
        => this.providerFactories = providerFactories;

    public void Configure(Resource resource)
    {
        providerFactories.Register();
    }

    public IResourceReader Build()
        => new TableDatabaseReader();
}
