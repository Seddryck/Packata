using Packata.Core.Contracts;

namespace Packata.Application;

/// <summary>A parsed descriptor and its canonical projection, before host presentation.</summary>
public sealed record ContractInspection(ContractDocument Document, MappingResult<DataContract> Mapping);

/// <summary>Inspects descriptor declarations without acquiring or reading resource data.</summary>
public static class ContractInspector
{
    public static ContractInspection Inspect(string text, string format = "auto", Uri? baseUri = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Inspect(ContractSyntax.Parse(text), format, baseUri, cancellationToken);
    }

    public static ContractInspection Inspect(ContractSyntax syntax, string format = "auto", Uri? baseUri = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Inspect(ContractDocument.Parse(syntax, format, baseUri), cancellationToken);
    }

    public static ContractInspection Inspect(ContractDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mapped = document.Map();
        cancellationToken.ThrowIfCancellationRequested();
        return new(document, mapped);
    }
}
