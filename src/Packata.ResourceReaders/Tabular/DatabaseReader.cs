using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DubUrl;
using DubUrl.Mapping;
using Packata.Core;
using Packata.Core.ResourceReading;

namespace Packata.ResourceReaders.Tabular;
internal class DatabaseReader : IResourceReader
{
    private readonly ConnectionUrlFactory _connectionUrlFactory;

    public DatabaseReader(string rootPath)
        : this(new ConnectionUrlFactory(
            new SchemeRegistryBuilder()
                  .WithRootPath(rootPath)
                  .WithAssemblies(typeof(SchemeRegistryBuilder).Assembly)
                  .WithAutoDiscoveredMappings()
                  .Build()))
    { }

    public DatabaseReader(ConnectionUrlFactory connectionUrlFactory)
        => _connectionUrlFactory = connectionUrlFactory;

    public IDataReader ToDataReader(Resource resource)
    {
        var url = resource.Connection?.ConnectionUrl ?? throw new ArgumentException("Connection is not specified", nameof(resource));
        var dialect = resource.Dialect as TableDatabaseDialect ?? throw new InvalidOperationException();

        var connectionUrl = _connectionUrlFactory.Instantiate(url);
        using (var connection = connectionUrl.Open())
        {
            var command = connection.CreateCommand();
            command.CommandText = string.IsNullOrEmpty(dialect.Namespace)
                                ? $"SELECT * FROM {connectionUrl.Dialect.Renderer.Render(dialect.Table, "identity")}"
                                : $"SELECT * FROM {connectionUrl.Dialect.Renderer.Render(dialect.Namespace, "identity")}.{connectionUrl.Dialect.Renderer.Render(dialect.Table, "identity")}";
            return command.ExecuteReader();
        }
    }
}
