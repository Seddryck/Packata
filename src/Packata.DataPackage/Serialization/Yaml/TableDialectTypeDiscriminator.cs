using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization.BufferedDeserialization;

namespace Packata.DataPackage.Serialization.Yaml;
internal class TableDialectTypeDiscriminator : ITypeDiscriminator
{
    private static Dictionary<string, Type> GetValueMappings()
        => new()
        {
            { "database", typeof(TableDatabaseDialect)},
            { "delimited", typeof(TableDelimitedDialect)},
            { TableSpreadsheetDialect.DialectType, typeof(TableSpreadsheetDialect)},
            { TableStructuredDialect.DialectType, typeof(TableStructuredDialect)}
        };

    public void Execute(ITypeDiscriminatingNodeDeserializerOptions options)
        => options.AddKeyValueTypeDiscriminator<TableDialect>("type", GetValueMappings());
}
