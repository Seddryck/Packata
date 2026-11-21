using Packata.Core;
using Packata.Core.Contracts;
using PocketCsvReader;
using PocketCsvReader.Indexing;

namespace Packata.ResourceReaders.Providers;

internal static class DelimitedIndexLookup
{
    public static CsvFileQuery<string> Apply(CsvFileQuery<string> query, ResolvedIndexedEquality equality, bool hasHeader)
    {
        ValidateKey(equality);
        return equality.Value switch
        {
            string value => ApplyKey(query, equality, hasHeader, value),
            int value => ApplyKey(query, equality, hasHeader, value),
            long value => ApplyKey(query, equality, hasHeader, value),
            short value => ApplyKey(query, equality, hasHeader, value),
            byte value => ApplyKey(query, equality, hasHeader, value),
            sbyte value => ApplyKey(query, equality, hasHeader, value),
            ushort value => ApplyKey(query, equality, hasHeader, value),
            uint value => ApplyKey(query, equality, hasHeader, value),
            ulong value => ApplyKey(query, equality, hasHeader, value),
            decimal value => ApplyKey(query, equality, hasHeader, value),
            DateOnly value => ApplyKey(query, equality, hasHeader, value),
            _ => throw new NotSupportedException($"CSV indexing does not support the key type '{equality.Value.GetType().Name}'.")
        };
    }

    private static void ValidateKey(ResolvedIndexedEquality equality)
    {
        var field = equality.Field;
        var type = new RuntimeTypeMapper().Resolve(field, TypeMappingOptions.LegacyCompatibility).RuntimeType;
        if (equality.Value.GetType() != type)
            throw new ArgumentException($"Indexed lookup on '{field.Name}' requires a value of type '{type?.Name}'.");
        if (equality.Index.Comparison is not null && type != typeof(string))
            throw new NotSupportedException($"String comparison is unsupported for indexed property '{field.Name}'.");
    }

    private static CsvFileQuery<string> ApplyKey<TKey>(CsvFileQuery<string> query, ResolvedIndexedEquality equality,
        bool hasHeader, TKey value)
        where TKey : ISpanParsable<TKey>
    {
        var key = CreateKey<TKey>(equality, hasHeader);
        return equality.Index.Kind switch
        {
            DataIndexKind.Bloom => query.WithIndexedLookup(new BloomIndex(key), value),
            DataIndexKind.Ordered when equality.Index.Order is null or DataIndexOrder.Ascending =>
                query.WithIndexedLookup(new SparseIndex(key, checkpointInterval: 1024), value),
            _ => throw new NotSupportedException($"CSV indexing does not support '{equality.Index.Kind}' with order '{equality.Index.Order}'.")
        };
    }

    private static IndexKey<TKey> CreateKey<TKey>(ResolvedIndexedEquality equality, bool hasHeader)
        where TKey : ISpanParsable<TKey>
    {
        var field = equality.Field;
        var comparison = equality.Index.Comparison switch
        {
            null => (StringComparison?)null,
            DataIndexComparison.Ordinal => StringComparison.Ordinal,
            DataIndexComparison.OrdinalIgnoreCase => StringComparison.OrdinalIgnoreCase,
            _ => throw new NotSupportedException($"Unsupported index comparison '{equality.Index.Comparison}'.")
        };
        var pattern = typeof(TKey) == typeof(DateOnly) ? PocketCsvReaderProviderDefaults.ResolvePattern(field) : null;
        var format = pattern is null or "%Y-%m-%d" ? null : DateTimeFormatConverter.Convert(pattern);
        return hasHeader
            ? new IndexKey<TKey>(field.PhysicalName ?? field.Name, format, comparison: comparison)
            : new IndexKey<TKey>(equality.Ordinal, format, comparison: comparison);
    }
}
