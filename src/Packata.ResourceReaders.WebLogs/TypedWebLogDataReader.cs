using System.Data;

namespace Packata.ResourceReaders.WebLogs;

internal sealed class TypedWebLogDataReader(IDataReader inner) : IDataReader
{
    private static readonly HashSet<string> Integers = new(StringComparer.OrdinalIgnoreCase)
    { "StatusCode", "sc-status", "sc-substatus", "sc-win32-status" };
    private static readonly HashSet<string> Longs = new(StringComparer.OrdinalIgnoreCase)
    { "ResponseBytes", "sc-bytes", "cs-bytes", "time-taken" };

    public object this[int i] => GetValue(i);
    public object this[string name] => GetValue(GetOrdinal(name));
    public int Depth => inner.Depth;
    public bool IsClosed => inner.IsClosed;
    public int RecordsAffected => inner.RecordsAffected;
    public int FieldCount => inner.FieldCount;
    public void Close() => inner.Close();
    public DataTable? GetSchemaTable() => inner.GetSchemaTable();
    public bool NextResult() => inner.NextResult();
    public bool Read() => inner.Read();
    public void Dispose() => inner.Dispose();
    public bool GetBoolean(int i) => Convert.ToBoolean(GetValue(i));
    public byte GetByte(int i) => Convert.ToByte(GetValue(i));
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferOffset, int length) => inner.GetBytes(i, fieldOffset, buffer, bufferOffset, length);
    public char GetChar(int i) => Convert.ToChar(GetValue(i));
    public long GetChars(int i, long fieldOffset, char[]? buffer, int bufferOffset, int length) => inner.GetChars(i, fieldOffset, buffer, bufferOffset, length);
    public IDataReader GetData(int i) => inner.GetData(i);
    public string GetDataTypeName(int i) => GetFieldType(i).Name;
    public DateTime GetDateTime(int i) => Convert.ToDateTime(GetValue(i));
    public decimal GetDecimal(int i) => Convert.ToDecimal(GetValue(i));
    public double GetDouble(int i) => Convert.ToDouble(GetValue(i));
    public Type GetFieldType(int i) => Integers.Contains(GetName(i)) ? typeof(int)
        : Longs.Contains(GetName(i)) ? typeof(long) : typeof(string);
    public float GetFloat(int i) => Convert.ToSingle(GetValue(i));
    public Guid GetGuid(int i) => Guid.Parse(GetString(i));
    public short GetInt16(int i) => Convert.ToInt16(GetValue(i));
    public int GetInt32(int i) => Convert.ToInt32(GetValue(i));
    public long GetInt64(int i) => Convert.ToInt64(GetValue(i));
    public string GetName(int i) => inner.GetName(i);
    public int GetOrdinal(string name) => inner.GetOrdinal(name);
    public string GetString(int i) => Convert.ToString(GetValue(i))!;
    public object GetValue(int i)
    {
        var value = inner.GetValue(i);
        if (value is DBNull) return value;
        return Integers.Contains(GetName(i)) ? Convert.ToInt32(value)
            : Longs.Contains(GetName(i)) ? Convert.ToInt64(value) : value;
    }
    public int GetValues(object[] values)
    {
        var length = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < length; i++) values[i] = GetValue(i);
        return length;
    }
    public bool IsDBNull(int i) => inner.IsDBNull(i);
}
