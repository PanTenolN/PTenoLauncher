using System.Security.Cryptography;
using System.Text;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Security;

public sealed class SecretVault(LauncherPaths paths)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PTenoLauncher/secrets/v1");
    public byte[]? Read(string name)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Для секретів потрібен Windows DPAPI.");
        var path = SafePath.Join(paths.Secrets, name + ".dpapi");
        if (!File.Exists(path)) return null;
        return ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
    }
    public void Write(string name, byte[] bytes)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Для секретів потрібен Windows DPAPI.");
        var path = SafePath.Join(paths.Secrets, name + ".dpapi");
        // MSAL callbacks and account saves can run on a UI synchronization context.
        // A synchronous callback must not wait for an async write that posts back to that context.
        AtomicFile.Write(path, ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser));
    }
    public void Delete(string name)
    {
        var path = SafePath.Join(paths.Secrets, name + ".dpapi");
        if (File.Exists(path)) File.Delete(path);
    }
}
