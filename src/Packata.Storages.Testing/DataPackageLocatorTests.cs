using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Packata.Core.Storage;

namespace Packata.Storages.Testing;

public class DataPackageLocatorTests
{
    [Test]
    public async Task LocateAsync_CheckIfExists_Success()
    {
        var container = new Mock<IDataPackageContainer>();
        container.Setup(c => c.ExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        var locator = new DataPackageLocator(
            new Dictionary<string, Func<Uri, IDataPackageContainer>> { { "mock", (uri) => container.Object } },
            new Dictionary<string, Func<Uri, IContainerWrapper>>()
            );
        var handle = await locator.LocateAsync(new Uri("mock://foo"));
        Assert.DoesNotThrowAsync(handle.ValidateAsync);
        container.Verify(c => c.ExistsAsync("datapackage.json"), Times.Once);
    }

    [Test]
    public async Task LocateAsync_CheckIfExistsExplicitDescriptor_Success()
    {
        var container = new Mock<IDataPackageContainer>();
        container.Setup(c => c.ExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        var locator = new DataPackageLocator(
            new Dictionary<string, Func<Uri, IDataPackageContainer>> { { "mock", (uri) => container.Object } },
            new Dictionary<string, Func<Uri, IContainerWrapper>>()
            );
        var handle = await locator.LocateAsync(new Uri("mock://foo"), "datapackage.yaml");
        Assert.DoesNotThrowAsync(handle.ValidateAsync);
        container.Verify(c => c.ExistsAsync("datapackage.yaml"), Times.Once);
    }

    [Test]
    public async Task LocateAsync_NotExisting_Failure()
    {
        var container = new Mock<IDataPackageContainer>();
        container.Setup(c => c.ExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        var locator = new DataPackageLocator(
            new Dictionary<string, Func<Uri, IDataPackageContainer>> { { "mock", (uri) => container.Object } },
            new Dictionary<string, Func<Uri, IContainerWrapper>>()
            );
        var handle = await locator.LocateAsync(new Uri("mock://foo"));
        Assert.ThrowsAsync<FileNotFoundException>(handle.ValidateAsync);
        container.Verify(c => c.ExistsAsync("datapackage.json"), Times.Once);
    }
}
