using Parquet;
using Parquet.Data;
using Parquet.Schema;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Packata.ResourceReaders.Tabular;
public class ParquetDataReader : System.Data.IDataReader
{
    private static readonly MethodInfo ReadValueColumnMethod = typeof(ParquetDataReader)
        .GetMethod(nameof(ReadValueColumnAsync), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo ReadNullableColumnMethod = typeof(ParquetDataReader)
        .GetMethod(nameof(ReadNullableColumnAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly List<Parquet.ParquetReader> _readers = new();
    private readonly List<Stream> _streams = new(); // to dispose later
    private readonly List<DataField[]> _schemas = new();
    private readonly List<object[]> _rows = new();

    private int _fieldCount = -1;
    private int _currentRowIndex = -1;
    private DataField[] _dataFields = [];

    private ParquetDataReader() { }

    public static async Task<ParquetDataReader> CreateAsync(IEnumerable<Stream> streams)
    {
        var reader = new ParquetDataReader();
        foreach (var stream in streams)
        {
            var parquetReader = await Parquet.ParquetReader.CreateAsync(stream);
            var dataFields = parquetReader.Schema.GetDataFields();

            if (reader._fieldCount == -1)
            {
                reader._fieldCount = dataFields.Length;
                reader._dataFields = dataFields;
            }
            else
            {
                // Ensure schemas are consistent across files
                if (!reader.AreSchemasEqual(reader._dataFields, dataFields))
                    throw new InvalidOperationException("Inconsistent schema across parquet files.");
            }

            reader._streams.Add(stream);
            reader._readers.Add(parquetReader);
            await reader.LoadRowsFromReaderAsync(parquetReader);
        }

        return reader;
    }

    private bool AreSchemasEqual(DataField[] a, DataField[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].Name != b[i].Name || a[i].ClrType != b[i].ClrType)
                return false;
        }
        return true;
    }

    private async Task LoadRowsFromReaderAsync(Parquet.ParquetReader reader)
    {
        for (int i = 0; i < reader.RowGroupCount; i++)
        {
            using var groupReader = reader.OpenRowGroupReader(i);
            var columns = new object?[_dataFields.Length][];
            var rowCount = checked((int)groupReader.RowCount);

            for (int j = 0; j < _dataFields.Length; j++)
            {
                columns[j] = await ReadColumnAsync(groupReader, _dataFields[j], rowCount);
            }

            for (int row = 0; row < rowCount; row++)
            {
                var values = new object[_dataFields.Length];
                for (int col = 0; col < _dataFields.Length; col++)
                {
                    values[col] = columns[col][row]!;
                }
                _rows.Add(values);
            }
        }
    }

    private static Task<object?[]> ReadColumnAsync(ParquetRowGroupReader reader, DataField field, int rowCount)
    {
        if (field.ClrType == typeof(string) || field.ClrType == typeof(ReadOnlyMemory<char>))
            return ReadStringColumnAsync(reader, field, rowCount);
        if (field.ClrType == typeof(byte[]) || field.ClrType == typeof(ReadOnlyMemory<byte>))
            return ReadByteArrayColumnAsync(reader, field, rowCount);
        if (!field.ClrType.IsValueType)
            throw new NotSupportedException($"Parquet column type '{field.ClrType}' is not supported.");

        var method = field.IsNullable && field.ClrType.IsValueType
            ? ReadNullableColumnMethod
            : ReadValueColumnMethod;
        return (Task<object?[]>)method.MakeGenericMethod(field.ClrType)
            .Invoke(null, [reader, field, rowCount])!;
    }

    private static async Task<object?[]> ReadValueColumnAsync<T>(ParquetRowGroupReader reader, DataField field, int rowCount)
        where T : struct
    {
        var values = new T[rowCount];
        await reader.ReadAsync(field, values.AsMemory());
        return values.Cast<object?>().ToArray();
    }

    private static async Task<object?[]> ReadNullableColumnAsync<T>(ParquetRowGroupReader reader, DataField field, int rowCount)
        where T : struct
    {
        var values = new T?[rowCount];
        await reader.ReadAsync(field, values.AsMemory());
        return values.Cast<object?>().ToArray();
    }

    private static async Task<object?[]> ReadStringColumnAsync(ParquetRowGroupReader reader, DataField field, int rowCount)
    {
        var values = new string?[rowCount];
        await reader.ReadAsync(field, values.AsMemory());
        return values.Cast<object?>().ToArray();
    }

    private static async Task<object?[]> ReadByteArrayColumnAsync(ParquetRowGroupReader reader, DataField field, int rowCount)
    {
        var values = new byte[]?[rowCount];
        await reader.ReadAsync(field, values.AsMemory());
        return values.Cast<object?>().ToArray();
    }

    public bool Read()
    {
        _currentRowIndex++;
        return _currentRowIndex < _rows.Count;
    }

    public int FieldCount => _dataFields.Length;

    public object GetValue(int i) => _rows[_currentRowIndex][i];

    public string GetName(int i) => _dataFields[i].Name;
    public string GetDataTypeName(int i) => _dataFields[i].SchemaType.ToString();
    public Type GetFieldType(int i) => _dataFields[i].ClrType switch
    {
        var type when type == typeof(ReadOnlyMemory<char>) => typeof(string),
        var type when type == typeof(ReadOnlyMemory<byte>) => typeof(byte[]),
        var type => type
    };
    public int GetOrdinal(string name)
    {
        for (int i = 0; i < _dataFields.Length; i++)
            if (_dataFields[i].Name == name)
                return i;
        throw new ArgumentException($"Column name '{name}' not found", nameof(name));
    }

    public bool IsDBNull(int i) => GetValue(i) == null;

    public int GetInt32(int i) => (int)GetValue(i);
    public long GetInt64(int i) => (long)GetValue(i);
    public string GetString(int i) => (string)GetValue(i);
    public bool GetBoolean(int i) => (bool)GetValue(i);
    public DateTime GetDateTime(int i) => (DateTime)GetValue(i);

    // Not implemented or rarely used
    public object this[int i] => GetValue(i);
    public object this[string name] => GetValue(GetOrdinal(name));
    public int Depth => 0;
    public bool IsClosed => _disposed;
    public int RecordsAffected => -1;
    public void Close() => Dispose();
    public System.Data.DataTable GetSchemaTable() => throw new NotSupportedException();
    public bool NextResult() => false;

    private bool _disposed = false;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            foreach (var reader in _readers)
                reader.DisposeAsync().AsTask().GetAwaiter().GetResult();
            foreach (var stream in _streams)
                stream.Dispose();
        }
        _disposed = true;
    }

    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();
    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();
    public char GetChar(int i) => (char)GetValue(i);
    public Guid GetGuid(int i) => (Guid)GetValue(i);
    public short GetInt16(int i) => (short)GetValue(i);
    public float GetFloat(int i) => (float)GetValue(i);
    public double GetDouble(int i) => (double)GetValue(i);
    public decimal GetDecimal(int i) => (decimal)GetValue(i);
    public string GetDataTypeName(string columnName) => GetDataTypeName(GetOrdinal(columnName));
    public System.Data.IDataReader GetData(int i) => throw new NotSupportedException();
    System.Data.DataTable? System.Data.IDataReader.GetSchemaTable() => throw new NotImplementedException();
    public byte GetByte(int i) => throw new NotImplementedException();
    System.Data.IDataReader System.Data.IDataRecord.GetData(int i) => throw new NotImplementedException();
    public int GetValues(object[] values)
    {
        if (_currentRowIndex< 0 || _currentRowIndex >= _rows.Count)
            return 0;
        var current = _rows[_currentRowIndex];
        var len = Math.Min(values.Length, current.Length);
        Array.Copy(current, values, len);
        return len;
    }
}
