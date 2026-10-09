using System.Data;

namespace Packata.ResourceReaders.Database;

internal sealed class OwnedDatabaseReader(IDataReader inner, params IDisposable[] owned) : IDataReader
{
    private bool _disposed;
    public object this[int i] => inner[i];
    public object this[string name] => inner[name];
    public int Depth => inner.Depth;
    public bool IsClosed => inner.IsClosed;
    public int RecordsAffected => inner.RecordsAffected;
    public int FieldCount => inner.FieldCount;
    public void Close() => inner.Close();
    public DataTable? GetSchemaTable() => inner.GetSchemaTable();
    public bool NextResult() => inner.NextResult();
    public bool Read() => inner.Read();
    public bool GetBoolean(int i) => inner.GetBoolean(i);
    public byte GetByte(int i) => inner.GetByte(i);
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferOffset, int length) => inner.GetBytes(i, fieldOffset, buffer, bufferOffset, length);
    public char GetChar(int i) => inner.GetChar(i);
    public long GetChars(int i, long fieldOffset, char[]? buffer, int bufferOffset, int length) => inner.GetChars(i, fieldOffset, buffer, bufferOffset, length);
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
    public string GetString(int i) => inner.GetString(i);
    public object GetValue(int i) => inner.GetValue(i);
    public int GetValues(object[] values) => inner.GetValues(values);
    public bool IsDBNull(int i) => inner.IsDBNull(i);
    public void Dispose()
    {
        if (_disposed) return;
        inner.Dispose();
        foreach (var disposable in owned) disposable.Dispose();
        _disposed = true;
    }
}
