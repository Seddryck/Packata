using Parquet;
using Parquet.Data;
using Parquet.Schema;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

public class ParquetDataReader : System.Data.IDataReader
{
    private readonly ParquetReader _reader;
    private readonly DataField[] _dataFields;
    private readonly List<object[]> _rows = [];
    private int _currentRowIndex = -1;

    private ParquetDataReader(ParquetReader reader)
    {
        _reader = reader;
        _dataFields = _reader.Schema.GetDataFields();
    }

    public static async Task<ParquetDataReader> CreateAsync(Stream stream)
    {
        var reader = await ParquetReader.CreateAsync(stream);
        var dataReader = new ParquetDataReader(reader);
        await dataReader.LoadRowsAsync();
        return dataReader;
    }

    private async Task LoadRowsAsync()
    {
        for (int i = 0; i < _reader.RowGroupCount; i++)
        {
            using var groupReader = _reader.OpenRowGroupReader(i);
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

    public object GetValue(int i) => _rows[_currentRowIndex][i];

    public string GetName(int i) => _dataFields[i].Name;
    public string GetDataTypeName(int i) => _dataFields[i].SchemaType.ToString();
    public Type GetFieldType(int i) => _dataFields[i].ClrType;
    public int GetOrdinal(string name)
    {
        for (int i = 0; i < _dataFields.Length; i++)
        {
            if (_dataFields[i].Name == name) return i;
        }
        return -1;
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
    public bool IsClosed => false;
    public int RecordsAffected => -1;
    public void Close() => _reader.Dispose();
    public System.Data.DataTable GetSchemaTable() => throw new NotSupportedException();
    public bool NextResult() => false;

    public void Dispose()
        => _reader?.Dispose();

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
    public int GetValues(object[] values) => throw new NotImplementedException();
}
