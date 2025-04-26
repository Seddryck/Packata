using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Storage;
public class DataPackageHandle
{
    public string DescriptorPath { get; }
    public IDataPackageContainer Container { get; }

    public DataPackageHandle(IDataPackageContainer container, string descriptorPath)
        => (Container, DescriptorPath) = (container, descriptorPath);

    public async Task ValidateAsync()
    {
        if (!await Container.ExistsAsync(DescriptorPath))
            throw new FileNotFoundException($"Unable to find '{DescriptorPath}' in container: {Container.BaseUri}");
    }
}
