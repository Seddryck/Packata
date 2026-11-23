using System.Collections.ObjectModel;
using System.Data;
using Packata.Core;
using Packata.Core.Contracts;

namespace Packata.ResourceReaders;

internal sealed class ReaderBindingPlan(IDataEndpointReaderProvider reader)
{
    private readonly Dictionary<int, ResolvedField> _fields = [];
    internal IReadOnlyDictionary<int, ReaderFieldBinding> NativeBindings =>
        _fields.Count == 0 ? ReadOnlyDictionary<int, ReaderFieldBinding>.Empty
            : new ReadOnlyDictionary<int, ReaderFieldBinding>(_fields.ToDictionary(entry => entry.Key, entry => entry.Value.Binding));

    internal bool HasBindings => _fields.Count > 0;

    internal string? GetUnconvertedText(IDataRecord row, int ordinal)
        => reader.GetUnconvertedText(row, ordinal);

    internal bool TryGetField(int ordinal, out ResolvedField? field) => _fields.TryGetValue(ordinal, out field);

    internal static ReaderBindingPlan Create(IDataEndpointReaderProvider reader, DataSchema? schema,
        IReadOnlyList<ReaderRepresentationProfile> profiles, IReadOnlyList<ReaderValueConverter> converters,
        ReaderBindingCatalog catalog)
    {
        var plan = new ReaderBindingPlan(reader);
        if (schema is null) return plan;

        var providers = new Lazy<IReadOnlyList<IReaderBindingProvider>>(() =>
        {
            var discovered = catalog.Load(reader.BindingKey, profiles);
            return reader is IReaderBindingProvider native ? [native, .. discovered] : discovered;
        });
        for (var ordinal = 0; ordinal < schema.Fields.Count; ordinal++)
        {
            var field = schema.Fields[ordinal];
            var selection = ReaderRepresentationProfile.Resolve(profiles, field);
            var target = selection?.Type ?? reader.GetDefaultRepresentation(field);
            if (target is null && converters.Count == 0) continue;

            var missing = ReaderMissingValues.Resolve(schema, field);
            if (target is null && converters.Count > 0)
            {
                var resolved = FieldTypeResolver.Resolve(field, TypeMappingOptions.Strict).RuntimeType;
                if (resolved is not null && converters.Any(converter => converter.Matches(new(field, resolved, null, missing))))
                    target = resolved;
            }

            if (target is not null)
            {
                var request = new ReaderRepresentationRequest(field, target, selection?.ProfileName, missing);
                plan._fields.Add(ordinal, Resolve(reader.BindingKey, request, providers.Value, converters));
            }
        }

        return plan;
    }

    private static ResolvedField Resolve(string key, ReaderRepresentationRequest request,
        IReadOnlyList<IReaderBindingProvider> providers, IReadOnlyList<ReaderValueConverter> converters)
    {
        if (FindNative(key, request, providers) is { } native)
            return new(request, native, null);

        ResolvedField? result = null;
        foreach (var converter in converters.Where(candidate => candidate.Matches(request)))
        {
            var sourceRequest = request with { RuntimeType = converter.SourceType };
            if (FindNative(key, sourceRequest, providers) is not { } source) continue;

            if (!RepresentationType.Same(converter.OutputType, request.RuntimeType))
            {
                throw new InvalidOperationException(
                    $"Converter for {Describe(key, request)} declares output '{converter.OutputType.FullName}'.");
            }
            if (result is not null)
                throw new InvalidOperationException($"Multiple fallback converters can bind {Describe(key, request)}.");

            result = new(request, source, converter);
        }

        return result ?? throw new NotSupportedException(
            $"No binding for {Describe(key, request)}. Install a reader integration or configure a converter from a type supported by this reader.");
    }

    private static ReaderFieldBinding? FindNative(string key, ReaderRepresentationRequest request,
        IReadOnlyList<IReaderBindingProvider> providers)
    {
        ReaderFieldBinding? result = null;
        foreach (var provider in providers)
        {
            ReaderFieldBinding? binding;
            try
            {
                binding = provider.TryBind(request);
            }
            catch (Exception error) when (error is IOException or TypeLoadException or TypeInitializationException)
            {
                throw new InvalidOperationException(
                    $"Cannot use binding '{provider.GetType().FullName}' for {Describe(key, request)}. Check its installed dependencies.",
                    error);
            }

            if (binding is null) continue;
            if (!RepresentationType.Same(binding.RuntimeType, request.RuntimeType))
            {
                throw new InvalidOperationException(
                    $"Binding '{provider.GetType().FullName}' for {Describe(key, request)} declares '{binding.RuntimeType.FullName}'.");
            }
            if (result is not null)
                throw new InvalidOperationException($"Multiple native bindings match {Describe(key, request)}.");

            result = binding;
        }

        return result;
    }

    private static string Describe(string key, ReaderRepresentationRequest request) =>
        $"reader '{key}', field '{request.Field.Name}' as '{request.RuntimeType.FullName}'"
        + (request.ProfileName is { } profile ? $" from '{profile}'" : string.Empty);

    internal sealed record ResolvedField(ReaderRepresentationRequest Request, ReaderFieldBinding Binding, ReaderValueConverter? Converter);
}
