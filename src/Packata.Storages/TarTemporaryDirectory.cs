using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Packata.Storages;

internal static class TarTemporaryDirectory
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    internal static string Resolve(string? configuredPath)
    {
        var directory = configuredPath is null ? CreateDefault() : new DirectoryInfo(configuredPath);
        if (!directory.Exists) throw new DirectoryNotFoundException("The TAR spill directory must already exist.");
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("The TAR spill directory must not be a symbolic link or reparse point.");
        if (OperatingSystem.IsWindows()) ValidateWindowsAccess(directory);
        else if ((File.GetUnixFileMode(directory.FullName) & ~OwnerOnly) != 0)
            throw new UnauthorizedAccessException("The TAR spill directory must be accessible only to its owner (Unix mode 0700).");
        return directory.FullName;
    }

    private static DirectoryInfo CreateDefault()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
            throw new InvalidOperationException("Configure a private TAR spill directory when local application data is unavailable.");
        var path = Path.Combine(localData, "Packata", "archives");
        return CreatePrivateDirectory(path);
    }

    internal static DirectoryInfo CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) return CreateWindowsDirectory(path);
        return Directory.CreateDirectory(path, OwnerOnly);
    }

    [SupportedOSPlatform("windows")]
    private static DirectoryInfo CreateWindowsDirectory(string path)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new UnauthorizedAccessException("The current user has no security identifier.");
        var security = new DirectorySecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        // Apply the ACL at creation, without an intermediate publicly accessible directory.
        return security.CreateDirectory(path);
    }

    [SupportedOSPlatform("windows")]
    private static void ValidateWindowsAccess(DirectoryInfo directory)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var trusted = new[] { identity.User, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) };
        var rules = directory.GetAccessControl().GetAccessRules(includeExplicit: true, includeInherited: true,
            targetType: typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            if ((rule.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;
            if (rule.AccessControlType == AccessControlType.Allow && !trusted.Contains(rule.IdentityReference))
                throw new UnauthorizedAccessException("The TAR spill directory grants access to other users. Configure a private directory.");
        }
    }
}
