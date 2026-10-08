using Packata.Core.Contracts;

namespace Packata.ResourceReaders;

internal static class KnownFormatRegistry
{
    private static readonly KnownFormat[] Formats =
    [
        new("xlsx", ["xlsx", "xls"],
            ["application/vnd.ms-excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
            "Packata.ResourceReaders.Excel", "AddExcel()"),
        new("parquet", ["parquet", "pqt"], ["application/vnd.apache.parquet"],
            "Packata.ResourceReaders.Parquet", "AddParquet()"),
        new("database", ["database", "db"], [],
            "Packata.ResourceReaders.Database", "AddDatabase()"),
        new("ndjson", ["ndjson", "jsonl"], ["application/x-ndjson", "application/ndjson"],
            "Packata.ResourceReaders.Ndjson", "AddNdjson()")
    ];

    public static KnownFormat? Find(DataEndpoint endpoint, ResolvedDataFormat resolved)
    {
        if (endpoint.Location is ConnectionLocation)
            return Formats.Single(format => format.Name == "database");

        return Formats.FirstOrDefault(format =>
            format.Aliases.Contains(resolved.Name, StringComparer.OrdinalIgnoreCase) ||
            resolved.MediaType is not null &&
            format.MediaTypes.Contains(resolved.MediaType, StringComparer.OrdinalIgnoreCase));
    }
}
