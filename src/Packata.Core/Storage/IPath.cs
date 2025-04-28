using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Storage;
public interface IPath
{
    string Value { get; }
    bool IsFullyQualified { get; }

    Task<Stream> OpenAsync();
    Task<bool> ExistsAsync();
}
