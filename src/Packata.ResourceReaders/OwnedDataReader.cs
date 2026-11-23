using System.Data;
using System.Text.Json;
using Packata.Core.Reading;

namespace Packata.ResourceReaders;

internal sealed class OwnedDataReader : IDataReader, IOriginalColumnNames
{
    private readonly IDataReader _inner;
    private readonly IDisposable[] _owned;
    private readonly ReaderBindingPlan? _plan;
    private Dictionary<int, object>? _converted;
    private bool _disposed;

    internal OwnedDataReader(IDataReader inner, params IDisposable[] owned)
        : this(inner, owned, null)
    { }

    internal OwnedDataReader(IDataReader inner, IDisposable[] owned, ReaderBindingPlan? plan)
    {
        _inner = inner;
        _owned = owned;
        _plan = plan;
    }

    public IReadOnlyList<string>? OriginalColumnNames
    {
        get
        {
            if (_inner is IOriginalColumnNames metadata) return metadata.OriginalColumnNames;
            if (_inner is PocketCsvReader.CsvDataReader { Fields: { } fields }) return Array.AsReadOnly(fields.ToArray());
            return null;
        }
    }

    public object this[int i] => Bound(i) is null ? _inner[i] : GetValue(i);
    public object this[string name] => _plan?.HasBindings == true ? this[GetOrdinal(name)] : _inner[name];
    public int Depth => _inner.Depth;
    public bool IsClosed => _inner.IsClosed;
    public int RecordsAffected => _inner.RecordsAffected;
    public int FieldCount => _inner.FieldCount;

    public void Close() => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _converted?.Clear();
        ResourceDisposal.Run(new[] { _inner }.Concat(_owned).Select(value => (Action)value.Dispose));
    }

    public bool GetBoolean(int i) => Bound(i) is null ? _inner.GetBoolean(i) : (bool)GetValue(i);
    public byte GetByte(int i) => Bound(i) is null ? _inner.GetByte(i) : (byte)GetValue(i);
    public char GetChar(int i) => Bound(i) is null ? _inner.GetChar(i) : (char)GetValue(i);
    public DateTime GetDateTime(int i) => Bound(i) is null ? _inner.GetDateTime(i) : (DateTime)GetValue(i);
    public decimal GetDecimal(int i) => Bound(i) is null ? _inner.GetDecimal(i) : (decimal)GetValue(i);
    public double GetDouble(int i) => Bound(i) is null ? _inner.GetDouble(i) : (double)GetValue(i);
    public float GetFloat(int i) => Bound(i) is null ? _inner.GetFloat(i) : (float)GetValue(i);
    public Guid GetGuid(int i) => Bound(i) is null ? _inner.GetGuid(i) : (Guid)GetValue(i);
    public short GetInt16(int i) => Bound(i) is null ? _inner.GetInt16(i) : (short)GetValue(i);
    public int GetInt32(int i) => Bound(i) is null ? _inner.GetInt32(i) : (int)GetValue(i);
    public long GetInt64(int i) => Bound(i) is null ? _inner.GetInt64(i) : (long)GetValue(i);
    public string GetString(int i) => Bound(i) is null ? _inner.GetString(i) : (string)GetValue(i);
    public IDataReader GetData(int i) => Bound(i) is null ? _inner.GetData(i) : (IDataReader)GetValue(i);
    public string GetDataTypeName(int i) => Bound(i)?.Converter is null ? _inner.GetDataTypeName(i) : GetFieldType(i).Name;
    public Type GetFieldType(int i) => Bound(i)?.Request.RuntimeType ?? _inner.GetFieldType(i);
    public string GetName(int i) => _inner.GetName(i);
    public int GetOrdinal(string name) => _inner.GetOrdinal(name);

    public long GetBytes(int i, long offset, byte[]? buffer, int bufferOffset, int length)
        => Bound(i) is null ? _inner.GetBytes(i, offset, buffer, bufferOffset, length)
            : Copy<byte>(((byte[])GetValue(i)).AsSpan(), offset, buffer, bufferOffset, length);

    public long GetChars(int i, long offset, char[]? buffer, int bufferOffset, int length)
        => Bound(i) is null ? _inner.GetChars(i, offset, buffer, bufferOffset, length)
            : Copy<char>(((string)GetValue(i)).AsSpan(), offset, buffer, bufferOffset, length);

    public DataTable? GetSchemaTable()
    {
        var table = _inner.GetSchemaTable();
        if (table is null || _plan?.HasBindings != true
            || !table.Columns.Contains("DataType") || !table.Columns.Contains("ColumnOrdinal"))
            return table;

        var copy = table.Copy();
        foreach (DataRow row in copy.Rows)
        {
            if (row["ColumnOrdinal"] is int ordinal && Bound(ordinal) is { } field)
                row["DataType"] = field.Request.RuntimeType;
        }
        return copy;
    }

    public object GetValue(int i)
    {
        if (Bound(i) is not { } field) return _inner.GetValue(i);
        if (field.Converter is null)
        {
            if (field.Request.MissingValues.Count > 0 && (_inner.IsDBNull(i) || MatchesMissingMarker(i, field)))
                return DBNull.Value;

            return CheckValue(_inner.GetValue(i), field.Request.RuntimeType, field.Request.Field.Name);
        }

        if (_converted?.TryGetValue(i, out var cached) == true) return cached;

        var value = ConvertValue(i, field);
        (_converted ??= []).Add(i, value);
        return value;
    }

    public int GetValues(object[] values)
    {
        if (_plan?.HasBindings != true) return _inner.GetValues(values);
        ArgumentNullException.ThrowIfNull(values);
        var count = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < count; i++)
            values[i] = GetValue(i);
        return count;
    }

    public bool IsDBNull(int i)
    {
        if (Bound(i) is not { } field) return _inner.IsDBNull(i);
        return field.Converter is null
            ? _inner.IsDBNull(i) || MatchesMissingMarker(i, field)
            : GetValue(i) is DBNull;
    }

    public bool NextResult()
    {
        _converted?.Clear();
        return _inner.NextResult();
    }

    public bool Read()
    {
        _converted?.Clear();
        return _inner.Read();
    }

    private ReaderBindingPlan.ResolvedField? Bound(int ordinal)
        => _plan is not null && _plan.TryGetField(ordinal, out var field) ? field : null;

    private bool MatchesMissingMarker(int ordinal, ReaderBindingPlan.ResolvedField field)
        => field.Request.MissingValues.Count > 0
        && _plan!.GetUnconvertedText(_inner, ordinal) is { } text
        && field.Request.MissingValues.Contains(text, StringComparer.Ordinal);

    private object ConvertValue(int ordinal, ReaderBindingPlan.ResolvedField field)
    {
        if (_inner.IsDBNull(ordinal) || MatchesMissingMarker(ordinal, field)) return DBNull.Value;
        var raw = _inner.GetValue(ordinal);
        if (raw is null or DBNull || (raw is string text && field.Request.MissingValues.Contains(text, StringComparer.Ordinal)))
            return DBNull.Value;

        var name = field.Request.Field.Name;
        var source = CheckValue(raw, field.Binding.RuntimeType, name);
        var converted = CheckValue(field.Converter!.Convert(source), field.Request.RuntimeType, name);
        return converted switch
        {
            JsonElement json => json.Clone(),
            byte[] bytes => bytes.ToArray(),
            _ => converted
        };
    }

    private static object CheckValue(object? value, Type expected, string field)
    {
        if (value is null or DBNull) return DBNull.Value;
        if (RepresentationType.Accepts(expected, value)) return value;
        throw new InvalidOperationException(
            $"Binding for field '{field}' returned '{value.GetType().FullName}', expected '{expected.FullName}'.");
    }

    private static int Copy<T>(ReadOnlySpan<T> value, long offset, T[]? buffer, int bufferOffset, int length)
    {
        if (buffer is null) return value.Length;
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var start = (int)Math.Min(offset, value.Length);
        var count = Math.Min(length, value.Length - start);
        value.Slice(start, count).CopyTo(buffer.AsSpan(bufferOffset, count));
        return count;
    }
}
