using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.ResourceReaders.Inference;
/// <summary>
/// Interface for extracting file extensions from paths.
/// </summary>
public interface IExtractExtension
{
    /// <summary>
    /// Attempts to extract a common file extension from the provided paths.
    /// </summary>
    /// <param name="paths">The array of paths to extract from, may be null.</param>
    /// <param name="extension">When this method returns, contains the extracted extension without the leading dot if successful, or null if unsuccessful.</param>
    /// <returns>true if a common extension was successfully extracted; otherwise, false.</returns>
    bool TryGetPathExtension(IPath[]? paths, out string? extension);
}
