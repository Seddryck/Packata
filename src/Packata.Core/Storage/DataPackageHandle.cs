using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Storage;
public class DocumentHandle
{
    public string DescriptorPath { get; }
    public IDocumentContainer Container { get; }

    public DocumentHandle(IDocumentContainer container, string descriptorPath)
        => (Container, DescriptorPath) = (container, descriptorPath);

    public async Task ValidateAsync()
    {
        if (!await Container.ExistsAsync(DescriptorPath))
            throw new FileNotFoundException($"Unable to find '{DescriptorPath}' in container: {Container.BaseUri}");
    }
}
