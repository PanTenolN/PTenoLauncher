using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed record AternosSavedServer(string Name, string Address)
{
    private const string Setting = "aternos.saved-server";

    public static async Task<AternosSavedServer?> LoadAsync(ILauncherStore store, CancellationToken ct = default)
    {
        var saved = await store.GetSettingAsync(Setting, ct);
        return string.IsNullOrEmpty(saved) ? null : JsonSerializer.Deserialize<AternosSavedServer>(saved, AtomicFile.JsonOptions)?.Validated();
    }

    public Task SaveAsync(ILauncherStore store, CancellationToken ct = default) =>
        store.SetSettingAsync(Setting, JsonSerializer.Serialize(Validated(), AtomicFile.JsonOptions), ct);

    private AternosSavedServer Validated()
    {
        var name = Name?.Trim();
        if (name is not { Length: >= 1 and <= 80 } || name.Any(char.IsControl))
            throw new ArgumentException("AternosSavedNameInvalid");
        var address = Address?.Trim() ?? "";
        _ = MinecraftServerStatus.ParseAddress(address);
        return new(name, address);
    }
}
