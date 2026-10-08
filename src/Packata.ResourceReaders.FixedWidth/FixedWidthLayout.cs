using Packata.Core.Reading;

namespace Packata.ResourceReaders.FixedWidth;

/// <summary>Resolved zero-based field offsets and character widths for a fixed-width record.</summary>
public sealed record FixedWidthLayout(
    IReadOnlyList<int> Offsets,
    IReadOnlyList<int> Widths,
    int? RecordWidth = null);

/// <summary>Information available while resolving a fixed-width layout.</summary>
public sealed record FixedWidthLayoutContext(
    DataEndpointReadRequest Request,
    ResolvedDataFormat Format);

/// <summary>Resolves a fixed-width layout from canonical metadata or an external source.</summary>
public interface IFixedWidthLayoutResolver
{
    /// <summary>Returns a layout, or <see langword="null"/> when this resolver has no applicable metadata.</summary>
    ValueTask<FixedWidthLayout?> ResolveAsync(
        FixedWidthLayoutContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Configures fixed-width layout resolvers evaluated before the built-in format-options resolver.</summary>
public sealed class FixedWidthReaderOptions
{
    private readonly List<IFixedWidthLayoutResolver> _layoutResolvers = [];

    public FixedWidthReaderOptions AddLayoutResolver(IFixedWidthLayoutResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _layoutResolvers.Add(resolver);
        return this;
    }

    internal IReadOnlyList<IFixedWidthLayoutResolver> LayoutResolvers => _layoutResolvers;
}
