using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DubUrl;

namespace Packata.Core.ResourceReading;
internal class TableDatabaseReader : IResourceReader
{
    private readonly ConnectionUrlFactory connectionUrlFactory;

    public TableDatabaseReader()
        : this(new ConnectionUrlFactory(new DubUrl.Mapping.SchemeMapperBuilder()))
    { }

    public TableDatabaseReader(ConnectionUrlFactory connectionUrlFactory)
        => this.connectionUrlFactory = connectionUrlFactory;

    public IDataReader ToDataReader(Resource resource)
    {
        var url = resource.Connection?.ConnectionUrl ?? throw new ArgumentException();
        var dialect = (resource.Dialect as TableDatabaseDialect) ?? throw new InvalidOperationException();

        var connectionUrl = connectionUrlFactory.Instantiate(url);
        using (var connection = connectionUrl.Open())
        {
            var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {dialect.Table}";
            return command.ExecuteReader();
        }
    }
}
