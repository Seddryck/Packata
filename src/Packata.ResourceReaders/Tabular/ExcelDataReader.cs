using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ExcelDataReader;

namespace Packata.ResourceReaders.Tabular;
internal class ExcelDataReader : IDataReader
{
    private readonly IExcelDataReader _reader;
    private readonly string[] _fieldNames;
    private bool _isClosed = false;

    public ExcelDataReader(IExcelDataReader reader, string[] fieldNames)
    {
        _reader = reader;
        _fieldNames = fieldNames;
    }

    public int Depth => 0;

    public bool IsClosed => _isClosed;

    public int RecordsAffected => -1;

    public int FieldCount
        => _reader.FieldCount;

    public object this[string name]
        => _reader[GetOrdinal(name)];

    public object this[int i]
        => _reader[i];

    public void Close()
    {
        _reader.Close();
        _isClosed = true;
    }

    public DataTable? GetSchemaTable()
        => throw new NotImplementedException();

    public bool NextResult()
        => _reader.NextResult();

    public bool Read()
        => _reader.Read();

    public void Dispose()
        => _reader.Dispose();

    public bool GetBoolean(int i)
        => _reader.GetBoolean(i);
    public byte GetByte(int i) => throw new NotImplementedException();
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
    public char GetChar(int i) => throw new NotImplementedException();
    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
    public IDataReader GetData(int i)
        => _reader.GetData(i);
    public string GetDataTypeName(int i)
        => _reader.GetDataTypeName(i);
    public DateTime GetDateTime(int i)
        => _reader.GetDateTime(i);
    public decimal GetDecimal(int i)
        => _reader.GetDecimal(i);
    public double GetDouble(int i)
        => _reader.GetDouble(i);
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
    public Type GetFieldType(int i)
        => _reader.GetFieldType(i);
    public float GetFloat(int i)
        => _reader.GetFloat(i);
    public Guid GetGuid(int i)
        => _reader.GetGuid(i);
    public short GetInt16(int i)
        => _reader.GetInt16(i);
    public int GetInt32(int i)
        => _reader.GetInt32(i);
    public long GetInt64(int i)
        => _reader.GetInt64(i);
    public string GetName(int i)
        => _fieldNames[i];
    public int GetOrdinal(string name)
    {
        var index = Array.IndexOf(_fieldNames, name);
        if (index < 0)
            throw new IndexOutOfRangeException($"Field '{name}' not found.");
        return index;
    }

    public string GetString(int i)
        => _reader.GetString(i);
    public object GetValue(int i)
        => _reader.GetValue(i);
    public int GetValues(object[] values)
        => _reader.GetValues(values);
    public bool IsDBNull(int i)
        => _reader.IsDBNull(i);
}

