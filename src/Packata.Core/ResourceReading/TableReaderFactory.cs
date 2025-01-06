using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.ResourceReading;
internal class TableReaderFactory : IResourceReaderFactory
{
    public const string Delimited = "delimited";
    public const string Structured = "structured";
    public const string Spreadsheet = "spreadsheet";
    public const string Database = "database";

    private Func<Resource, string> Heuristic { get; set; }

    private Dictionary<string, IResourceReaderBuilder> Readers { get; } = new();

    public void AddOrReplaceReader(string type, IResourceReaderBuilder builder)
    {
        if (!Readers.TryAdd(type, builder))
            Readers[type] = builder;
    }

    public void SetHeuristic(Func<Resource, string> heuristic)
        => Heuristic = heuristic;

    public TableReaderFactory()
    {
        AddOrReplaceReader(Delimited, new TableDelimitedReaderBuilder());
        Heuristic = resource => resource.Dialect?.Type ?? Delimited;
    }

    public IResourceReader Create(Resource resource)
    {
        if ((resource.Type ?? "table").ToLowerInvariant() != "table")
            throw new InvalidOperationException();

        if (!Readers.TryGetValue(Heuristic(resource), out var builder))
            throw new ArgumentException();

        builder.Configure(resource);
        return builder.Build();
    }
}
