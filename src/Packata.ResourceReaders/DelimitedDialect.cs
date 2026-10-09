using Packata.Core.Reading;

namespace Packata.ResourceReaders;

/// <summary>Parsing settings for a delimited text resource.</summary>
public sealed record DelimitedDialect(
    char? Delimiter = null,
    string? LineTerminator = null,
    bool? Header = null,
    char? QuoteChar = null);

/// <summary>Information available while resolving a delimited-text dialect.</summary>
public sealed record DelimitedDialectContext(
    DataEndpointReadRequest Request,
    ResolvedDataFormat Format);

/// <summary>Resolves a delimited-text dialect from canonical metadata or an external source.</summary>
public interface IDelimitedDialectResolver
{
    /// <summary>Returns a dialect, or <see langword="null"/> when this resolver has no applicable metadata.</summary>
    ValueTask<DelimitedDialect?> ResolveAsync(
        DelimitedDialectContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Configures resolvers evaluated before the built-in format-options dialect resolver.</summary>
public sealed class DelimitedReaderOptions
{
    private readonly List<IDelimitedDialectResolver> _dialectResolvers = [];

    public DelimitedReaderOptions AddDialectResolver(IDelimitedDialectResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _dialectResolvers.Add(resolver);
        return this;
    }

    internal IReadOnlyList<IDelimitedDialectResolver> DialectResolvers => _dialectResolvers;
}
