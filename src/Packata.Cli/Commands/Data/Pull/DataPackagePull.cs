using Packata.Application;
using YamlDotNet.RepresentationModel;

namespace Packata.Cli;

internal static class DataPackagePull
{
    private static readonly char[] InvalidFileNameCharacters = [.. Path.GetInvalidFileNameChars(), '/', '\\', ':'];

    public static async Task<int> RunAsync(HttpClient client, Uri source, string outputDirectory, CancellationToken cancellationToken)
    {
        using var descriptor = await client.GetAsync(source, cancellationToken).ConfigureAwait(false);
        descriptor.EnsureSuccessStatusCode();
        var descriptorUri = descriptor.RequestMessage?.RequestUri ?? source;
        var syntax = ContractSyntax.Parse(await descriptor.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (syntax.Format != "datapackage" ||
            Packata.Application.ContractDocument.Child(syntax.Root, "resources") is not YamlSequenceNode)
            throw new ContractOperationException("CLI050", "$", "Expected a Data Package descriptor with a resources list.");
        var document = Packata.Application.ContractDocument.Parse(syntax, baseUri: new Uri(descriptorUri, "."));
        var package = (Packata.DataPackage.DataPackage)document.NativeDocument;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        var downloads = Plan(package, descriptorUri, root);
        foreach (var download in downloads)
            await DownloadAsync(client, root, download, cancellationToken).ConfigureAwait(false);
        return downloads.Count;
    }

    private static List<Download> Plan(Packata.DataPackage.DataPackage package, Uri descriptorUri, string root)
    {
        var comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var files = new HashSet<string>(comparer);
        var directories = new HashSet<string>(comparer);
        var downloads = package.Resources.SelectMany((resource, resourceIndex) =>
            resource.Paths.Select((path, pathIndex) =>
                Resolve(path.Value, $"$.resources[{resourceIndex}].path[{pathIndex}]", descriptorUri, root))).ToList();
        foreach (var download in downloads)
        {
            if (!files.Add(download.Destination) || directories.Contains(download.Destination))
                throw Conflict(download);
            foreach (var directory in ParentDirectories(root, download.RelativePath))
            {
                if (files.Contains(directory)) throw Conflict(download);
                directories.Add(directory);
            }
            CheckDestination(root, download);
        }
        return downloads;
    }

    private static Download Resolve(string path, string location, Uri descriptorUri, string root)
    {
        var (source, urlPath) = ResolveResourceUrl(path, location, descriptorUri);
        var relativePath = ResolveDestinationPath(urlPath, location);
        return new Download(source, location, relativePath, Path.Combine(root, relativePath));
    }

    private static (Uri Source, string UrlPath) ResolveResourceUrl(string path, string location, Uri descriptorUri)
    {
        if (path != path.Trim() || path.StartsWith('/')) throw UnsupportedResourcePath(location);
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri))
            return (new Uri(descriptorUri, path), path.Split(['?', '#'], 2)[0]);
        if (uri.Scheme is not ("http" or "https")) throw UnsupportedResourcePath(location);
        return (uri, uri.Segments[^1]);
    }

    private static ContractOperationException UnsupportedResourcePath(string location)
        => new("CLI050", location, "Resource paths must be relative paths or absolute HTTP(S) URLs.");

    private static string ResolveDestinationPath(string urlPath, string location)
    {
        var segments = urlPath.Split('/').Select(Uri.UnescapeDataString).ToArray();
        var parts = segments.Where(part => part is not ("" or ".")).ToArray();
        if (segments[^1] is "" or "." || parts.Any(part => !IsFileName(part)))
            throw new ContractOperationException("CLI050", location, "Resource path does not name a safe destination file.");
        return Path.Combine(parts);
    }

    private static bool IsFileName(string part)
    {
        if (part == ".." || part.IndexOfAny(InvalidFileNameCharacters) >= 0 || part.Any(char.IsControl))
            return false;
        return !OperatingSystem.IsWindows() || IsWindowsFileName(part);
    }

    private static bool IsWindowsFileName(string part)
    {
        if (part.EndsWith('.') || part.EndsWith(' ')) return false;
        var stem = part.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$") return false;
        return stem.Length != 4 || !IsWindowsPort(stem);
    }

    private static bool IsWindowsPort(string stem)
        => (stem[..3] is "COM" or "LPT") && "0123456789¹²³".Contains(stem[3]);

    private static IEnumerable<string> ParentDirectories(string root, string relativePath)
    {
        for (var directory = Path.GetDirectoryName(relativePath); !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
            yield return Path.Combine(root, directory);
        yield return root;
    }

    private static void CheckDestination(string root, Download download)
    {
        foreach (var directory in ParentDirectories(root, download.RelativePath).Reverse())
        {
            if (Attributes(directory) is { } attributes &&
                (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
                throw Conflict(download);
        }
        if (Attributes(download.Destination) is not null) throw Conflict(download);
    }

    private static FileAttributes? Attributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static ContractOperationException Conflict(Download download)
        => new("CLI051", download.Location, $"Destination conflict for resource file '{download.RelativePath}'.");

    private static async Task DownloadAsync(HttpClient client, string root, Download download, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(download.Source, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var parent = Path.GetDirectoryName(download.Destination)!;
            CheckDestination(root, download);
            Directory.CreateDirectory(parent);
            await SaveAsync(response, root, download, parent, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException) when (Attributes(download.Destination) is not null)
        {
            throw Conflict(download);
        }
        catch (Exception error) when (error is IOException or HttpRequestException or UnauthorizedAccessException or OperationCanceledException)
        {
            var message = error is HttpRequestException { StatusCode: { } status }
                ? $"HTTP {(int)status}." : CommandOutput.Failure(error).Message;
            throw new TransferException(download.Location, $"Unable to download resource file '{download.RelativePath}'. {message}", error);
        }
    }

    private static async Task SaveAsync(HttpResponseMessage response, string root, Download download, string parent, CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(parent, $".packata-pull-{Guid.NewGuid():N}.tmp");
        var created = false;
        try
        {
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                created = true;
                await response.Content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            CheckDestination(root, download);
            File.Move(temporaryPath, download.Destination);
            created = false;
        }
        finally
        {
            if (created) File.Delete(temporaryPath);
        }
    }

    private sealed record Download(Uri Source, string Location, string RelativePath, string Destination);

    internal sealed class TransferException(string location, string message, Exception innerException) : Exception(message, innerException)
    {
        public string Location { get; } = location;
    }
}
