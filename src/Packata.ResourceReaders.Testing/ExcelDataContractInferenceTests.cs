using System.Reflection;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.ResourceReaders.Excel;
using Packata.Testing;

namespace Packata.ResourceReaders.Testing;

public sealed class ExcelDataContractInferenceTests
{
    [Test]
    public async Task Infer_ProfilesTypedCellsAndReadsUsingResolvedLayout()
    {
        using var stream = ExcelWorkbookFixture.Create([[null, null, null], ["id", "id", null], ["001", 2, true], ["002", 3.5, null]]);
        stream.Position = 2;
        var result = new ExcelDataContractInference(new() { Header = ExcelHeaderMode.Present, AssetId = "test" }).Infer(stream, "book.xlsx");
        var asset = result.Contract.Assets.Single();
        var fields = asset.Schema!.Fields;
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(fields.Select(field => field.Name), Is.EqualTo(new[] { "id", "id_2", "column3" }));
            Assert.That(fields.Select(field => field.LogicalType), Is.EqualTo(new[] { "string", "number", "boolean" }));
            Assert.That(fields.All(field => !field.Required), Is.True);
            Assert.That(stream.Position, Is.EqualTo(2));
            Assert.That(result.Contract.Assets.Single().Id, Is.EqualTo("test"));
        });
        var factory = ResourceReaderFactory.Create(options => options.AddExcel(), new StreamResolver(stream.ToArray()));
        using var reader = await factory.OpenAsync(result.Contract.Endpoints.Single(), asset.Schema);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetName(1), Is.EqualTo("id_2"));
        Assert.That(reader[0], Is.EqualTo("001"));
    }

    [TestCase(ExcelHeaderMode.Auto, true)]
    [TestCase(ExcelHeaderMode.Present, true)]
    [TestCase(ExcelHeaderMode.Absent, false)]
    public void Infer_HeaderModes(ExcelHeaderMode mode, bool expected)
    {
        using var stream = ExcelWorkbookFixture.Create([["id", "name"], [1, "Alice"], [2, "Bob"]]);
        var result = new ExcelDataContractInference(new() { Header = mode, MaximumRecords = 1 }).Infer(stream, "book.xlsx");
        Assert.That(result.Contract.Endpoints.Single().Format!.Options["header"], Is.EqualTo(expected));
        Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Code == "EXCEL005" && diagnostic.Severity == InferenceSeverity.Warning), Is.True);
    }

    [Test]
    public void Infer_AmbiguousTextKeepsFirstRowAndMixedTypesAreReported()
    {
        using var stream = ExcelWorkbookFixture.Create([["name", "code"], ["Alice", "001"], [true, "002"]]);
        var result = new ExcelDataContractInference(new() { Header = ExcelHeaderMode.Absent }).Infer(stream, "book.xlsx");
        Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Code == "EXCEL008"), Is.True);
        using var text = ExcelWorkbookFixture.Create([["name"], ["Alice"]]);
        var ambiguous = new ExcelDataContractInference().Infer(text, "text.xlsx");
        Assert.That(ambiguous.Contract.Endpoints.Single().Format!.Options["header"], Is.False);
        Assert.That(ambiguous.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("ambiguous", StringComparison.Ordinal)), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Infer_EmptyAndHeaderOnlySheetsKeepUnknownTypes(bool headerOnly)
    {
        using var stream = ExcelWorkbookFixture.Create(headerOnly ? [["id", "blank"]] : []);
        var result = new ExcelDataContractInference(new() { Header = ExcelHeaderMode.Present }).Infer(stream, "empty.xlsx");
        Assert.That(result.IsSuccessful, Is.True);
        Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Code == "EXCEL006"), Is.True);
        Assert.That(result.Contract.Assets.Single().Schema!.Fields.All(field => field.LogicalType is null), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Infer_DatesFormatsAndFormulaCachesAreExplicit(bool date1904)
    {
        using var stream = ExcelWorkbookFixture.Create([["date", "masked", "formula", "error"],
            [new ExcelTestCell(45000, 1), new ExcelTestCell(12, 2), new ExcelTestCell(3, Formula: "1+2"), new ExcelTestCell("#DIV/0!", Type: "e")]], date1904, true);
        var result = new ExcelDataContractInference(new() { Header = ExcelHeaderMode.Present }).Infer(stream, "dates.xlsx");
        Assert.Multiple(() =>
        {
            Assert.That(result.Contract.Assets.Single().Schema!.Fields[0].LogicalType, Is.EqualTo("datetime"));
            Assert.That(result.Contract.Assets.Single().Schema!.Fields[1].LogicalType, Is.EqualTo("number"));
            Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Code), Does.Contain("EXCEL002").And.Contain("EXCEL003").And.Contain("EXCEL009"));
        });
    }

    [Test]
    public void Infer_LegacyXlsUsesActualBinaryWorkbook()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Packata.ResourceReaders.Testing.Resources.typed-cells.xls")!;
        var result = new ExcelDataContractInference(new() { Header = ExcelHeaderMode.Absent }).Infer(stream, "typed.xls");
        Assert.That(result.IsSuccessful, Is.True);
        Assert.That(result.Contract.Assets.Single().Schema!.Fields, Is.Not.Empty);
        Assert.That(result.Contract.Endpoints.Single().Format!.Name, Is.EqualTo("xls"));
    }

    [TestCase("missing", null)]
    [TestCase(null, 0)]
    [TestCase(null, 2)]
    [TestCase("Data", 1)]
    public void Infer_InvalidSheetSelectionRestoresStream(string? name, int? number)
    {
        using var stream = ExcelWorkbookFixture.Create([["id"], [1]]);
        stream.Position = 1;
        Assert.Catch(() => new ExcelDataContractInference(new() { SheetName = name, SheetNumber = number }).Infer(stream, "bad.xlsx"));
        Assert.That(stream.Position, Is.EqualTo(1));
        Assert.That(stream.CanRead, Is.True);
    }

    [Test]
    public void Infer_RejectsLimitsCancellationAndUnsupportedSelections()
    {
        using var stream = ExcelWorkbookFixture.Create([["a", "b"], [1, 2]]);
        Assert.Throws<InvalidDataException>(() => new ExcelDataContractInference(new() { MaximumBytes = 1 }).Infer(stream, "limit.xlsx"));
        Assert.Throws<InvalidDataException>(() => new ExcelDataContractInference(new() { MaximumColumns = 1 }).Infer(stream, "limit.xlsx"));
        Assert.Throws<OperationCanceledException>(() => new ExcelDataContractInference().Infer(stream, "cancel.xlsx", new CancellationToken(true)));
        Assert.Throws<NotSupportedException>(() => new ExcelDataContractInference(new()
        { SourceFormat = new DataFormat("xlsx", Options: new Dictionary<string, object?> { ["table"] = "Table1" }) }).Infer(stream, "table.xlsx"));
        using var corrupt = new MemoryStream(new byte[100]);
        Assert.Catch(() => new ExcelDataContractInference().Infer(corrupt, "bad.xlsx"));
    }

    private sealed class StreamResolver(byte[] bytes) : Packata.Core.Reading.IEndpointStreamResolver
    {
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(new MemoryStream(bytes));
    }

    [Test]
    public void Infer_RejectsNonSeekableAndMultiRowHeaders()
    {
        using var stream = new NonSeekableStream();
        Assert.Throws<NotSupportedException>(() => new ExcelDataContractInference().Infer(stream, "stream.xlsx"));
        using var workbook = ExcelWorkbookFixture.Create([["a"], ["b"]]);
        Assert.Throws<NotSupportedException>(() => new ExcelDataContractInference(new()
        { SourceFormat = new DataFormat("xlsx", Options: new Dictionary<string, object?> { ["headerRows"] = new[] { 1, 2 } }) })
            .Infer(workbook, "headers.xlsx"));
    }

    [Test]
    public void Infer_BoundsLeadingRowsAndKeepsBlankInternalColumns()
    {
        using var workbook = ExcelWorkbookFixture.Create([[null, null, null], ["id", null, "name"], [1, null, "A"], [null, null, null]]);
        Assert.Throws<InvalidDataException>(() => new ExcelDataContractInference(new() { MaximumLeadingRows = 0 }).Infer(workbook, "leading.xlsx"));
        var result = new ExcelDataContractInference(new() { Header = ExcelHeaderMode.Present }).Infer(workbook, "leading.xlsx");
        var field = result.Contract.Assets.Single().Schema!.Fields[1];
        Assert.That(field.Name, Is.EqualTo("column2"));
        Assert.That(field.LogicalType, Is.Null);
        Assert.That(field.Extensions["packata.inference"]["observedBlank"], Is.True);
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
