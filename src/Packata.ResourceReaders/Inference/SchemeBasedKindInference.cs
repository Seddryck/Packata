using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;
using Packata.Core.Storage;

namespace Packata.ResourceReaders.Inference;
internal class SchemeBasedKindInference : IKindInference
{
    private readonly Func<string, bool> _remoteSchemes;
    private readonly Func<string, bool> _serviceSchemes;

    public SchemeBasedKindInference(Func<string, bool> remoteSchemes, Func<string, bool> serviceSchemes)
        => (_remoteSchemes, _serviceSchemes) = (remoteSchemes, serviceSchemes);

    public bool TryInfer(Resource resource, [NotNullWhen(true)] out string? kind)
    {
        kind = null;
        if(!resource.Paths.Any())
        {
            if (resource.Data is null)
            {
                kind = "virtual";
                return true;
            }
            return false;
        }

        if (resource.Paths.First() is ContainerPath relativePath)
        {
                kind = "local";
                return true;
            
        }

        if (resource.Paths.First() is FullyQualifiedPath absolutePath)
        {
            var uri = new Uri(absolutePath.Value);
            if (_remoteSchemes(uri.Scheme))
            {
                kind = "remote";
                return true;
            }

            if (_serviceSchemes(uri.Scheme))
            {
                kind = "service";
                return true;
            }
        }
        return false;
    }
}
