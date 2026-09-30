using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Core.Testing;

public class ArchitectureTests
{
    [Test]
    public void Core_does_not_reference_native_formats_or_serializers()
    {
        var references = typeof(DataContract).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.That(references, Does.Not.Contain("Packata.DataPackage"));
        Assert.That(references, Does.Not.Contain("Packata.OpenDataContract"));
        Assert.That(references, Does.Not.Contain("Newtonsoft.Json"));
        Assert.That(references, Does.Not.Contain("YamlDotNet"));
    }

    [Test]
    public void Core_does_not_export_legacy_Data_Package_document_types()
    {
        var exported = typeof(DataContract).Assembly.GetExportedTypes().Select(x => x.FullName).ToArray();
        Assert.That(exported, Does.Not.Contain("Packata.Core.DataPackage"));
        Assert.That(exported, Does.Not.Contain("Packata.Core.Resource"));
        Assert.That(exported, Does.Not.Contain("Packata.Core.Schema"));
        Assert.That(exported, Does.Not.Contain("Packata.Core.Field"));
    }
}
