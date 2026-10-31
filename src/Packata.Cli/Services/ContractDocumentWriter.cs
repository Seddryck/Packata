using System.Text;

namespace Packata.Cli;

internal static class ContractDocumentWriter
{
    public static void WriteBytes(string path, byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath)) throw new IOException("Output already exists.");
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Write(string path, string content, bool overwrite)
    {
        var fullPath = Path.GetFullPath(path);
        if (!overwrite && File.Exists(fullPath))
            throw new IOException($"Output file '{fullPath}' already exists.");

        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, fullPath, overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
