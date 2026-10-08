using System.Data;

namespace Packata.ResourceReaders;

internal sealed class SequentialDataReader(IEnumerable<IDataReader> readers) : IDataReader
{
    private readonly IDataReader[] _readers = readers.ToArray();
    private int _index;
    private IDataReader Current => _readers[_index];
    public object this[int i] => Current[i];
    public object this[string name] => Current[name];
    public int Depth => Current.Depth;
    public bool IsClosed => _readers.All(reader => reader.IsClosed);
    public int RecordsAffected => -1;
    public int FieldCount => Current.FieldCount;
    public void Close() { foreach (var reader in _readers) reader.Close(); }
    public DataTable? GetSchemaTable() => Current.GetSchemaTable();
    public bool NextResult() => false;
    public bool Read()
    {
        while (_index < _readers.Length)
        {
            if (Current.Read()) return true;
            if (++_index == _readers.Length) return false;
        }
        return false;
    }
    public void Dispose() { foreach (var reader in _readers) reader.Dispose(); }
    public bool GetBoolean(int i) => Current.GetBoolean(i);
    public byte GetByte(int i) => Current.GetByte(i);
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferOffset, int length) => Current.GetBytes(i, fieldOffset, buffer, bufferOffset, length);
    public char GetChar(int i) => Current.GetChar(i);
    public long GetChars(int i, long fieldOffset, char[]? buffer, int bufferOffset, int length) => Current.GetChars(i, fieldOffset, buffer, bufferOffset, length);
    public IDataReader GetData(int i) => Current.GetData(i);
    public string GetDataTypeName(int i) => Current.GetDataTypeName(i);
    public DateTime GetDateTime(int i) => Current.GetDateTime(i);
    public decimal GetDecimal(int i) => Current.GetDecimal(i);
    public double GetDouble(int i) => Current.GetDouble(i);
    public Type GetFieldType(int i) => Current.GetFieldType(i);
    public float GetFloat(int i) => Current.GetFloat(i);
    public Guid GetGuid(int i) => Current.GetGuid(i);
    public short GetInt16(int i) => Current.GetInt16(i);
    public int GetInt32(int i) => Current.GetInt32(i);
    public long GetInt64(int i) => Current.GetInt64(i);
    public string GetName(int i) => Current.GetName(i);
    public int GetOrdinal(string name) => Current.GetOrdinal(name);
    public string GetString(int i) => Current.GetString(i);
    public object GetValue(int i) => Current.GetValue(i);
    public int GetValues(object[] values) => Current.GetValues(values);
    public bool IsDBNull(int i) => Current.IsDBNull(i);
}
