using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Storage;
public interface IStorageHandler
{
    Task<bool> ExistsAsync(string path);
    Task<Stream> OpenAsync(string path);
}
