using System.Data;
using Packata.Core;
using Packata.Core.Contracts;

namespace Packata.ResourceReaders;

internal sealed class NamedSchemaDataReader(IDataReader inner, DataSchema schema) : IDataReader
{
    private readonly string[] _names = schema.Fields.Select(field => field.Name).ToArray();
    private readonly Type[] _types = schema.Fields
        .Select(field => new RuntimeTypeMapper().Map(field.LogicalType, field.Format)).ToArray();

    public object this[int i] => GetValue(i);
    public object this[string name] => GetValue(GetOrdinal(name));
    public int Depth => inner.Depth;
    public bool IsClosed => inner.IsClosed;
    public int RecordsAffected => inner.RecordsAffected;
    public int FieldCount => _names.Length;
    public void Close() => inner.Close();
    public DataTable? GetSchemaTable() => null;
    public bool NextResult() => inner.NextResult();
    public bool Read() => inner.Read();
    public void Dispose() => inner.Dispose();
    public bool GetBoolean(int i) => Convert.ToBoolean(GetValue(i));
    public byte GetByte(int i) => Convert.ToByte(GetValue(i));
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferOffset, int length) =>
        Copy((byte[])GetValue(i), fieldOffset, buffer, bufferOffset, length);
    public char GetChar(int i) => Convert.ToChar(GetValue(i));
    public long GetChars(int i, long fieldOffset, char[]? buffer, int bufferOffset, int length) =>
        Copy(GetString(i).ToCharArray(), fieldOffset, buffer, bufferOffset, length);
    public IDataReader GetData(int i) => throw new NotSupportedException();
    public string GetDataTypeName(int i) => _types[i].Name;
    public DateTime GetDateTime(int i) => Convert.ToDateTime(GetValue(i));
    public decimal GetDecimal(int i) => Convert.ToDecimal(GetValue(i));
    public double GetDouble(int i) => Convert.ToDouble(GetValue(i));
    public Type GetFieldType(int i) => _types[i];
    public float GetFloat(int i) => Convert.ToSingle(GetValue(i));
    public Guid GetGuid(int i) => GetValue(i) is Guid value ? value : Guid.Parse(GetString(i));
    public short GetInt16(int i) => Convert.ToInt16(GetValue(i));
    public int GetInt32(int i) => Convert.ToInt32(GetValue(i));
    public long GetInt64(int i) => Convert.ToInt64(GetValue(i));
    public string GetName(int i) => _names[i];
    public int GetOrdinal(string name)
    {
        var index = Array.IndexOf(_names, name);
        return index >= 0 ? index : throw new IndexOutOfRangeException($"Field '{name}' not found.");
    }
    public string GetString(int i) => Convert.ToString(GetValue(i))!;
    public object GetValue(int i)
    {
        try { return inner.GetValue(inner.GetOrdinal(_names[i])); }
        catch (Exception exception) when (exception is ArgumentException or IndexOutOfRangeException)
        { return DBNull.Value; }
    }
    public int GetValues(object[] values)
    {
        var length = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < length; i++) values[i] = GetValue(i);
        return length;
    }
    public bool IsDBNull(int i) => GetValue(i) is DBNull;

    private static long Copy<T>(T[] source, long fieldOffset, T[]? buffer, int bufferOffset, int length)
    {
        var available = Math.Max(0, source.Length - checked((int)fieldOffset));
        if (buffer is null) return source.Length;
        var count = Math.Min(available, length);
        Array.Copy(source, fieldOffset, buffer, bufferOffset, count);
        return count;
    }
}
