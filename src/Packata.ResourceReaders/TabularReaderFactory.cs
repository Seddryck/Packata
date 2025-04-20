using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;
using Packata.Core.ResourceReading;
using Packata.ResourceReaders.Tabular;

namespace Packata.ResourceReaders;
internal class TabularReaderFactory : IResourceReaderFactory
{
    public const string Delimited = "delimited";
    public const string Structured = "structured";
    public const string Spreadsheet = "spreadsheet";
    public const string Parquet = "parquet";
    public const string Database = "database";

    private Func<Resource, string> Heuristic { get; set; }

    private Dictionary<string, IResourceReaderBuilder> Readers { get; } = [];

    public void AddOrReplaceReader(string type, IResourceReaderBuilder builder)
    {
        if (!Readers.TryAdd(type, builder))
            Readers[type] = builder;
    }

    public void SetHeuristic(Func<Resource, string> heuristic)
        => Heuristic = heuristic;

    public TabularReaderFactory()
    {
        AddOrReplaceReader(Delimited, new DelimitedReaderBuilder());
        AddOrReplaceReader(Database, new DatabaseReaderBuilder());
        AddOrReplaceReader(Spreadsheet, new SpreadsheetReaderBuilder());
        AddOrReplaceReader(Parquet, new ParquetReaderBuilder());
        Heuristic = TabularHeuristic;
    }

    public IResourceReader Create(Resource resource)
    {
        if (!(resource.Type ?? "table").Equals("table", StringComparison.InvariantCultureIgnoreCase))
            throw new InvalidOperationException();

        if (!Readers.TryGetValue(Heuristic(resource), out var builder))
            throw new ArgumentException("Can't determine the appropriate reader for this resource", nameof(resource));

        builder.Configure(resource);
        return builder.Build();
    }

    private static string TabularHeuristic(Resource resource)
    {
        if (resource.Dialect?.Type is not null)
            return resource.Dialect.Type;

        if (resource.Format is not null)
        {
            var format = resource.Format.ToLowerInvariant();
            if (format.EndsWith(".gz"))
                format = format[0..^3];

            return (format) switch
            {
                "csv" or "tsv" or "psv" or "txt" => Delimited,
                "xlsx" or "xls" => Spreadsheet,
                "parquet" or "pqt" => Parquet,
                _ => Delimited,
            };
        }
        return Delimited;
    }
}
