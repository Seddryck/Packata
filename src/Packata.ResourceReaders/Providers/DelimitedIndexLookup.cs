using Packata.Core;
using Packata.Core.Contracts;
using PocketCsvReader;
using PocketCsvReader.Indexing;

namespace Packata.ResourceReaders.Providers;

internal static class DelimitedIndexLookup
{
    public static CsvFileQuery<string> Apply(CsvFileQuery<string> query, ResolvedIndexedEquality equality, bool hasHeader)
    {
        var field = equality.Field;
        var type = new RuntimeTypeMapper().Resolve(field, TypeMappingOptions.LegacyCompatibility).RuntimeType;
        if (equality.Value.GetType() != type)
            throw new ArgumentException($"Indexed lookup on '{field.Name}' requires a value of type '{type?.Name}'.");
        if (equality.Index.Comparison is not null && type != typeof(string))
            throw new NotSupportedException($"String comparison is unsupported for indexed property '{field.Name}'.");

        return equality.Value switch
        {
            string value => Add(value),
            int value => Add(value),
            long value => Add(value),
            short value => Add(value),
            byte value => Add(value),
            sbyte value => Add(value),
            ushort value => Add(value),
            uint value => Add(value),
            ulong value => Add(value),
            decimal value => Add(value),
            DateOnly value => Add(value),
            _ => throw new NotSupportedException($"CSV indexing does not support the key type '{type?.Name}'.")
        };

        CsvFileQuery<string> Add<TKey>(TKey value)
            where TKey : ISpanParsable<TKey>
        {
            var comparison = equality.Index.Comparison switch
            {
                null => (StringComparison?)null,
                DataIndexComparison.Ordinal => StringComparison.Ordinal,
                DataIndexComparison.OrdinalIgnoreCase => StringComparison.OrdinalIgnoreCase,
                _ => throw new NotSupportedException($"Unsupported index comparison '{equality.Index.Comparison}'.")
            };
            var pattern = typeof(TKey) == typeof(DateOnly) ? PocketCsvReaderProviderDefaults.ResolvePattern(field) : null;
            var format = pattern is null or "%Y-%m-%d" ? null : DateTimeFormatConverter.Convert(pattern);
            var key = hasHeader
                ? new IndexKey<TKey>(field.PhysicalName ?? field.Name, format, comparison: comparison)
                : new IndexKey<TKey>(equality.Ordinal, format, comparison: comparison);

            return equality.Index.Kind switch
            {
                DataIndexKind.Bloom => query.WithIndexedLookup(new BloomIndex(key), value),
                DataIndexKind.Ordered when equality.Index.Order is null or DataIndexOrder.Ascending =>
                    query.WithIndexedLookup(new SparseIndex(key, checkpointInterval: 1024), value),
                _ => throw new NotSupportedException($"CSV indexing does not support '{equality.Index.Kind}' with order '{equality.Index.Order}'.")
            };
        }
    }
}
