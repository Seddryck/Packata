using System.Data;

namespace Packata.ResourceReaders;

internal sealed class OwnedDataReader(IDataReader inner, params IDisposable[] owned) : IDataReader
{
    public object this[int i] => inner[i];
    public object this[string name] => inner[name];
    public int Depth => inner.Depth;
    public bool IsClosed => inner.IsClosed;
    public int RecordsAffected => inner.RecordsAffected;
    public int FieldCount => inner.FieldCount;
    public void Close() => Dispose();
    public void Dispose() { inner.Dispose(); foreach (var value in owned) value.Dispose(); }
    public bool GetBoolean(int i) => inner.GetBoolean(i);
    public byte GetByte(int i) => inner.GetByte(i);
    public long GetBytes(int i, long o, byte[]? b, int bo, int l) => inner.GetBytes(i, o, b, bo, l);
    public char GetChar(int i) => inner.GetChar(i);
    public long GetChars(int i, long o, char[]? b, int bo, int l) => inner.GetChars(i, o, b, bo, l);
    public IDataReader GetData(int i) => inner.GetData(i);
    public string GetDataTypeName(int i) => inner.GetDataTypeName(i);
    public DateTime GetDateTime(int i) => inner.GetDateTime(i);
    public decimal GetDecimal(int i) => inner.GetDecimal(i);
    public double GetDouble(int i) => inner.GetDouble(i);
    public Type GetFieldType(int i) => inner.GetFieldType(i);
    public float GetFloat(int i) => inner.GetFloat(i);
    public Guid GetGuid(int i) => inner.GetGuid(i);
    public short GetInt16(int i) => inner.GetInt16(i);
    public int GetInt32(int i) => inner.GetInt32(i);
    public long GetInt64(int i) => inner.GetInt64(i);
    public string GetName(int i) => inner.GetName(i);
    public int GetOrdinal(string name) => inner.GetOrdinal(name);
    public DataTable? GetSchemaTable() => inner.GetSchemaTable();
    public string GetString(int i) => inner.GetString(i);
    public object GetValue(int i) => inner.GetValue(i);
    public int GetValues(object[] values) => inner.GetValues(values);
    public bool IsDBNull(int i) => inner.IsDBNull(i);
    public bool NextResult() => inner.NextResult();
    public bool Read() => inner.Read();
}
