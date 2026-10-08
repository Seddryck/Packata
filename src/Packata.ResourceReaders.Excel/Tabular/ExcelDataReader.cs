using System.Data;
using System.Diagnostics.CodeAnalysis;
using ExcelDataReader;

namespace Packata.ResourceReaders.Excel.Tabular;

internal sealed class ExcelDataReader(IExcelDataReader reader, string[] fieldNames) : IDataReader
{
    private bool _isClosed;
    public int Depth => 0;
    public bool IsClosed => _isClosed;
    public int RecordsAffected => -1;
    public int FieldCount => reader.FieldCount;
    public object this[string name] => reader[GetOrdinal(name)];
    public object this[int i] => reader[i];
    public void Close() { reader.Close(); _isClosed = true; }
    public DataTable? GetSchemaTable() => throw new NotImplementedException();
    public bool NextResult() => reader.NextResult();
    public bool Read() => reader.Read();
    public void Dispose() => reader.Dispose();
    public bool GetBoolean(int i) => reader.GetBoolean(i);
    public byte GetByte(int i) => throw new NotSupportedException("Byte access is not supported by ExcelDataReader.");
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferOffset, int length) =>
        throw new NotSupportedException("Byte array access is not supported by ExcelDataReader.");
    public char GetChar(int i) => throw new NotSupportedException("Char access is not supported by ExcelDataReader.");
    public long GetChars(int i, long fieldOffset, char[]? buffer, int bufferOffset, int length) =>
        throw new NotSupportedException("Char array access is not supported by ExcelDataReader.");
    public IDataReader GetData(int i) => reader.GetData(i);
    public string GetDataTypeName(int i) => reader.GetDataTypeName(i);
    public DateTime GetDateTime(int i) => reader.GetDateTime(i);
    public decimal GetDecimal(int i) => reader.GetDecimal(i);
    public double GetDouble(int i) => reader.GetDouble(i);
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
    public Type GetFieldType(int i) => reader.GetFieldType(i);
    public float GetFloat(int i) => reader.GetFloat(i);
    public Guid GetGuid(int i) => reader.GetGuid(i);
    public short GetInt16(int i) => reader.GetInt16(i);
    public int GetInt32(int i) => reader.GetInt32(i);
    public long GetInt64(int i) => reader.GetInt64(i);
    public string GetName(int i) => fieldNames[i];
    public int GetOrdinal(string name)
    {
        var index = Array.IndexOf(fieldNames, name);
        return index >= 0 ? index : throw new IndexOutOfRangeException($"Field '{name}' not found.");
    }
    public string GetString(int i) => reader.GetString(i);
    public object GetValue(int i) => reader.GetValue(i);
    public int GetValues(object[] values) => reader.GetValues(values);
    public bool IsDBNull(int i) => reader.IsDBNull(i);
}
