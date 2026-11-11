using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed record LocalServerSettings(string Name, string Motd, string GameMode, string Difficulty, bool Pvp, int MaxPlayers,
    int Port, int MemoryMb, bool AllowList, bool EnforceAllowList);

public sealed record LocalServerSettingsSnapshot(LocalVanillaServer Server, LocalServerSettings Settings, string Revision,
    LocalServerSettings? Previous);

public sealed partial class VanillaServerManager
{
    private const string PendingSettingsFile = ".settings-pending.json";
    private const string PreviousSettingsFile = ".settings-previous.json";
    private sealed record SettingsTransaction(LocalVanillaServer Before, LocalVanillaServer After, byte[] Original, string AfterHash);

    public async Task<LocalServerSettingsSnapshot> ReadSettingsAsync(LocalVanillaServer server, CancellationToken ct = default)
    {
        var folder = DirectoryFor(server.Id);
        using var lease = Acquire(folder);
        await CheckIdentityAsync(server, folder, ct);
        await CheckProcessAsync(folder, ct);
        server = await RecoverSettingsAsync(folder, server, ct);
        var bytes = await ReadPropertiesBytesAsync(folder, ct);
        var properties = ParseProperties(bytes, server);
        var previous = await AtomicFile.ReadJsonAsync<SettingsTransaction>(SafePath.Join(folder, PreviousSettingsFile), ct);
        if (previous is not null) ValidateSettingsTransaction(previous, server.Id);
        return new(server, ReadSettings(server, properties), Digest(bytes),
            previous is null ? null : ReadSettings(previous.Before, ParseProperties(previous.Original, previous.Before)));
    }

    public async Task<LocalVanillaServer> ApplySettingsAsync(LocalServerSettingsSnapshot snapshot, LocalServerSettings desired,
        CancellationToken ct = default)
    {
        var server = snapshot.Server;
        var updated = ValidateSettings(server, desired);

        var folder = DirectoryFor(server.Id);
        using var lease = Acquire(folder);
        await CheckIdentityAsync(server, folder, ct);
        await CheckProcessAsync(folder, ct);
        CheckPendingSettings(folder);
        var old = await ReadPropertiesBytesAsync(folder, ct);
        if (Digest(old) != snapshot.Revision) throw new InvalidOperationException("ContentChanged");
        var properties = ParseProperties(old, server);
        properties["server-port"] = updated.Port.ToString(CultureInfo.InvariantCulture);
        properties["motd"] = EncodeProperty(desired.Motd);
        properties["gamemode"] = desired.GameMode;
        properties["difficulty"] = desired.Difficulty;
        properties["pvp"] = BoolProperty(desired.Pvp);
        properties["max-players"] = desired.MaxPlayers.ToString(CultureInfo.InvariantCulture);
        properties["white-list"] = BoolProperty(desired.AllowList);
        properties["enforce-whitelist"] = BoolProperty(desired.EnforceAllowList);
        var next = Encoding.Latin1.GetBytes(string.Join('\n', properties.Select(pair => pair.Key + "=" + pair.Value)) + "\n");
        var pending = SafePath.Join(folder, PendingSettingsFile);
        var previous = SafePath.Join(folder, PreviousSettingsFile);
        await AtomicFile.WriteJsonAsync(pending, new SettingsTransaction(server, updated, old, Digest(next)), ct);
        try
        {
            await AtomicFile.WriteAsync(SafePath.Join(folder, "server.properties"), next, ct);
            await AtomicFile.WriteJsonAsync(SafePath.Join(folder, "server.json"), updated, ct);
            SafePath.RejectLinks(previous);
            File.Move(pending, previous, overwrite: true);
        }
        catch (Exception error)
        {
            // Leave the pending record for recovery if rollback itself is blocked.
            try
            {
                var identity = await AtomicFile.ReadJsonAsync<LocalVanillaServer>(SafePath.Join(folder, "server.json")) ?? server;
                await RecoverSettingsAsync(folder, identity, CancellationToken.None);
            }
            catch (Exception recovery)
            {
                throw new InvalidOperationException("ServerSettingsPending", new AggregateException(error, recovery));
            }
            throw;
        }
        return updated;
    }

    public static LocalVanillaServer ValidateSettings(LocalVanillaServer server, LocalServerSettings desired)
    {
        var updated = server with { Name = desired.Name.Trim(), Port = desired.Port, MemoryMb = desired.MemoryMb };
        Validate(updated, server.Id);
        if (desired.Motd is not { Length: <= 255 } || desired.Motd.Any(char.IsControl) ||
            desired.GameMode is not ("survival" or "creative" or "adventure" or "spectator") ||
            desired.Difficulty is not ("peaceful" or "easy" or "normal" or "hard") ||
            desired.MaxPlayers is < 1 or > 1000)
            throw new InvalidDataException("ServerSettingsInvalid");

        return updated;
    }

    private static void CheckPendingSettings(string folder)
    {
        if (File.Exists(SafePath.Join(folder, PendingSettingsFile))) throw new InvalidOperationException("ServerSettingsPending");
    }

    private static async Task<LocalVanillaServer> RecoverSettingsAsync(string folder, LocalVanillaServer current, CancellationToken ct)
    {
        var pending = SafePath.Join(folder, PendingSettingsFile);
        var transaction = await AtomicFile.ReadJsonAsync<SettingsTransaction>(pending, ct);
        if (transaction is null) return current;
        ValidateSettingsTransaction(transaction, current.Id);
        var currentHash = Digest(await ReadPropertiesBytesAsync(folder, ct));
        var originalHash = Digest(transaction.Original);
        if ((current != transaction.Before && current != transaction.After) ||
            (currentHash != originalHash && currentHash != transaction.AfterHash))
            throw new InvalidOperationException("ContentChanged");
        if (currentHash != originalHash)
            await AtomicFile.WriteAsync(SafePath.Join(folder, "server.properties"), transaction.Original, ct);
        if (current != transaction.Before)
            await AtomicFile.WriteJsonAsync(SafePath.Join(folder, "server.json"), transaction.Before, ct);
        File.Delete(pending);
        return transaction.Before;
    }

    private static void ValidateSettingsTransaction(SettingsTransaction transaction, Guid id)
    {
        Validate(transaction.Before, id);
        Validate(transaction.After, id);
        var after = transaction.After;
        if ((transaction.Before with { Name = after.Name, Port = after.Port, MemoryMb = after.MemoryMb }) != after ||
            transaction.Original is not { Length: > 0 and <= 256 * 1024 } ||
            transaction.AfterHash is not { Length: 64 } || !transaction.AfterHash.All(Uri.IsHexDigit))
            throw new InvalidDataException("ServerSettingsInvalid");
        _ = ParseProperties(transaction.Original, transaction.Before);
    }

    private static LocalServerSettings ReadSettings(LocalVanillaServer server, Dictionary<string, string> properties) => new(
        server.Name, DecodeProperty(properties.GetValueOrDefault("motd", "A Minecraft Server").TrimStart(' ', '\t', '\f')),
        properties.GetValueOrDefault("gamemode", "survival") switch { "0" => "survival", "1" => "creative", "2" => "adventure", "3" => "spectator", var mode => mode },
        properties.GetValueOrDefault("difficulty", "easy") switch { "0" => "peaceful", "1" => "easy", "2" => "normal", "3" => "hard", var difficulty => difficulty },
        properties.GetValueOrDefault("pvp", "true") == "true",
        int.TryParse(properties.GetValueOrDefault("max-players"), NumberStyles.None, CultureInfo.InvariantCulture, out var max) ? max : 20,
        server.Port, server.MemoryMb, properties.GetValueOrDefault("white-list") == "true",
        properties.GetValueOrDefault("enforce-whitelist") == "true");

    private static string Digest(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    private static string BoolProperty(bool value) => value ? "true" : "false";

    private static string EncodeProperty(string value)
    {
        var encoded = new StringBuilder();
        foreach (var ch in value)
        {
            if (ch is '\\' or ' ') encoded.Append('\\').Append(ch);
            else if (ch > '~') encoded.Append("\\u").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
            else encoded.Append(ch);
        }
        return encoded.ToString();
    }

    private static string DecodeProperty(string value) => Regex.Replace(value, @"\\(?:u[0-9a-fA-F]{4}|.)", match => match.Value[1] switch
    {
        'u' when match.Length == 6 => ((char)int.Parse(match.Value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString(),
        't' => "\t", 'r' => "\r", 'n' => "\n", 'f' => "\f", _ => match.Value[1..]
    });
}
