﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization.BufferedDeserialization;

namespace Packata.Core.Serialization.Yaml;
internal class TableDialectTypeDiscriminator : ITypeDiscriminator
{
    private static Dictionary<string, Type> GetValueMappings()
        => new()
        {
            { "database", typeof(TableDatabaseDialect)},
            { "delimited", typeof(TableDelimitedDialect)},
            { "spreadsheet", typeof(TableSpreadsheetDialect)}
        };

    public void Execute(ITypeDiscriminatingNodeDeserializerOptions options)
        => options.AddKeyValueTypeDiscriminator<TableDialect>("type", GetValueMappings());
}
