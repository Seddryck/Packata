using System.Data;
using System.Data.Common;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.Application.Testing;

public sealed class DataPreviewServiceTests
{
    [TestCase(0)]
    [TestCase(-1)]
    public async Task InvalidLimits_DoNotOpenReaders(int limit)
    {
        var factory = new Mock<IDataEndpointReaderFactory>(MockBehavior.Strict);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new DataPreviewService(factory.Object).ReadAsync(Request(), new(limit)));
        factory.VerifyNoOtherCalls();
    }

    [Test]
    public async Task CancellationBeforeOpening_DoesNotAccessData()
    {
        var factory = new Mock<IDataEndpointReaderFactory>(MockBehavior.Strict);
        await Assert.ThrowsAsync<OperationCanceledException>(() => new DataPreviewService(factory.Object)
            .ReadAsync(Request(), new(), new CancellationToken(true)));
        factory.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Read_ProjectsOrderedColumnsAndTypesAndStopsAtLimit()
    {
        using var data = new DataTable();
        data.Columns.Add("id", typeof(int));
        data.Columns.Add("name", typeof(string));
        data.Rows.Add(1, "Alice");
        data.Rows.Add(2, DBNull.Value);
        var reader = data.CreateDataReader();
        var request = Request(new([new("id", "integer"), new("name", "string")]));
        var sample = await new DataPreviewService(Factory(reader)).ReadAsync(request, new(2, ["name", "id"]));
        Assert.Multiple(() =>
        {
            Assert.That(sample.Columns, Is.EqualTo(new[] { new DataPreviewColumn("name", "string"), new("id", "integer") }));
            Assert.That(sample.Rows[0], Is.EqualTo(new object?[] { "Alice", 1 }));
            Assert.That(sample.Rows[1], Is.EqualTo(new object?[] { null, 2 }));
            Assert.That(reader.IsClosed, Is.True);
        });
    }

    [Test]
    public async Task Read_DiscoversHeadersOnFirstReadAndNeverReadsPastSample()
    {
        var reader = new Mock<IDataReader>(MockBehavior.Strict);
        reader.Setup(value => value.Read()).Returns(true);
        reader.SetupGet(value => value.FieldCount).Returns(1);
        reader.Setup(value => value.GetName(0)).Returns("id");
        reader.Setup(value => value.GetFieldType(0)).Returns(typeof(int));
        reader.Setup(value => value.IsDBNull(0)).Returns(false);
        reader.Setup(value => value.GetValue(0)).Returns(42);
        reader.Setup(value => value.Dispose());
        var factory = new Mock<IDataEndpointReaderFactory>();
        var request = Request();
        factory.Setup(value => value.OpenAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(reader.Object);
        var sample = await new DataPreviewService(factory.Object).ReadAsync(request, new(1));
        reader.Verify(value => value.Read(), Times.Once);
        reader.Verify(value => value.Dispose(), Times.Once);
        Assert.That(sample.Columns.Single().Type, Is.EqualTo("Int32"));
        Assert.That(sample.Rows.Single(), Is.EqualTo(new object?[] { 42 }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EmptyAndHeaderOnlyInputs_PreserveDeclaredColumns(bool headers)
    {
        using var data = new DataTable();
        if (headers) data.Columns.Add("id", typeof(int));
        var reader = data.CreateDataReader();
        var sample = await new DataPreviewService(Factory(reader)).ReadAsync(Request(new([new("id", "integer")])), new());
        Assert.That(sample.Columns.Single(), Is.EqualTo(new DataPreviewColumn("id", "integer")));
        Assert.That(sample.Rows, Is.Empty);
        Assert.That(reader.IsClosed, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InvalidColumns_DisposeTheOpenedReader(bool duplicate)
    {
        var reader = new Mock<IDataReader>();
        reader.SetupGet(value => value.FieldCount).Returns(duplicate ? 2 : 1);
        reader.Setup(value => value.GetName(It.IsAny<int>())).Returns("id");
        var error = await Assert.ThrowsAsync<DataPreviewSelectionException>(() => new DataPreviewService(Factory(reader.Object))
            .ReadAsync(Request(), new(1, [duplicate ? "id" : "missing"])));
        Assert.That(error!.Failure, Is.EqualTo(DataPreviewSelectionFailure.Column));
        reader.Verify(value => value.Dispose(), Times.Once);
    }

    [Test]
    public async Task ReadFailure_DisposesTheOpenedReader()
    {
        var reader = new Mock<IDataReader>();
        reader.Setup(value => value.Read()).Throws(new InvalidDataException("invalid row"));
        await Assert.ThrowsAsync<InvalidDataException>(() => new DataPreviewService(Factory(reader.Object)).ReadAsync(Request(), new()));
        reader.Verify(value => value.Dispose(), Times.Once);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RepeatedHeaders_RespectPositionalViewerPolicy(bool requireUnique)
    {
        var reader = new Mock<IDataReader>();
        reader.SetupGet(value => value.FieldCount).Returns(2);
        reader.Setup(value => value.GetName(It.IsAny<int>())).Returns("id");
        reader.Setup(value => value.Read()).Returns(true);
        reader.Setup(value => value.GetValue(0)).Returns(1);
        reader.Setup(value => value.GetValue(1)).Returns(2);
        var service = new DataPreviewService(Factory(reader.Object));
        if (requireUnique)
            await Assert.ThrowsAsync<DataPreviewSelectionException>(() => service.ReadAsync(Request(), new(1)));
        else
        {
            var sample = await service.ReadAsync(Request(), new(1, RequireUniqueColumnNames: false));
            Assert.That(sample.Columns.Select(column => column.Name), Is.EqualTo(new[] { "id", "id" }));
            Assert.That(sample.Rows.Single(), Is.EqualTo(new object?[] { 1, 2 }));
        }
        reader.Verify(value => value.Dispose(), Times.Once);
    }

    [Test]
    public async Task AsyncReaders_ReceiveCancellationAndAreDisposed()
    {
        using var cancellation = new CancellationTokenSource();
        var reader = new Mock<DbDataReader>();
        reader.Setup(value => value.ReadAsync(cancellation.Token)).Returns(() =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<bool>(cancellation.Token);
        });
        await Assert.ThrowsAsync<TaskCanceledException>(() => new DataPreviewService(Factory(reader.Object))
            .ReadAsync(Request(), new(), cancellation.Token));
        reader.Verify(value => value.ReadAsync(cancellation.Token), Times.Once);
        reader.Protected().Verify("Dispose", Times.Once(), new object[] { true });
    }

    [Test]
    public void Selection_RequiresUniqueBindingsAndEndpointsButAllowsExplicitBindingIndex()
    {
        var first = Request().Endpoint;
        var second = first with { Id = "second" };
        var asset = new DataAsset("orders", "orders", "physical_orders", null, AssetKind.Table, null,
            [new(first.Id, "bound_orders"), new(second.Id)]);
        var contract = new DataContract(new("contract"), new(), [asset], [first, second], new());
        var ambiguous = Assert.Throws<DataPreviewSelectionException>(() => DataPreviewService.SelectRequest(contract, asset));
        Assert.That(ambiguous!.Failure, Is.EqualTo(DataPreviewSelectionFailure.Binding));
        var request = DataPreviewService.SelectRequest(contract, asset, 0);
        Assert.That(request.AssetPath, Is.EqualTo("bound_orders"));
        Assert.That(request.Contract, Is.SameAs(contract));
        Assert.That(DataPreviewService.SelectRequest(contract, asset, 1).AssetPath, Is.EqualTo("physical_orders"));
        Assert.Throws<DataPreviewSelectionException>(() => DataPreviewService.SelectRequest(contract, asset, 2));
        Assert.Throws<DataPreviewSelectionException>(() => DataPreviewService.SelectRequest(contract, asset, -1));
        var missing = Assert.Throws<DataPreviewSelectionException>(() => DataPreviewService.SelectRequest(contract with { Endpoints = [] }, asset, 0));
        Assert.That(missing!.Failure, Is.EqualTo(DataPreviewSelectionFailure.Endpoint));
        var duplicate = Assert.Throws<DataPreviewSelectionException>(() => DataPreviewService.SelectRequest(contract with { Endpoints = [first, first] }, asset, 0));
        Assert.That(duplicate!.Failure, Is.EqualTo(DataPreviewSelectionFailure.Endpoint));
    }

    private static DataEndpointReadRequest Request(DataSchema? schema = null) =>
        new(new("endpoint", null, EndpointKind.Inline, null, new InlineLocation(null)), schema);

    private static IDataEndpointReaderFactory Factory(IDataReader reader)
    {
        var factory = new Mock<IDataEndpointReaderFactory>();
        factory.Setup(value => value.OpenAsync(It.IsAny<DataEndpointReadRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(reader);
        return factory.Object;
    }
}
