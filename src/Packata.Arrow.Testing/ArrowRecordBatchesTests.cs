using System.Collections;
using System.Data;
using System.Data.Common;
using Apache.Arrow;
using Apache.Arrow.Types;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.ResourceReaders;

namespace Packata.Arrow.Testing;

public class ArrowRecordBatchesTests
{
    private static DataSchema Schema(params DataField[] fields) => new(fields);
    private static DataField Field(string type, bool nullable = false, params DataConstraint[] parameters)
        => new("value", type, Constraints: parameters, Nullable: nullable);

    [TestCase("boolean", ArrowTypeId.Boolean)]
    [TestCase("integer", ArrowTypeId.Int64)]
    [TestCase("number", ArrowTypeId.Double)]
    [TestCase("string", ArrowTypeId.String)]
    [TestCase("binary", ArrowTypeId.Binary)]
    [TestCase("date", ArrowTypeId.Date32)]
    [TestCase("timestamp", ArrowTypeId.Timestamp)]
    public void MapsCanonicalTypesAndNullability(string type, ArrowTypeId id)
    {
        var schema = ArrowSchemaMapper.Map(Schema(Field(type, true)));
        Assert.That(schema.GetFieldByIndex(0).DataType.TypeId, Is.EqualTo(id));
        Assert.That(schema.GetFieldByIndex(0).IsNullable, Is.True);
        Assert.That(ArrowSchemaMapper.Map(Schema(Field(type))).GetFieldByIndex(0).IsNullable, Is.False);
    }

    [TestCase(8, true, ArrowTypeId.Int8)]
    [TestCase(16, true, ArrowTypeId.Int16)]
    [TestCase(32, true, ArrowTypeId.Int32)]
    [TestCase(64, true, ArrowTypeId.Int64)]
    [TestCase(8, false, ArrowTypeId.UInt8)]
    [TestCase(16, false, ArrowTypeId.UInt16)]
    [TestCase(32, false, ArrowTypeId.UInt32)]
    [TestCase(64, false, ArrowTypeId.UInt64)]
    public void MapsAndConvertsIntegerWidths(int width, bool signed, ArrowTypeId id)
    {
        var schema = Schema(Field("integer", false, new("integerWidth", width), new("signed", signed)));
        using var reader = new RowsReader(["value"], 1, _ => [42]);
        using var batch = ArrowRecordBatches.Read(reader, schema).Single();
        Assert.That(batch.Column(0).Data.DataType.TypeId, Is.EqualTo(id));
        Assert.That(batch.Column(0).Length, Is.EqualTo(1));
    }

    [Test]
    public void ConvertsValuesInContractOrderAndNulls()
    {
        var contract = Schema(new("id", "integer", PhysicalName: "source_id"), new("text", "string", Nullable: true),
            new("flag", "boolean"), new("float", "number"), new("bytes", "binary"),
            new("amount", "number", Constraints: [new("precision", 8), new("scale", 2)]),
            new("day", "date"), new("instant", "timestamp"));
        var instant = new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.FromHours(2));
        using var reader = new RowsReader(["text", "source_id", "flag", "float", "bytes", "amount", "day", "instant"], 2,
            i => [i == 0 ? "héllo" : DBNull.Value, 7, true, 1.5f, new byte[] { 0, 255 }, 123.45m, new DateOnly(1969, 12, 31), instant]);
        using var batch = ArrowRecordBatches.Read(reader, contract).Single();
        Assert.Multiple(() =>
        {
            Assert.That(((Int64Array)batch.Column(0)).GetValue(0), Is.EqualTo(7));
            Assert.That(((StringArray)batch.Column(1)).GetString(0), Is.EqualTo("héllo"));
            Assert.That(batch.Column(1).IsNull(1), Is.True);
            Assert.That(((BooleanArray)batch.Column(2)).GetValue(0), Is.True);
            Assert.That(((DoubleArray)batch.Column(3)).GetValue(0), Is.EqualTo(1.5));
            Assert.That(((BinaryArray)batch.Column(4)).GetBytes(0).ToArray(), Is.EqualTo(new byte[] { 0, 255 }));
            Assert.That(((Decimal128Array)batch.Column(5)).GetValue(0), Is.EqualTo(123.45m));
            Assert.That(((Date32Array)batch.Column(6)).GetValue(0), Is.EqualTo(-1));
            Assert.That(((TimestampArray)batch.Column(7)).GetTimestamp(0), Is.EqualTo(instant.ToUniversalTime()));
        });
    }

    [Test]
    public void EmptyInputHasSchemaAndNoBatchesAndDisposesReader()
    {
        var reader = new RowsReader(["value"], 0, _ => []);
        Assert.That(ArrowRecordBatches.Read(reader, Schema(Field("integer"))).Count(), Is.Zero);
        Assert.That(reader.Disposals, Is.EqualTo(1));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void DiscoversStreamingHeadersOnFirstRead(int count)
    {
        var reader = new RowsReader(["value"], count, _ => [42], discoverOnRead: true);
        var rows = 0;
        foreach (var batch in ArrowRecordBatches.Read(reader, Schema(Field("integer"))))
        {
            using (batch) rows += batch.Length;
        }
        Assert.That(rows, Is.EqualTo(count));
        Assert.That(reader.Disposals, Is.EqualTo(1));
    }

    [Test]
    public void LargeLazyInputHasBoundedBatchesAndNoReadAheadAtRowLimit()
    {
        var reader = new RowsReader(["value"], 100_003, i => [(long)i]);
        using var enumerator = ArrowRecordBatches.Read(reader, Schema(Field("integer")), new() { BatchSize = 128 }).GetEnumerator();
        Assert.That(enumerator.MoveNext(), Is.True);
        using var first = enumerator.Current;
        Assert.That(reader.ReadCalls, Is.EqualTo(128));
        var total = first.Length;
        while (enumerator.MoveNext())
        {
            using var batch = enumerator.Current;
            Assert.That(batch.Length, Is.InRange(1, 128));
            Assert.That(((Int64Array)batch.Column(0)).GetValue(0), Is.EqualTo(total));
            // Earlier caller-owned batches stay usable after advancing the enumerator.
            Assert.That(((Int64Array)first.Column(0)).GetValue(0), Is.Zero);
            total += batch.Length;
        }
        Assert.That(total, Is.EqualTo(100_003));
        Assert.That(reader.Disposals, Is.EqualTo(1));
    }

    [Test]
    public void ByteBudgetSplitsVariableWidthRowsAndPreservesReusedBinaryBuffers()
    {
        var bytes = new byte[10];
        var reader = new RowsReader(["value"], 5, i => { bytes[0] = (byte)i; return [bytes]; });
        var sizes = new List<int>();
        var total = 0;
        foreach (var batch in ArrowRecordBatches.Read(reader, Schema(Field("binary")), new() { MaxBufferedBytes = 34 }))
        {
            using (batch)
            {
                sizes.Add(batch.Length);
                for (var i = 0; i < batch.Length; i++)
                    Assert.That(((BinaryArray)batch.Column(0)).GetBytes(i)[0], Is.EqualTo(total++));
            }
        }
        Assert.That(sizes, Is.EqualTo(new[] { 2, 2, 1 }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EarlyExitHonorsLeaveOpen(bool leaveOpen)
    {
        var reader = new RowsReader(["value"], 20, _ => [1]);
        using (var enumerator = ArrowRecordBatches.Read(reader, Schema(Field("integer")),
            new() { BatchSize = 1, LeaveOpen = leaveOpen }).GetEnumerator())
        {
            Assert.That(enumerator.MoveNext(), Is.True);
            enumerator.Current.Dispose();
        }
        Assert.That(reader.Disposals, Is.EqualTo(leaveOpen ? 0 : 1));
        reader.Dispose();
    }

    [TestCase("array")]
    [TestCase("object")]
    [TestCase("time")]
    [TestCase("unknown")]
    [TestCase("decimal")]
    public void UnsupportedSchemaDisposesReader(string type)
    {
        var reader = new RowsReader(["value"], 0, _ => []);
        Assert.Throws<NotSupportedException>(() => ArrowRecordBatches.Read(reader, Schema(Field(type))).Count());
        Assert.That(reader.Disposals, Is.EqualTo(1));
    }

    [Test]
    public void RejectsAmbiguousSchemaAndColumnNames()
    {
        Assert.Throws<NotSupportedException>(() => ArrowSchemaMapper.Map(Schema(Field("integer"), Field("integer"))));
        Assert.Throws<NotSupportedException>(() => ArrowSchemaMapper.Map(Schema(Field("integer", false, new DataConstraint("integerWidth", 7)))));
        Assert.Throws<NotSupportedException>(() => ArrowSchemaMapper.Map(Schema(Field("integer", false, new DataConstraint("signed", "false")))));
        Assert.Throws<NotSupportedException>(() => ArrowSchemaMapper.Map(Schema(Field("number", false, new DataConstraint("precision", 9)))));
        Assert.Throws<NotSupportedException>(() => ArrowSchemaMapper.Map(Schema(Field("timestamp", false, new DataConstraint("defaultTimezone", "Europe/Brussels")))));
        foreach (var names in new[] { new[] { "other" }, new[] { "value", "value" } })
        {
            var reader = new RowsReader(names, 0, _ => []);
            Assert.Throws<NotSupportedException>(() => ArrowRecordBatches.Read(reader, Schema(Field("integer"))).Count());
            Assert.That(reader.Disposals, Is.EqualTo(1));
        }
    }

    [Test]
    public void RejectsLossyValuesAndDisposesOnFailure()
    {
        var cases = new (DataField Field, object Value)[]
        {
            (Field("integer"), 1.5), (Field("integer"), ulong.MaxValue), (Field("integer"), DBNull.Value),
            (Field("number"), 9007199254740993L), (Field("string"), "\ud800"),
            (Field("number", false, new("precision", 4), new("scale", 2)), 123.45m),
            (Field("decimal", false, new("precision", 4), new("scale", 2)), 1.234m),
            (Field("date"), DateTime.UtcNow.Date.AddHours(1)),
            (Field("timestamp"), new DateTime(2026, 1, 1)),
            (Field("timestamp"), DateTime.UnixEpoch.AddTicks(1)),
            (Field("timestamp", false, new DataConstraint("precision", "nanosecond")), DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)),
            (Field("string"), new string('x', 100))
        };
        foreach (var item in cases)
        {
            var reader = new RowsReader(["value"], 1, _ => [item.Value]);
            Assert.Throws<InvalidDataException>(() => ArrowRecordBatches.Read(reader, Schema(item.Field),
                new() { MaxBufferedBytes = 64 }).Count(), item.Field.LogicalType!);
            Assert.That(reader.Disposals, Is.EqualTo(1));
        }
    }

    [TestCase("second", 1_000_000_000L)]
    [TestCase("millisecond", 1_000_000L)]
    [TestCase("microsecond", 1_000L)]
    [TestCase("nanosecond", 1L)]
    public void TimestampUnitsAndNaiveSemantics(string unit, long divisor)
    {
        var field = Field("timestamp", false, new("precision", unit), new("timezone", false));
        var value = DateTime.SpecifyKind(DateTime.UnixEpoch.AddSeconds(-1), DateTimeKind.Unspecified);
        using var reader = new RowsReader(["value"], 1, _ => [value]);
        using var batch = ArrowRecordBatches.Read(reader, Schema(field)).Single();
        Assert.That(((TimestampArray)batch.Column(0)).GetValue(0), Is.EqualTo(-1_000_000_000L / divisor));
        Assert.That(((TimestampType)batch.Schema.GetFieldByIndex(0).DataType).Timezone, Is.Null.Or.Empty);
    }

    [Test]
    public void ReaderFailureAndCancellationDisposeReader()
    {
        var failed = new RowsReader(["value"], 2, _ => throw new IOException("source failed"));
        Assert.Throws<IOException>(() => ArrowRecordBatches.Read(failed, Schema(Field("integer"))).Count());
        Assert.That(failed.Disposals, Is.EqualTo(1));
        using var source = new CancellationTokenSource();
        var reader = new RowsReader(["value"], 5, _ => { source.Cancel(); return [1]; });
        Assert.Throws<OperationCanceledException>(() => ArrowRecordBatches.Read(reader, Schema(Field("integer")), cancellationToken: source.Token).Count());
        Assert.That(reader.ReadCalls, Is.EqualTo(1));
        Assert.That(reader.Disposals, Is.EqualTo(1));
        var preCanceled = new RowsReader(["value"], 5, _ => [1]);
        Assert.Throws<OperationCanceledException>(() => ArrowRecordBatches.Read(preCanceled, Schema(Field("integer")), cancellationToken: source.Token).Count());
        Assert.That(preCanceled.ReadCalls, Is.Zero);
        Assert.That(preCanceled.Disposals, Is.EqualTo(1));
    }

    [Test]
    public async Task ConvertsAnExistingPackataReader()
    {
        var schema = Schema(Field("integer"));
        var endpoint = new DataEndpoint("inline", null, EndpointKind.Inline, null,
            new InlineLocation(new[] { new object[] { 42 }, new object[] { 43 } }));
        var reader = await new ResourceReaderFactory().OpenAsync(endpoint, schema);
        using var batch = ArrowRecordBatches.Read(reader, schema).Single();
        Assert.That(((Int64Array)batch.Column(0)).GetValue(1), Is.EqualTo(43));
        Assert.That(reader.IsClosed, Is.True);
    }

    [Test]
    public void InvalidOptionsDoNotTakeOwnership()
    {
        using var reader = new RowsReader(["value"], 1, _ => [1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => ArrowRecordBatches.Read(reader, Schema(Field("integer")), new() { BatchSize = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ArrowRecordBatches.Read(reader, Schema(Field("integer")), new() { MaxBufferedBytes = 0 }));
        Assert.That(reader.Disposals, Is.Zero);
    }

    internal sealed class RowsReader(string[] names, int count, Func<int, object?[]> getRow, bool discoverOnRead = false) : DbDataReader
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
        public override int FieldCount => discoverOnRead && ReadCalls == 0 ? 0 : names.Length;
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
}
