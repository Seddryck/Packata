using Packata.Application;
using YamlDotNet.RepresentationModel;

namespace Packata.Cli;

internal static class DataPackagePull
{
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
        var downloads = new List<Download>();
        for (var resourceIndex = 0; resourceIndex < package.Resources.Count; resourceIndex++)
        {
            var resource = package.Resources[resourceIndex];
            for (var pathIndex = 0; pathIndex < resource.Paths.Count; pathIndex++)
            {
                var location = $"$.resources[{resourceIndex}].path[{pathIndex}]";
                var download = Resolve(resource.Paths[pathIndex].Value, location, descriptorUri, root);
                if (!files.Add(download.Destination) || directories.Contains(download.Destination))
                    throw Conflict(download);
                foreach (var directory in ParentDirectories(root, download.RelativePath))
                {
                    if (files.Contains(directory)) throw Conflict(download);
                    directories.Add(directory);
                }
                CheckDestination(root, download);
                downloads.Add(download);
            }
        }
        return downloads;
    }

    private static Download Resolve(string path, string location, Uri descriptorUri, string root)
    {
        var absolute = Uri.TryCreate(path, UriKind.Absolute, out var uri);
        if (path != path.Trim() || (absolute && uri!.Scheme is not ("http" or "https")) || path.StartsWith('/'))
            throw new ContractOperationException("CLI050", location, "Resource paths must be relative paths or absolute HTTP(S) URLs.");

        var urlPath = absolute ? uri!.Segments[^1] : path.Split('?', '#')[0];
        var segments = urlPath.Split('/').Select(Uri.UnescapeDataString).ToArray();
        var parts = segments.Where(part => part is not ("" or ".")).ToArray();
        if (segments[^1] is "" or "." || parts.Any(part => !IsFileName(part)))
            throw new ContractOperationException("CLI050", location, "Resource path does not name a safe destination file.");
        var relativePath = Path.Combine(parts);
        return new Download(absolute ? uri! : new Uri(descriptorUri, path), location, relativePath, Path.Combine(root, relativePath));
    }

    private static bool IsFileName(string part)
    {
        if (part.Length == 0 || part == ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            part.Contains('/') || part.Contains('\\') || part.Contains(':') || part.Any(char.IsControl))
            return false;
        if (!OperatingSystem.IsWindows()) return true;
        var stem = part.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return !part.EndsWith('.') && !part.EndsWith(' ') &&
            stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$") &&
            !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
              "0123456789¹²³".Contains(stem[3]));
    }

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
