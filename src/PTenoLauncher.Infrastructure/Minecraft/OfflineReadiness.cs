using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Authentication;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed class OfflineReadiness(LauncherPaths paths, JavaManager java, MicrosoftAuthentication auth, LocalModManager? mods = null)
{
    public async Task<ReadinessReport> CheckAsync(GameProfile profile, CancellationToken ct = default)
    {
        var issues = new List<ReadinessIssue>(); int verified = 0;
        if (profile.Loader is not (ModLoader.Vanilla or ModLoader.Fabric or ModLoader.Quilt)) issues.Add(new("loader.unsupported", "Встановлення та запуск Mod Loader ще не реалізовано."));
        InstallationReceipt? receipt = null;
        try { receipt = await AtomicFile.ReadJsonAsync<InstallationReceipt>(paths.Receipt(profile.Id), ct); if (receipt is not null) ReceiptValidator.Validate(paths, receipt); }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException) { receipt = null; issues.Add(new("manifest.corrupt", "Пошкоджено локальний опис встановлення.")); }
        if (receipt is null) issues.Add(new("game.missing", "Minecraft ще не встановлено для цього профілю."));
        else
        {
            if (receipt.Loader != profile.Loader) issues.Add(new("loader.mismatch", "Потрібно встановити вибраний Mod Loader або Vanilla для цього профілю."));
            if (receipt.Version != profile.MinecraftVersion) issues.Add(new("version.mismatch", "Встановлена версія відрізняється від налаштувань профілю."));
            foreach (var file in receipt.Files)
            {
                ct.ThrowIfCancellationRequested();
                if (!await VerifiedDownloader.VerifyAsync(SafePath.Join(paths.Cache, file.RelativePath), file.Hash, file.Algorithm, file.Size, ct))
                    issues.Add(new("file.invalid", $"Відсутній або пошкоджений файл: {file.RelativePath}"));
                else verified++;
            }
        }
        if (string.IsNullOrWhiteSpace(profile.JavaPath)) issues.Add(new("java.missing", "Оберіть Java x64 для профілю."));
        else
        {
            try
            {
                if (!await JavaRuntimeManager.VerifyManagedPathAsync(paths, profile.JavaPath, ct)) issues.Add(new("java.corrupt", "Пошкоджений керований Java Runtime. Відновіть Java."));
                else
                {
                var runtime = await java.InspectAsync(profile.JavaPath, ct);
                if (!runtime.Is64Bit) issues.Add(new("java.arch", "Потрібна Java x64."));
                if (receipt is not null && runtime.Major != receipt.JavaMajor)
                    issues.Add(new("java.version", $"Для цієї версії потрібна Java {receipt.JavaMajor}; обрана Java {runtime.Major}."));
                }
            }
            catch (Exception e) when (e is not OperationCanceledException) { issues.Add(new("java.invalid", "Java не пройшла перевірку.")); }
        }
        var modReport = await new InstalledModReadiness(paths, mods ?? new(paths, new SqliteLauncherStore(paths))).CheckAsync(profile, receipt, ct);
        issues.AddRange(modReport.Issues);
        if (auth.GetCachedSession() is null) issues.Add(new("account.missing", "Спочатку увійдіть онлайн і підтвердьте ліцензію Minecraft Java Edition."));
        return new(issues, verified, modReport.VerifiedFiles);
    }
}
