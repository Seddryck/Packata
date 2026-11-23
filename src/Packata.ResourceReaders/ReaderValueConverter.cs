using Packata.Core.Contracts;

namespace Packata.ResourceReaders;

internal sealed record ReaderValueConverter(Type SourceType, Type TargetType, Type OutputType, Func<object, object?> Convert,
    string? LogicalType, string? Format, SpatialEncoding? Encoding)
{
    internal bool Matches(ReaderRepresentationRequest request) =>
        RepresentationType.Same(TargetType, request.RuntimeType)
        && (LogicalType is null || string.Equals(LogicalType, request.Field.LogicalType?.Trim(), StringComparison.OrdinalIgnoreCase))
        && (Format is null || string.Equals(Format, request.Format, StringComparison.Ordinal))
        && (Encoding is null || Encoding == request.Encoding);
}
