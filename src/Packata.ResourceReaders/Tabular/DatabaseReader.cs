using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DubUrl;
using Packata.Core;
using Packata.Core.ResourceReading;

namespace Packata.ResourceReaders.Tabular;
internal class DatabaseReader : IResourceReader
{
    private readonly ConnectionUrlFactory _connectionUrlFactory;

    public DatabaseReader(string rootPath)
        : this(new ConnectionUrlFactory(new DubUrl.Mapping.SchemeMapperBuilder(rootPath)))
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
            command.CommandText = $"SELECT * FROM {connectionUrl.Dialect.Renderer.Render(dialect.Table, "identity")}";
            return command.ExecuteReader();
        }
    }
}
