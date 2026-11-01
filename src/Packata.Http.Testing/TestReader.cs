using System.Collections;
using System.Data;
using System.Data.Common;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.Http.Testing;

internal sealed class TestReader(string[] names, int count, Func<int, object?[]> getRow) : DbDataReader
{
    private int index = -1;
    private object?[] row = [];
    public int Disposals { get; private set; }
    public int ReadCalls { get; private set; }
    public override bool Read()
    {
        ReadCalls++;
        if (++index >= count) return false;
        row = getRow(index);
        return true;
    }
    protected override void Dispose(bool disposing) { if (disposing) Disposals++; }
    public override bool IsClosed => Disposals > 0;
    public override int FieldCount => names.Length;
    public override string GetName(int ordinal) => names[ordinal];
    public override object GetValue(int ordinal) => row[ordinal]!;
    public override bool IsDBNull(int ordinal) => GetValue(ordinal) is null or DBNull;
    public override int GetOrdinal(string name) => System.Array.IndexOf(names, name);
    public override bool NextResult() => false;
    public override int Depth => 0;
    public override int RecordsAffected => -1;
    public override bool HasRows => count > 0;
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));
    public override Type GetFieldType(int ordinal) => typeof(object);
    public override string GetDataTypeName(int ordinal) => "object";
    public override int GetValues(object[] values) => throw new NotSupportedException();
    public override IEnumerator GetEnumerator() => throw new NotSupportedException();
    public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
    public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
    public override long GetBytes(int ordinal, long offset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public override char GetChar(int ordinal) => (char)GetValue(ordinal);
    public override long GetChars(int ordinal, long offset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
    public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
    public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
    public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
    public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
    public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
    public override string GetString(int ordinal) => (string)GetValue(ordinal);
}

internal sealed class TestFactory(Func<DataEndpointReadRequest, CancellationToken, IDataReader> open) : IDataEndpointReaderFactory
{
    public int Opens { get; private set; }
    public DataEndpointReadRequest? Request { get; private set; }
    public ValueTask<IDataReader> OpenAsync(DataEndpoint endpoint, DataSchema? schema = null, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Asset context is required.");
    public ValueTask<IDataReader> OpenAsync(DataEndpointReadRequest request, CancellationToken cancellationToken = default)
    {
        Opens++; Request = request;
        return ValueTask.FromResult(open(request, cancellationToken));
    }
}
