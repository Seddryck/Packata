using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Storage;
public interface IStorageProvider
{
    bool CanHandle(string absolutePath);
    Task<Stream> OpenAsync(string absolutePath);
    Task<bool> ExistsAsync(string absolutePath);
}
