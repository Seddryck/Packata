using System.Data;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using PocketCsvReader.Configuration;
using PocketCsvReader.Indexing;

namespace Packata.ResourceReaders.Testing;

public class IndexedLookupTests
{
    private const string Header = "Date,CustomerId,OrderId\n";
    private static readonly DateOnly Date = new(2026, 5, 17);
    private static readonly DataSchema Schema = new([new("Date", "date"), new("CustomerId", "string"), new("OrderId", "integer")])
    {
        Indexes =
        [
            new("Date", DataIndexKind.Ordered, Order: DataIndexOrder.Ascending),
            new("CustomerId", DataIndexKind.Bloom, Comparison: DataIndexComparison.OrdinalIgnoreCase)
        ]
    };

    [Test]
    public async Task Prunes_partitions_then_bloom_and_preserves_exact_matches_in_source_order()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var missing = Path.Combine(folder.FullName, "unselected.csv");
            var negative = Write(folder, "negative.csv", "2026-05-17,other,20\n");
            var first = Write(folder, "z-first.csv", string.Concat(
                "2026-05-16,CUSTOMER,1\n",
                "2026-05-17,CUSTOMER,2\n",
                "2026-05-17,other,3\n",
                "2026-05-17,customer,4\n",
                "2026-05-18,customer,5\n"));
            var second = Write(folder, "a-second.csv", "2026-05-17,customer,6\n2026-05-17,customer,7\n");
            BuildBloom(negative);
            foreach (var path in new[] { first, second })
            {
                BuildBloom(path);
                BuildOrdered(path);
            }

            var factory = new ResourceReaderFactory();
            var request = Request(missing, negative, first, second) with
            {
                IndexedRead = new(
                    [new("Date", DataIndexKind.Ordered, Date), new("CustomerId", DataIndexKind.Bloom, "Customer")],
                    path => path != missing)
            };
            using var indexed = await factory.OpenAsync(request);
            using var sequential = await factory.OpenAsync(Request(negative, first, second));
            var expected = Orders(sequential, row => row.GetValue(0) is DateOnly date && date == Date
                && string.Equals(row.GetString(1), "Customer", StringComparison.OrdinalIgnoreCase));

            Assert.That(expected, Is.EqualTo(new[] { 2, 4, 6, 7 }));
            Assert.That(Orders(indexed, _ => true), Is.EqualTo(expected));
        }
        finally { folder.Delete(recursive: true); }
    }

    [TestCase(DataIndexKind.Ordered, "missing")]
    [TestCase(DataIndexKind.Ordered, "stale")]
    [TestCase(DataIndexKind.Ordered, "corrupt")]
    [TestCase(DataIndexKind.Ordered, "incompatible")]
    [TestCase(DataIndexKind.Ordered, "unreadable")]
    [TestCase(DataIndexKind.Bloom, "missing")]
    [TestCase(DataIndexKind.Bloom, "stale")]
    [TestCase(DataIndexKind.Bloom, "corrupt")]
    [TestCase(DataIndexKind.Bloom, "incompatible")]
    [TestCase(DataIndexKind.Bloom, "unreadable")]
    public async Task Explicit_lookup_requires_usable_sidecars_but_ordinary_reads_scan(DataIndexKind kind, string failure)
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var path = Write(folder, "orders.csv", "2026-05-17,customer,1\n");
            var ordered = kind == DataIndexKind.Ordered;
            var sidecar = path + (ordered ? ".sparse.idx" : ".bloom.idx");
            if (failure != "missing")
            {
                if (ordered) BuildOrdered(path, wrongColumn: failure == "incompatible");
                else BuildBloom(path, failure == "incompatible" ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            }
            if (failure == "stale") File.AppendAllText(path, "2026-05-18,customer,2\n");
            if (failure == "corrupt") File.WriteAllText(sidecar, "corrupt");
            using var locked = failure == "unreadable" ? File.Open(sidecar, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;

            var factory = new ResourceReaderFactory();
            var request = Request(path);
            using var ordinary = await factory.OpenAsync(request);
            Assert.That(ordinary.Read(), Is.True);

            var equality = ordered ? new IndexedEquality("Date", kind, Date) : new("CustomerId", kind, "customer");
            using var indexed = await factory.OpenAsync(request with { IndexedRead = new([equality]) });

            var exceptionType = failure switch
            {
                "missing" => typeof(FileNotFoundException),
                "unreadable" => typeof(IOException),
                _ => typeof(InvalidDataException)
            };
            Assert.That(() => indexed.Read(), Throws.InstanceOf(exceptionType));
        }
        finally { folder.Delete(recursive: true); }
    }

    [Test]
    public void An_undeclared_index_fails_before_opening_the_file()
    {
        var request = Request("missing.csv") with { IndexedRead = new([new("OrderId", DataIndexKind.Bloom, 1)]) };

        Assert.That(async () => await new ResourceReaderFactory().OpenAsync(request),
            Throws.ArgumentException.With.Message.Contains("OrderId"));
    }

    [Test]
    public async Task Providers_receive_resolved_indexes_without_factory_stream_acquisition()
    {
        var provider = new TestIndexedProvider();
        var factory = ResourceReaderFactory.Create(options => options.AddProvider(provider));
        var request = Request("missing.csv") with { IndexedRead = new([new("Date", DataIndexKind.Ordered, Date)]) };

        using var reader = await factory.OpenAsync(request);

        var equality = provider.Context!.Equalities.Single();
        Assert.That(equality, Is.EqualTo(new ResolvedIndexedEquality(Schema.Indexes[0], Schema.Fields[0], 0, Date)));
    }

    [Test]
    public void A_reader_without_index_support_rejects_the_request()
    {
        var request = Request("missing.csv") with { IndexedRead = new([new("Date", DataIndexKind.Ordered, Date)]) };
        var factory = ResourceReaderFactory.Create(options => options.AddProvider(new TestOrdinaryProvider()));

        Assert.That(async () => await factory.OpenAsync(request), Throws.TypeOf<NotSupportedException>());
    }

    private static DataEndpointReadRequest Request(params string[] paths) =>
        new(new DataEndpoint("orders", null, EndpointKind.File, null, new PathLocation(paths),
            new DataFormat("csv", Options: new Dictionary<string, object?> { ["lineTerminator"] = "\n" })), Schema);

    private static string Write(DirectoryInfo folder, string name, string rows)
    {
        var path = Path.Combine(folder.FullName, name);
        File.WriteAllText(path, Header + rows);
        return path;
    }

    private static int[] Orders(IDataReader rows, Func<IDataReader, bool> keep)
    {
        var result = new List<int>();
        while (rows.Read())
            if (keep(rows)) result.Add(rows.GetInt32(2));
        return result.ToArray();
    }

    private static void BuildBloom(string path, StringComparison comparison = StringComparison.OrdinalIgnoreCase) =>
        Builder().Build().BuildIndexes(path, new BloomIndex(new IndexKey<string>("CustomerId", comparison: comparison)));

    private static void BuildOrdered(string path, bool wrongColumn = false) =>
        Builder().Build().BuildIndexes(path, new SparseIndex(
            wrongColumn ? new IndexKey<int>("OrderId") : new IndexKey<DateOnly>("Date"), checkpointInterval: 1));

    private static CsvReaderBuilder Builder()
    {
        var schema = new SchemaDescriptorBuilder().Indexed();
        schema.WithTemporalField(typeof(DateOnly), "Date", field => field.WithFormat("yyyy-MM-dd"));
        schema.WithField(typeof(string), "CustomerId");
        schema.WithField(typeof(int), "OrderId");
        return new CsvReaderBuilder()
            .WithDialect(dialect => dialect.WithLineTerminator("\n"))
            .WithResource(new ResourceDescriptorBuilder())
            .WithSchema(schema);
    }

    private class TestOrdinaryProvider : IDataEndpointReaderProvider
    {
        public bool CanHandle(DataEndpointReadRequest request, ResolvedDataFormat format) => true;

        public ValueTask<IDataReader> OpenAsync(ReaderOpenContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The ordinary path must never handle this lookup.");
    }

    private sealed class TestIndexedProvider : TestOrdinaryProvider, IDataEndpointIndexedReaderProvider
    {
        public IndexedReaderOpenContext? Context { get; private set; }

        public ValueTask<IDataReader> OpenIndexedAsync(IndexedReaderOpenContext context, CancellationToken cancellationToken = default)
        {
            Context = context;
            return ValueTask.FromResult<IDataReader>(new DataTable().CreateDataReader());
        }
    }
}
