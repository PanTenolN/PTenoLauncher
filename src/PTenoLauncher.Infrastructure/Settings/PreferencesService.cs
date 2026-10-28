using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Settings;

public sealed class PreferencesService(ILauncherStore store)
{
    public async Task<ThemePreferences> GetThemeAsync(CancellationToken ct = default)
    {
        var json = await store.GetSettingAsync("ui.theme", ct);
        var value = json is null ? new() : JsonSerializer.Deserialize<ThemePreferences>(json, AtomicFile.JsonOptions) ?? new ThemePreferences();
        PreferencesValidation.Validate(value); return value;
    }
    public Task SetThemeAsync(ThemePreferences value, CancellationToken ct = default)
    {
        PreferencesValidation.Validate(value);
        return store.SetSettingAsync("ui.theme", JsonSerializer.Serialize(value, AtomicFile.JsonOptions), ct);
    }
    public async Task<bool> GetAiEnabledAsync(CancellationToken ct = default) => bool.TryParse(await store.GetSettingAsync("ai.enabled", ct), out var enabled) && enabled;
    public Task SetAiEnabledAsync(bool enabled, CancellationToken ct = default) => store.SetSettingAsync("ai.enabled", enabled.ToString(), ct);
}