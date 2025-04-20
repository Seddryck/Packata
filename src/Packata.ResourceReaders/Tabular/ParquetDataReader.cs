using Parquet;
using Parquet.Data;
using Parquet.Schema;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Packata.ResourceReaders.Tabular;
public class ParquetDataReader : System.Data.IDataReader
{
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
            var columns = new DataColumn[_dataFields.Length];

            for (int j = 0; j < _dataFields.Length; j++)
            {
                columns[j] = await groupReader.ReadColumnAsync(_dataFields[j]);
            }

            int rowCount = columns[0].Data.Length;
            for (int row = 0; row < rowCount; row++)
            {
                var values = new object[_dataFields.Length];
                for (int col = 0; col < _dataFields.Length; col++)
                {
                    values[col] = columns[col].Data.GetValue(row)!;
                }
                _rows.Add(values);
            }
        }
    }

    public bool Read()
    {
        _currentRowIndex++;
        return _currentRowIndex < _rows.Count;
    }

    public int FieldCount => _dataFields.Length;

    public int GetOrdinal(string name)
    {
        for (int i = 0; i < _dataFields.Length; i++)
        {
            if (_dataFields[i].Name == name) return i;
        }
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
                reader.Dispose();
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
