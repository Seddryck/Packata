using System.Data;

namespace Packata.ResourceReaders;

internal sealed class DiscoveredSchemaDataReader(IDataReader inner) : IDataReader
{
    private string[]? _names;
    private Type[]? _types;
    private string[] Names => _names ?? [];
    public object this[int i] => GetValue(i);
    public object this[string name] => GetValue(GetOrdinal(name));
    public int Depth => inner.Depth;
    public bool IsClosed => inner.IsClosed;
    public int RecordsAffected => inner.RecordsAffected;
    public int FieldCount => Names.Length;
    public void Close() => inner.Close();
    public DataTable? GetSchemaTable() => null;
    public bool NextResult() => inner.NextResult();
    public bool Read()
    {
        if (!inner.Read()) return false;
        if (_names is null)
        {
            _names = Enumerable.Range(0, inner.FieldCount).Select(inner.GetName).ToArray();
            _types = Enumerable.Range(0, inner.FieldCount).Select(inner.GetFieldType).ToArray();
        }
        return true;
    }
    public void Dispose() => inner.Dispose();
    public bool GetBoolean(int i) => Convert.ToBoolean(GetValue(i));
    public byte GetByte(int i) => Convert.ToByte(GetValue(i));
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public char GetChar(int i) => Convert.ToChar(GetValue(i));
    public long GetChars(int i, long fieldOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public IDataReader GetData(int i) => throw new NotSupportedException();
    public string GetDataTypeName(int i) => GetFieldType(i).Name;
    public DateTime GetDateTime(int i) => Convert.ToDateTime(GetValue(i));
    public decimal GetDecimal(int i) => Convert.ToDecimal(GetValue(i));
    public double GetDouble(int i) => Convert.ToDouble(GetValue(i));
    public Type GetFieldType(int i) => _types?[i] ?? typeof(object);
    public float GetFloat(int i) => Convert.ToSingle(GetValue(i));
    public Guid GetGuid(int i) => GetValue(i) is Guid value ? value : Guid.Parse(GetString(i));
    public short GetInt16(int i) => Convert.ToInt16(GetValue(i));
    public int GetInt32(int i) => Convert.ToInt32(GetValue(i));
    public long GetInt64(int i) => Convert.ToInt64(GetValue(i));
    public string GetName(int i) => Names[i];
    public int GetOrdinal(string name)
    {
        var index = Array.IndexOf(Names, name);
        return index >= 0 ? index : throw new IndexOutOfRangeException($"Field '{name}' not found.");
    }
    public string GetString(int i) => Convert.ToString(GetValue(i))!;
    public object GetValue(int i)
    {
        var occurrence = Names.Take(i + 1).Count(name => name == Names[i]);
        var currentOccurrence = 0;
        for (var ordinal = 0; ordinal < inner.FieldCount; ordinal++)
        {
            if (inner.GetName(ordinal) != Names[i]) continue;
            if (++currentOccurrence == occurrence) return inner.GetValue(ordinal);
        }
        return DBNull.Value;
    }
    public int GetValues(object[] values)
    {
        var length = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < length; i++) values[i] = GetValue(i);
        return length;
    }
    public bool IsDBNull(int i) => GetValue(i) is DBNull;
}
