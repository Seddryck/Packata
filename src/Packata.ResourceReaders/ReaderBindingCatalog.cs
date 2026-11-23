using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace Packata.ResourceReaders;

internal sealed class ReaderBindingCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Lazy<Contribution[]> _contributions;

    internal ReaderBindingCatalog(string directory)
        => _contributions = new(() => ReadManifests(directory));

    internal static ReaderBindingCatalog ForApplication()
        => new(Path.Combine(AppContext.BaseDirectory, "Packata.ReaderBindings"));

    internal IReadOnlyList<IReaderBindingProvider> Load(string readerKey, IReadOnlyList<ReaderRepresentationProfile> profiles)
    {
        var candidates = _contributions.Value.Where(contribution => contribution.ReaderKey == readerKey
            && (contribution.ProfileName is null || profiles.Any(profile => profile.Name == contribution.ProfileName)))
            .ToArray();
        foreach (var group in candidates.GroupBy(candidate => candidate.Id, StringComparer.Ordinal))
        {
            if (group.Skip(1).Any())
            {
                var manifests = string.Join(", ", group.Select(candidate => candidate.Manifest));
                throw new InvalidOperationException($"Reader '{readerKey}' has duplicate binding contribution '{group.Key}' in {manifests}.");
            }
        }

        return candidates.Select(CreateProvider).ToArray();
    }

    private static Contribution[] ReadManifests(string directory)
    {
        if (!Directory.Exists(directory)) return [];

        return Directory.EnumerateFiles(directory, "*.readerbindings.json").Order(StringComparer.Ordinal)
            .SelectMany(ReadManifest).ToArray();
    }

    private static Contribution[] ReadManifest(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            var entries = JsonSerializer.Deserialize<Contribution[]>(file, JsonOptions);
            if (entries is null || entries.Any(entry => entry is null || string.IsNullOrWhiteSpace(entry.Id)
                || string.IsNullOrWhiteSpace(entry.ReaderKey) || string.IsNullOrWhiteSpace(entry.AssemblyName)
                || string.IsNullOrWhiteSpace(entry.ProviderType)))
                throw new JsonException("Every binding contribution requires id, readerKey, assemblyName, and providerType.");

            return entries.Select(entry => entry with { Manifest = Path.GetFileName(path) }).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException($"Unable to read reader binding manifest '{path}'. {error.Message}", error);
        }
    }

    private static IReaderBindingProvider CreateProvider(Contribution contribution)
    {
        try
        {
            var loadContext = AssemblyLoadContext.GetLoadContext(typeof(ReaderBindingCatalog).Assembly)
                ?? AssemblyLoadContext.Default;
            var assembly = loadContext.LoadFromAssemblyName(new AssemblyName(contribution.AssemblyName));
            var type = assembly.GetType(contribution.ProviderType, throwOnError: true)!;
            if (!typeof(IReaderBindingProvider).IsAssignableFrom(type))
                throw new InvalidOperationException($"'{type.FullName}' must implement {nameof(IReaderBindingProvider)}.");
            return (IReaderBindingProvider)Activator.CreateInstance(type)!;
        }
        catch (Exception error) when (error is IOException or BadImageFormatException or TypeLoadException
            or MissingMethodException or TargetInvocationException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidOperationException(
                $"Cannot load reader binding '{contribution.Id}' for '{contribution.ReaderKey}' from '{contribution.Manifest}'. "
                + $"Check that assembly '{contribution.AssemblyName}' and its dependencies are installed. {error.Message}", error);
        }
    }

    private sealed record Contribution(string Id, string ReaderKey, string? ProfileName, string AssemblyName, string ProviderType)
    {
        internal string Manifest { get; init; } = string.Empty;
    }
}
