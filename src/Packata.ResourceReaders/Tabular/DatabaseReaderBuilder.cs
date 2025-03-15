using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DubUrl.Registering;
using Packata.Core;
using Packata.Core.ResourceReading;

namespace Packata.ResourceReaders.Tabular;
public class DatabaseReaderBuilder : IResourceReaderBuilder
{
    private readonly ProviderFactoriesRegistrator providerFactories;
    private string? RootPath { get; set; }

    public DatabaseReaderBuilder()
        : this(new ProviderFactoriesRegistrator())
    { }

    public DatabaseReaderBuilder(ProviderFactoriesRegistrator providerFactories)
        => this.providerFactories = providerFactories;

    public void Configure(Resource resource)
    {
        providerFactories.Register();
        RootPath = resource.RootPath;
    }

    public IResourceReader Build()
        => new DatabaseReader(RootPath ?? throw new InvalidOperationException());
}
