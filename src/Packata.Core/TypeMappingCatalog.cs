using System.Data;

namespace Packata.Core;

// The legacy scalar contract and the rich projections deliberately share this table.
internal static class TypeMappingCatalog
{
    internal sealed record Entry(string LogicalType, string? Format, Type RuntimeType, DbType DatabaseType,
        int? IntegerWidth = null, bool? Signed = null);

    internal static readonly Entry[] Entries =
    [
        new("string", null, typeof(string), DbType.String),
        new("boolean", null, typeof(bool), DbType.Boolean),
        new("number", "fp32", typeof(float), DbType.Single),
        new("number", "fp64", typeof(double), DbType.Double),
        new("number", null, typeof(decimal), DbType.Decimal),
        new("integer", "i8", typeof(sbyte), DbType.SByte, 8, true),
        new("integer", "u8", typeof(byte), DbType.Byte, 8, false),
        new("integer", "i16", typeof(short), DbType.Int16, 16, true),
        new("integer", "u16", typeof(ushort), DbType.UInt16, 16, false),
        new("integer", "i32", typeof(int), DbType.Int32, 32, true),
        new("integer", "u32", typeof(uint), DbType.UInt32, 32, false),
        new("integer", "i64", typeof(long), DbType.Int64, 64, true),
        new("integer", "u64", typeof(ulong), DbType.UInt64, 64, false),
        new("integer", null, typeof(int), DbType.Int32),
        new("date", null, typeof(DateOnly), DbType.Date),
        new("time", null, typeof(TimeOnly), DbType.Time),
        new("datetime", null, typeof(DateTime), DbType.DateTime),
        new("timestamp", null, typeof(DateTimeOffset), DbType.DateTimeOffset),
        new("year", null, typeof(int), DbType.Int32),
        new("yearmonth", null, typeof(string), DbType.String),
        new("object", null, typeof(object), DbType.Object)
    ];

    internal static bool HasOffset(string type, string? format)
        => type == "datetime" && DateTimeFormatConverter.HasOffset(format);

    // Map retains its historical fallback for newly understood formats. Resolve is loss-aware.
    internal static bool IsLegacyEntry(Entry entry)
        => entry.Format is not ("i8" or "u8" or "u16" or "u32" or "u64");
}
