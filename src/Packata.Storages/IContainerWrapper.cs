using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Storages;
public interface IContainerWrapper
{
    /// <summary>
    /// Opens the archive identified by <paramref name="uri"/> from its parent container.
    /// The caller retains ownership of the parent container; the returned container owns its archive streams.
    /// </summary>
    Task<Packata.Core.Storage.IDocumentContainer> WrapAsync(
        Packata.Core.Storage.IDocumentContainer baseContainer, Uri uri);
}
