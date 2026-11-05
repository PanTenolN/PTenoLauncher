using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Updates;

public sealed record InstalledRegistryValue(string Key, string Name, string Kind, string Value);
public sealed record InstalledRegistration(string Root, string DataRoot, string Uninstaller, string Version, IReadOnlyList<InstalledRegistryValue> Values);
public sealed record InstalledUpdateSnapshot(InstalledRegistration Registration, IReadOnlyList<LauncherUpdateFile> UninstallerFiles);
public interface IInstalledUpdateRegistry
{
    InstalledRegistration ReadOwned(string applicationRoot, string dataRoot);
    void RestoreOwned(InstalledRegistration before);
}
public interface IInnoUpdateProcess
{
    Task<int> RunAsync(ProcessStartInfo command, Func<UpdateProcessIdentity, Task> started);
}
internal sealed class InnoUpdateProcess : IInnoUpdateProcess
{
    public async Task<int> RunAsync(ProcessStartInfo command, Func<UpdateProcessIdentity, Task> started)
    {
        using var process = Process.Start(command) ?? throw new LauncherUpdateException("UpdaterHelper");
        try { await started(new(process.Id, process.StartTime.ToUniversalTime().Ticks, Path.GetFullPath(command.FileName))); }
        catch { await process.WaitForExitAsync(); throw; }
        // No forced termination: an interrupted installer must finish before files/registration can be recovered.
        await process.WaitForExitAsync(); return process.ExitCode;
    }
}
[SupportedOSPlatform("windows")]
public sealed class WindowsInstalledUpdateRegistry : IInstalledUpdateRegistry
{
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{CF2E0B60-B0F5-4E74-9481-E0CE4ABBE030}_is1";
    public const string ApplicationKey = @"Software\PTenoLauncher";
    public InstalledRegistration ReadOwned(string applicationRoot, string dataRoot)
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var other = machine.OpenSubKey(UninstallKey);
            if (other is not null) throw new LauncherUpdateException("UpdaterInstalledBlocked");
            using var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            // Preflight both read and write access, without changing a key or creating an ownership record.
            using var app = user.OpenSubKey(ApplicationKey, writable: true);
            using var uninstall = user.OpenSubKey(UninstallKey, writable: true);
            if (app is null || uninstall is null || app.SubKeyCount != 0 || uninstall.SubKeyCount != 0) throw new LauncherUpdateException("UpdaterInstalledBlocked");
            var root = Canon(applicationRoot); var data = Canon(dataRoot);
            var command = uninstall.GetValue("UninstallString") as string ?? "";
            var executable = command.StartsWith('"') && command.EndsWith('"') ? command[1..^1] : command;
            if (!Path.IsPathFullyQualified(executable) || Canon(Path.GetDirectoryName(executable)!) != root || !Regex.IsMatch(Path.GetFileName(executable), "^unins[0-9]{3}\\.exe$") ||
                !Canon(uninstall.GetValue("InstallLocation") as string ?? "").Equals(root, StringComparison.OrdinalIgnoreCase) || !Canon(app.GetValue("UninstallDataRoot") as string ?? "").Equals(data, StringComparison.OrdinalIgnoreCase) ||
                uninstall.GetValue("DisplayName") as string != "PTenoLauncher") throw new LauncherUpdateException("UpdaterInstalledBlocked");
            var values = new List<InstalledRegistryValue>();
            foreach (var (label, key) in new[] { ("app", app), ("uninstall", uninstall) })
                foreach (var name in key.GetValueNames())
                {
                    var kind = key.GetValueKind(name); if (kind is not (RegistryValueKind.String or RegistryValueKind.DWord) || name.Length > 256 || values.Count >= 128) throw new LauncherUpdateException("UpdaterInstalledBlocked");
                    values.Add(new(label, name, kind.ToString(), Convert.ToString(key.GetValue(name), System.Globalization.CultureInfo.InvariantCulture) ?? ""));
                }
            return new(root, data, Path.GetFileName(executable), uninstall.GetValue("DisplayVersion") as string ?? throw new LauncherUpdateException("UpdaterInstalledBlocked"), values.OrderBy(v => v.Key, StringComparer.Ordinal).ThenBy(v => v.Name, StringComparer.Ordinal).ToArray());
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentException)
        { throw new LauncherUpdateException("UpdaterInstalledBlocked", e); }
    }
    public void RestoreOwned(InstalledRegistration before)
    {
        _ = ReadOwned(before.Root, before.DataRoot); // Preserve the ownership guard immediately before writing.
        using var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        foreach (var (label, path) in new[] { ("app", ApplicationKey), ("uninstall", UninstallKey) })
        {
            using var key = user.OpenSubKey(path, true) ?? throw new LauncherUpdateException("UpdaterInstalledBlocked");
            var desired = before.Values.Where(v => v.Key == label).ToArray();
            foreach (var name in key.GetValueNames().Where(n => !desired.Any(v => v.Name == n))) key.DeleteValue(name);
            foreach (var v in desired)
            {
                if (v.Kind == "String") key.SetValue(v.Name, v.Value, RegistryValueKind.String);
                else if (v.Kind == "DWord" && int.TryParse(v.Value, out var number)) key.SetValue(v.Name, number, RegistryValueKind.DWord);
                else throw new LauncherUpdateException("UpdaterInvalid");
            }
            key.Flush();
        }
    }
    private static string Canon(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
}
public sealed class InnoUpdateAdapter(IInstalledUpdateRegistry registry, IInnoUpdateProcess runner)
{
    public static InnoUpdateAdapter Default
        => OperatingSystem.IsWindows() ? new(new WindowsInstalledUpdateRegistry(), new InnoUpdateProcess()) : throw new LauncherUpdateException("UpdaterInstalledBlocked");
    private static string State(string operation) => SafePath.Join(operation, "installed-after.json");
    private static bool Same(object a, object b) => JsonSerializer.Serialize(a, AtomicFile.JsonOptions) == JsonSerializer.Serialize(b, AtomicFile.JsonOptions);
    internal static void ValidateSnapshot(InstalledUpdateSnapshot state, string root, string dataRoot)
    {
        if (state is null || state.Registration is null || state.Registration.Root != root || state.Registration.DataRoot != dataRoot || state.Registration.Values is null || state.Registration.Values.Count > 128 ||
            state.UninstallerFiles is null || state.UninstallerFiles.Count is < 2 or > 4 || state.UninstallerFiles.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.UninstallerFiles.Count ||
            !state.UninstallerFiles.Any(f => f.Path == state.Registration.Uninstaller) || !state.UninstallerFiles.Any(f => f.Path == Path.ChangeExtension(state.Registration.Uninstaller, ".dat"))) throw new LauncherUpdateException("UpdaterInstalledBlocked");
        foreach (var f in state.UninstallerFiles)
            if (!Regex.IsMatch(f.Path, "^unins[0-9]{3}\\.(exe|dat|msg)$") || f.Size is <= 0 or > 64L * 1024 * 1024 || f.Sha256 is not { Length: 64 } || !f.Sha256.All(Uri.IsHexDigit)) throw new LauncherUpdateException("UpdaterInstalledBlocked");
        if (state.Registration.Values.Any(v => v.Key is not ("app" or "uninstall") || v.Kind is not ("String" or "DWord")) || state.Registration.Values.Select(v => v.Key + "/" + v.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.Registration.Values.Count) throw new LauncherUpdateException("UpdaterInstalledBlocked");
    }
    public async Task<InstalledUpdateSnapshot> CaptureAsync(string root, string dataRoot, CancellationToken ct)
    {
        var registration = registry.ReadOwned(root, dataRoot); var files = new List<LauncherUpdateFile>();
        foreach (var file in Directory.EnumerateFiles(root, Path.GetFileNameWithoutExtension(registration.Uninstaller) + ".*").OrderBy(f => f, StringComparer.Ordinal).Take(5))
        {
            SafePath.RejectLinks(file); await using var s = File.OpenRead(file);
            files.Add(new(Path.GetFileName(file), s.Length, Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(s, ct)).ToLowerInvariant()));
        }
        var result = new InstalledUpdateSnapshot(registration, files); ValidateSnapshot(result, root, dataRoot); return result;
    }
    public async Task VerifyBeforeAsync(InstalledUpdateSnapshot before, string root, string dataRoot, CancellationToken ct)
    { if (!Same(before, await CaptureAsync(root, dataRoot, ct))) throw new LauncherUpdateException("UpdaterConflict"); }
    public async Task ApplyAsync(LauncherUpdateTransaction t, string installer, string operation, CancellationToken ct)
    {
        var before = t.Installed ?? throw new LauncherUpdateException("UpdaterInstalledBlocked");
        await VerifyBeforeAsync(before, t.ApplicationRoot, t.DataRoot, ct);
        var p = t.Offer.Package;
        if (p.InstallerUrl is null || p.InstallerContract != "inno-single-process-v1" || !await VerifiedDownloader.VerifyAsync(installer, p.InstallerSha256!, "SHA256", p.InstallerSize, ct)) throw new LauncherUpdateException("UpdaterIntegrity");
        var info = new ProcessStartInfo(installer) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = operation };
        foreach (var a in new[] { "/CURRENTUSER", "/VERYSILENT", "/SUPPRESSMSGBOXES", "/SP-", "/NORESTART", "/RESTARTEXITCODE=3010", "/NOCLOSEAPPLICATIONS", "/NOFORCECLOSEAPPLICATIONS", "/NORESTARTAPPLICATIONS", "/DIR=" + t.ApplicationRoot, "/INTEGRATIONDATAROOT=" + t.DataRoot, "/UPDATER=1", "/LOG=" + SafePath.Join(operation, "inno.log") }) info.ArgumentList.Add(a);
        ct.ThrowIfCancellationRequested();
        var code = await runner.RunAsync(info, id => AtomicFile.WriteJsonAsync(SafePath.Join(operation, "installer-process.json"), id));
        // Capture owned registry/uninstaller outputs even after a failed exit. Missing/foreign ownership fails closed.
        var after = await CaptureAsync(t.ApplicationRoot, t.DataRoot, CancellationToken.None);
        await AtomicFile.WriteJsonAsync(State(operation), after, CancellationToken.None);
        if (code != 0) throw new LauncherUpdateException("UpdaterInstallerFailed");
        if (after.Registration.Version != t.Offer.Release.Build.Version) throw new LauncherUpdateException("UpdaterIntegrity");
        foreach (var f in p.Files) if (!await VerifiedDownloader.VerifyAsync(SafePath.Join(t.ApplicationRoot, f.Path), f.Sha256, "SHA256", f.Size, CancellationToken.None)) throw new LauncherUpdateException("UpdaterIntegrity");
    }
    public async Task VerifyRecoveryAsync(LauncherUpdateTransaction t, string operation, CancellationToken ct)
    {
        var installer = await AtomicFile.ReadJsonAsync<UpdateProcessIdentity>(SafePath.Join(operation, "installer-process.json"), ct);
        if (installer is not null) { using var active = installer.OpenExact(required: false); if (active is not null) throw new LauncherUpdateException("UpdaterBusy"); }
        var before = t.Installed ?? throw new LauncherUpdateException("UpdaterInstalledBlocked");
        var after = await AtomicFile.ReadJsonAsync<InstalledUpdateSnapshot>(State(operation), ct);
        var expected = after ?? before;
        ValidateSnapshot(expected, t.ApplicationRoot, t.DataRoot);
        var currentRegistry = registry.ReadOwned(t.ApplicationRoot, t.DataRoot);
        if (currentRegistry.Uninstaller != before.Registration.Uninstaller ||
            currentRegistry.Values.Any(v => !before.Registration.Values.Contains(v) && !expected.Registration.Values.Contains(v))) throw new LauncherUpdateException("UpdaterConflict");
        foreach (var file in Directory.EnumerateFiles(t.ApplicationRoot, Path.GetFileNameWithoutExtension(currentRegistry.Uninstaller) + ".*"))
        {
            var name = Path.GetFileName(file); var match = false;
            foreach (var f in before.UninstallerFiles.Concat(expected.UninstallerFiles).Where(f => f.Path == name)) match |= await VerifiedDownloader.VerifyAsync(file, f.Sha256, "SHA256", f.Size, ct);
            if (!match) throw new LauncherUpdateException("UpdaterConflict");
        }
        foreach (var f in before.UninstallerFiles)
            if (!await VerifiedDownloader.VerifyAsync(SafePath.Join(operation, "backup-uninstaller/" + f.Path), f.Sha256, "SHA256", f.Size, ct)) throw new LauncherUpdateException("UpdaterIntegrity");
    }
    public async Task RestoreAsync(LauncherUpdateTransaction t, string operation, CancellationToken ct)
    {
        await VerifyRecoveryAsync(t, operation, ct); var before = t.Installed!;
        foreach (var f in before.UninstallerFiles)
        {
            var target = SafePath.Join(t.ApplicationRoot, f.Path);
            if (await VerifiedDownloader.VerifyAsync(target, f.Sha256, "SHA256", f.Size, ct)) continue;
            if (File.Exists(target))
            {
                var held = SafePath.Join(operation, "retired-uninstaller/" + Guid.NewGuid().ToString("N") + "/" + f.Path); Directory.CreateDirectory(Path.GetDirectoryName(held)!); File.Move(target, held, false);
            }
            var temporary = SafePath.Join(operation, "restore-uninstaller/" + Guid.NewGuid().ToString("N") + "/" + f.Path); Directory.CreateDirectory(Path.GetDirectoryName(temporary)!);
            File.Copy(SafePath.Join(operation, "backup-uninstaller/" + f.Path), temporary, false); File.Move(temporary, target, false);
        }
        registry.RestoreOwned(before.Registration);
        if (!Same(before, await CaptureAsync(t.ApplicationRoot, t.DataRoot, ct))) throw new LauncherUpdateException("UpdaterConflict");
    }
}
