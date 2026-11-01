using System.Collections;
using System.Data;
using System.Reflection;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace Packata.ResourceReaders.Parquet.Tabular;

public class ParquetDataReader : IDataReader
{
    private static readonly MethodInfo ReadValueColumnMethod = typeof(ParquetDataReader)
        .GetMethod(nameof(ReadValueColumnAsync), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo ReadNullableColumnMethod = typeof(ParquetDataReader)
        .GetMethod(nameof(ReadNullableColumnAsync), BindingFlags.NonPublic | BindingFlags.Static)!;
    private readonly List<ParquetReader> _readers = [];
    private readonly List<object[]> _rows = [];
    private int _fieldCount = -1;
    private int _currentRowIndex = -1;
    private DataField[] _dataFields = [];
    private bool _disposed;

    private ParquetDataReader() { }

    public static Task<ParquetDataReader> CreateAsync(IEnumerable<Stream> streams) => CreateAsync(streams, CancellationToken.None);

    public static async Task<ParquetDataReader> CreateAsync(IEnumerable<Stream> streams, CancellationToken cancellationToken)
    {
        var reader = new ParquetDataReader();
        try
        {
            foreach (var stream in streams)
            {
                var parquetReader = await ParquetReader.CreateAsync(stream, leaveStreamOpen: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                reader._readers.Add(parquetReader);
                reader.SetSchema(parquetReader.Schema.GetDataFields());
                await reader.LoadRowsFromReaderAsync(parquetReader, cancellationToken).ConfigureAwait(false);
            }
            return reader;
        }
        catch
        {
            reader.DisposeAfterFailure();
            throw;
        }
    }

    private void SetSchema(DataField[] fields)
    {
        if (_fieldCount == -1) { _fieldCount = fields.Length; _dataFields = fields; }
        else if (!AreSchemasEqual(_dataFields, fields))
        {
            throw new InvalidOperationException("Inconsistent schema across parquet files.");
        }
    }

    private void DisposeAfterFailure()
    {
        try { Dispose(); }
        catch (Exception) { /* Preserve the parser/transfer/cancellation failure. */ }
    }

    private bool AreSchemasEqual(DataField[] a, DataField[] b) =>
        a.Length == b.Length && a.Zip(b).All(pair =>
            pair.First.Name == pair.Second.Name && pair.First.ClrType == pair.Second.ClrType);

    private async Task LoadRowsFromReaderAsync(ParquetReader reader, CancellationToken cancellationToken)
    {
        for (var i = 0; i < reader.RowGroupCount; i++)
        {
            using var groupReader = reader.OpenRowGroupReader(i);
            var columns = new object?[_dataFields.Length][];
            var rowCount = checked((int)groupReader.RowCount);
            for (var j = 0; j < _dataFields.Length; j++)
                columns[j] = await ReadColumnAsync(groupReader, _dataFields[j], rowCount, cancellationToken).ConfigureAwait(false);
            for (var row = 0; row < rowCount; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = new object[_dataFields.Length];
                for (var column = 0; column < _dataFields.Length; column++) values[column] = columns[column][row]!;
                _rows.Add(values);
            }
        }
    }

    private static Task<object?[]> ReadColumnAsync(ParquetRowGroupReader reader, DataField field, int rowCount, CancellationToken cancellationToken)
    {
        if (field.ClrType == typeof(string) || field.ClrType == typeof(ReadOnlyMemory<char>))
            return ReadStringColumnAsync(reader, field, rowCount, cancellationToken);
        if (field.ClrType == typeof(byte[]) || field.ClrType == typeof(ReadOnlyMemory<byte>))
            return ReadByteArrayColumnAsync(reader, field, rowCount, cancellationToken);
        if (!field.ClrType.IsValueType)
            throw new NotSupportedException($"Parquet column type '{field.ClrType}' is not supported.");
        var method = field.IsNullable ? ReadNullableColumnMethod : ReadValueColumnMethod;
        return (Task<object?[]>)method.MakeGenericMethod(field.ClrType).Invoke(null, [reader, field, rowCount, cancellationToken])!;
    }

    private static async Task<object?[]> ReadValueColumnAsync<T>(ParquetRowGroupReader reader, DataField field, int rowCount, CancellationToken cancellationToken)
        where T : struct
    {
        var values = new T[rowCount];
        await reader.ReadAsync(field, values.AsMemory(), cancellationToken: cancellationToken).ConfigureAwait(false);
        return values.Cast<object?>().ToArray();
    }

    private static async Task<object?[]> ReadNullableColumnAsync<T>(ParquetRowGroupReader reader, DataField field, int rowCount, CancellationToken cancellationToken)
        where T : struct
    {
        var values = new T?[rowCount];
        await reader.ReadAsync(field, values.AsMemory(), cancellationToken: cancellationToken).ConfigureAwait(false);
        return values.Cast<object?>().ToArray();
    }

    private static async Task<object?[]> ReadStringColumnAsync(ParquetRowGroupReader reader, DataField field, int rowCount, CancellationToken cancellationToken)
    {
        var values = new string?[rowCount];
        await reader.ReadAsync(field, values.AsMemory(), cancellationToken: cancellationToken).ConfigureAwait(false);
        return values.Cast<object?>().ToArray();
    }

    private static async Task<object?[]> ReadByteArrayColumnAsync(ParquetRowGroupReader reader, DataField field, int rowCount, CancellationToken cancellationToken)
    {
        var values = new byte[]?[rowCount];
        await reader.ReadAsync(field, values.AsMemory(), cancellationToken: cancellationToken).ConfigureAwait(false);
        return values.Cast<object?>().ToArray();
    }

    public bool Read() => ++_currentRowIndex < _rows.Count;
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
        for (var i = 0; i < _dataFields.Length; i++) if (_dataFields[i].Name == name) return i;
        throw new ArgumentException($"Column name '{name}' not found", nameof(name));
    }
    public bool IsDBNull(int i) => GetValue(i) is null;
    public int GetInt32(int i) => (int)GetValue(i);
    public long GetInt64(int i) => (long)GetValue(i);
    public string GetString(int i) => (string)GetValue(i);
    public bool GetBoolean(int i) => (bool)GetValue(i);
    public DateTime GetDateTime(int i) => (DateTime)GetValue(i);
    public object this[int i] => GetValue(i);
    public object this[string name] => GetValue(GetOrdinal(name));
    public int Depth => 0;
    public bool IsClosed => _disposed;
    public int RecordsAffected => -1;
    public void Close() => Dispose();
    public DataTable? GetSchemaTable() => throw new NotSupportedException();
    public bool NextResult() => false;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ResourceDisposal.Run(_readers.Select(reader => (Action)(() => reader.DisposeAsync().AsTask().GetAwaiter().GetResult())));
        GC.SuppressFinalize(this);
    }
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public long GetChars(int i, long fieldOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public char GetChar(int i) => (char)GetValue(i);
    public Guid GetGuid(int i) => (Guid)GetValue(i);
    public short GetInt16(int i) => (short)GetValue(i);
    public float GetFloat(int i) => (float)GetValue(i);
    public double GetDouble(int i) => (double)GetValue(i);
    public decimal GetDecimal(int i) => (decimal)GetValue(i);
    public IDataReader GetData(int i) => throw new NotSupportedException();
    public byte GetByte(int i) => throw new NotSupportedException();
    public int GetValues(object[] values)
    {
        if (_currentRowIndex < 0 || _currentRowIndex >= _rows.Count) return 0;
        var length = Math.Min(values.Length, _rows[_currentRowIndex].Length);
        Array.Copy(_rows[_currentRowIndex], values, length);
        return length;
    }
}
