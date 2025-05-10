using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Packata.Core;
using Packata.ResourceReaders.Inference;
using Packata.Core.Storage;
using Moq;

namespace Packata.ResourceReaders.Testing.Inference;
public class SchemeBasedKindInferenceTests
{
    private static readonly string[] _remoteArray = ["http", "https"];
    private static readonly string[] _serviceArray = ["mssql", "pgsql"];

    [Test]
    [TestCase("data/file.csv", "local")]
    [TestCase("file.csv", "local")]
    [TestCase("http://www.data.org/file.csv", "remote")]
    [TestCase("mssql://192.168.16.45/db", "service")]
    [TestCase(null, "virtual")]
    public void TryInfer_Path_Success(string? path, string expected)
    {
        bool remote(string value) => (_remoteArray).Any(x => x.Equals(value, StringComparison.OrdinalIgnoreCase));
        bool service(string value) => (_serviceArray).Any(x => x.Equals(value, StringComparison.OrdinalIgnoreCase));
        var inference = new SchemeBasedKindInference(remote, service);

        var factory = new PathFactory(Mock.Of<IDataPackageContainer>(), Mock.Of<IStorageProvider>());
        var resource = path  is null
            ? new Resource()
            : new Resource() { Paths = [factory.Create(path)] };

        Assert.That(inference.TryInfer(resource, out var result), Is.True);
        Assert.That(result, Is.EqualTo(expected));
    }
}
