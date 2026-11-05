using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Infrastructure.Storage;
using PTenoLauncher.Infrastructure.Updates;
using Xunit;
namespace PTenoLauncher.Tests;

public sealed class InnoUpdaterTests
{
    [Fact] public async Task ShortLivedControlledProcessStillRecordsExactIdentityAndExit()
    {
        var source = new DirectoryInfo(AppContext.BaseDirectory);
        while (source is not null && !File.Exists(Path.Combine(source.FullName, "PTenoLauncher.sln"))) source = source.Parent;
        Assert.NotNull(source);
        var exe = Path.Combine(source.FullName, ".tools", "dotnet", "dotnet.exe");
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(Path.Combine(source.FullName, "tests", "PTenoLauncher.IntegrationSmoke", "bin", "Release", "net10.0", "PTenoLauncher.IntegrationSmoke.dll"));
        info.ArgumentList.Add("--argument-probe"); UpdateProcessIdentity? identity = null;
        var code = await new InnoUpdateProcess().RunAsync(info, id => { identity = id; return Task.CompletedTask; });
        Assert.Equal(0, code); Assert.NotNull(identity); Assert.True(identity.Pid > 0); Assert.True(identity.StartTicks > 0); Assert.Equal(Path.GetFullPath(exe), identity.Executable);
    }
    private sealed class Registration(string root, string data) : IInstalledUpdateRegistry
    {
        public bool Denied; public string Version = "0.2.0"; public int Restores;
        public InstalledRegistration ReadOwned(string applicationRoot, string dataRoot)
        {
            if (Denied || applicationRoot != root || dataRoot != data) throw new LauncherUpdateException("UpdaterInstalledBlocked");
            return new(root, data, "unins000.exe", Version, [new("app", "UninstallDataRoot", "String", data), new("uninstall", "DisplayVersion", "String", Version)]);
        }
        public void RestoreOwned(InstalledRegistration before) { Restores++; Version = before.Version; }
    }
    private sealed class Installer(LauncherUpdaterTests.Fixture fixture, LauncherUpdateOffer offer, Registration registration, int code) : IInnoUpdateProcess
    {
        public ProcessStartInfo? Command;
        public async Task<int> RunAsync(ProcessStartInfo command, Func<UpdateProcessIdentity, Task> started)
        {
            Command = command; await started(new(int.MaxValue, 1, command.FileName)); // Synthetic exited process descriptor; no test JAR/EXE is executed.
            using var zip = new ZipArchive(new MemoryStream(fixture.Package));
            foreach (var e in zip.Entries)
            {
                var target = SafePath.Join(fixture.AppRoot, e.FullName); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var s = e.Open(); await using var output = File.Create(target); await s.CopyToAsync(output);
            }
            await AtomicFile.WriteJsonAsync(SafePath.Join(fixture.AppRoot, LauncherUpdater.InstallationManifest), new ManagedLauncherInstallation(offer.Release.Build, offer.Package.Files));
            await File.WriteAllTextAsync(Path.Combine(fixture.AppRoot, "unins000.exe"), "new synthetic uninstaller");
            await File.WriteAllTextAsync(Path.Combine(fixture.AppRoot, "unins000.dat"), "new synthetic uninstall log");
            registration.Version = offer.Release.Build.Version; return code;
        }
    }
    private static async Task<(LauncherUpdateOffer Offer, Registration Registry)> Prepare(LauncherUpdaterTests.Fixture f)
    {
        File.Delete(Path.Combine(f.AppRoot, "portable.flag"));
        await File.WriteAllTextAsync(Path.Combine(f.AppRoot, "unins000.exe"), "old synthetic uninstaller");
        await File.WriteAllTextAsync(Path.Combine(f.AppRoot, "unins000.dat"), "old synthetic uninstall log");
        var original = await f.Offer(); var hash = Convert.ToHexString(SHA256.HashData(f.Package)).ToLowerInvariant();
        var package = original.Package with { Mode = "Installed", InstallerUrl = new("https://github.com/PanTenolN/PTenoLauncher/releases/download/test/setup.exe"), InstallerSize = f.Package.Length, InstallerSha256 = hash, InstallerContract = "inno-single-process-v1" };
        var release = original.Release with { Build = original.Release.Build with { Version = "0.2.1" }, Packages = [package] };
        var signed = f.Sign(new(1, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1), [release]));
        return (new(f.Current, release, package, signed), new(f.AppRoot, f.Paths.Root));
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(4)] [InlineData(5)] [InlineData(8)] [InlineData(3010)] [InlineData(42)]
    public async Task SeparateInnoAdapterValidatesExitOutputsAndRestoresFilesUninstallerAndRegistry(int exitCode)
    {
        using var f = new LauncherUpdaterTests.Fixture(); var (offer, registry) = await Prepare(f); var process = new Installer(f, offer, registry, exitCode);
        f.Updater.InstalledAdapterOverride = new(registry, process);
        var t = await f.Updater.PrepareAsync(offer, f.AppRoot, true);
        if (exitCode == 0) { await f.Updater.ApplyPreparedAsync(t.Id); Assert.Equal("AwaitingHealth", (await f.Updater.ReadTransactionAsync(t.Id)).State); }
        else { var error = await Assert.ThrowsAsync<LauncherUpdateException>(() => f.Updater.ApplyPreparedAsync(t.Id)); Assert.Equal("UpdaterInstallerFailed", error.Code); }
        Assert.False(process.Command!.UseShellExecute); Assert.Contains("/NOCLOSEAPPLICATIONS", process.Command.ArgumentList); Assert.Contains("/NORESTART", process.Command.ArgumentList);
        Assert.DoesNotContain("/FORCECLOSEAPPLICATIONS", process.Command.ArgumentList); Assert.Equal("0.2.1", registry.Version);
        f.Network.Offline = true; await f.Updater.RollbackAsync(t.Id);
        Assert.Equal("old synthetic exe", await File.ReadAllTextAsync(Path.Combine(f.AppRoot, "PTenoLauncher.exe")));
        Assert.Equal("old synthetic uninstaller", await File.ReadAllTextAsync(Path.Combine(f.AppRoot, "unins000.exe")));
        Assert.Equal("old synthetic uninstall log", await File.ReadAllTextAsync(Path.Combine(f.AppRoot, "unins000.dat")));
        Assert.Equal("0.2.0", registry.Version); Assert.Equal(1, registry.Restores);
    }
    [Fact] public async Task RegistryOwnershipGuardRejectsBeforeAnyPackageDownloadAndNeverFallsBack()
    {
        using var f = new LauncherUpdaterTests.Fixture(); var (offer, registry) = await Prepare(f); registry.Denied = true;
        var process = new Installer(f, offer, registry, 0); f.Updater.InstalledAdapterOverride = new(registry, process);
        var error = await Assert.ThrowsAsync<LauncherUpdateException>(() => f.Updater.PrepareAsync(offer, f.AppRoot, true));
        Assert.Equal("UpdaterInstalledBlocked", error.Code); Assert.Equal(0, f.PackageRequests); Assert.Null(process.Command);
    }
    [Fact] public async Task ChangedUninstallerIsPreservedAndBlocksRollbackBeforeOtherFilesChange()
    {
        using var f = new LauncherUpdaterTests.Fixture(); var (offer, registry) = await Prepare(f); f.Updater.InstalledAdapterOverride = new(registry, new Installer(f, offer, registry, 0));
        var t = await f.Updater.PrepareAsync(offer, f.AppRoot, true); await f.Updater.ApplyPreparedAsync(t.Id);
        var target = Path.Combine(f.AppRoot, "unins000.dat"); await File.WriteAllTextAsync(target, "new user bytes");
        await Assert.ThrowsAsync<LauncherUpdateException>(() => f.Updater.RollbackAsync(t.Id));
        Assert.Equal("new user bytes", await File.ReadAllTextAsync(target)); Assert.Equal("new synthetic exe", await File.ReadAllTextAsync(Path.Combine(f.AppRoot, "PTenoLauncher.exe"))); Assert.Equal(0, registry.Restores);
    }
}
