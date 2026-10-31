using System.Text;

namespace Packata.Cli;

internal static class ContractDocumentWriter
{
    public static void Write(string path, string content, bool overwrite)
    {
        Write(path, Encoding.UTF8.GetBytes(content), overwrite);
    }

    public static void Write(string path, byte[] content, bool overwrite)
    {
        var fullPath = Path.GetFullPath(path);
        if (!overwrite && File.Exists(fullPath))
            throw new IOException($"Output file '{fullPath}' already exists.");

        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, content);
            File.Move(temporary, fullPath, overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
