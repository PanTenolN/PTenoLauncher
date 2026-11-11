using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public enum ServerPlayerList { Allowed, Blocked, Operators }
public sealed record ServerPlayer(Guid Uuid, string Name);
public sealed record ServerPlayerLists(IReadOnlyList<ServerPlayer> Allowed, IReadOnlyList<ServerPlayer> Blocked,
    IReadOnlyList<ServerPlayer> Operators);
public sealed record ServerPlayerAccess(ServerPlayerLists Players, bool Whitelist);

public sealed partial class VanillaServerManager
{
    private static string PlayerListFile(ServerPlayerList list) => list switch
    {
        ServerPlayerList.Allowed => "whitelist.json",
        ServerPlayerList.Blocked => "banned-players.json",
        ServerPlayerList.Operators => "ops.json",
        _ => throw new ArgumentOutOfRangeException(nameof(list))
    };

    public async Task<ServerPlayerLists> ReadPlayersAsync(LocalVanillaServer server, CancellationToken ct = default)
        => (await ReadPlayerAccessAsync(server, ct: ct)).Players;

    public async Task<ServerPlayerAccess> ReadPlayerAccessAsync(LocalVanillaServer server, VanillaServerSession? session = null,
        CancellationToken ct = default)
    {
        var folder = DirectoryFor(server.Id);
        using var lease = session is null ? Acquire(folder) : null;
        if (session is not null) CheckLiveSession(server, session);
        await CheckIdentityAsync(server, folder, ct);
        if (session is null) await CheckProcessAsync(folder, ct);
        var properties = ParseProperties(await ReadPropertiesBytesAsync(folder, ct), server);
        return new(new(await ReadListAsync(ServerPlayerList.Allowed), await ReadListAsync(ServerPlayerList.Blocked),
            await ReadListAsync(ServerPlayerList.Operators)), properties.GetValueOrDefault("white-list") == "true");

        async Task<ServerPlayer[]> ReadListAsync(ServerPlayerList list) =>
            (await ReadPlayerListAsync(SafePath.Join(folder, PlayerListFile(list)), ct)).Select(ReadPlayer).ToArray();
    }

    public async Task AddPlayerAsync(LocalVanillaServer server, ServerPlayerList list, string name, CancellationToken ct = default)
        => await AddPlayerAsync(server, list, name, null, ct);

    public async Task AddPlayerAsync(LocalVanillaServer server, ServerPlayerList list, string name, VanillaServerSession? session,
        CancellationToken ct = default)
    {
        var player = await LookupPlayerAsync(name, ct);
        if (session is not null)
        {
            await ChangeLivePlayerAsync(server, session, list, player.Uuid, player.Name, true, ct);
            return;
        }

        var folder = DirectoryFor(server.Id);
        using var lease = Acquire(folder);
        await CheckIdentityAsync(server, folder, ct);
        await CheckProcessAsync(folder, ct);
        var path = SafePath.Join(folder, PlayerListFile(list));
        var players = await ReadPlayerListAsync(path, ct);
        if (players.Any(row => ReadPlayer(row).Uuid == player.Uuid)) return;
        var record = new JsonObject { ["uuid"] = player.Uuid.ToString("D"), ["name"] = player.Name };
        switch (list)
        {
            case ServerPlayerList.Blocked:
                record["created"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss '+0000'", CultureInfo.InvariantCulture);
                record["source"] = "PTenoLauncher";
                record["expires"] = "forever";
                record["reason"] = "Banned by an administrator.";
                break;
            case ServerPlayerList.Operators:
                record["level"] = 4;
                record["bypassesPlayerLimit"] = false;
                break;
        }
        players.Add(record);
        await AtomicFile.WriteJsonAsync(path, players, ct);
    }

    public async Task RemovePlayerAsync(LocalVanillaServer server, ServerPlayerList list, Guid uuid, CancellationToken ct = default)
        => await RemovePlayerAsync(server, list, uuid, null, ct);

    public async Task RemovePlayerAsync(LocalVanillaServer server, ServerPlayerList list, Guid uuid, VanillaServerSession? session,
        CancellationToken ct = default)
    {
        if (session is not null)
        {
            await ChangeLivePlayerAsync(server, session, list, uuid, null, false, ct);
            return;
        }
        var folder = DirectoryFor(server.Id);
        using var lease = Acquire(folder);
        await CheckIdentityAsync(server, folder, ct);
        await CheckProcessAsync(folder, ct);
        var path = SafePath.Join(folder, PlayerListFile(list));
        var players = await ReadPlayerListAsync(path, ct);
        var player = players.SingleOrDefault(row => ReadPlayer(row).Uuid == uuid);
        if (player is null) throw new InvalidOperationException("ContentChanged");
        players.Remove(player);
        await AtomicFile.WriteJsonAsync(path, players, ct);
    }

    public async Task SetWhitelistAsync(LocalVanillaServer server, bool enabled, VanillaServerSession? session = null,
        CancellationToken ct = default)
    {
        if (session is null)
        {
            var snapshot = await ReadSettingsAsync(server, ct);
            await ApplySettingsAsync(snapshot, snapshot.Settings with { AllowList = enabled, EnforceAllowList = enabled }, ct);
            return;
        }
        var folder = DirectoryFor(server.Id);
        await RunConfirmedAsync(server, session, async () =>
        {
            var properties = ParseProperties(await ReadPropertiesBytesAsync(folder, ct), server);
            return (properties.GetValueOrDefault("white-list") == "true") == enabled;
        }, () => enabled ? "whitelist on" : "whitelist off", ct);
    }

    private async Task<ServerPlayer> LookupPlayerAsync(string name, CancellationToken ct)
    {
        name = name.Trim();
        if (!IsPlayerName(name)) throw new InvalidDataException("ServerPlayerNameInvalid");
        try
        {
            var uri = new Uri("https://api.minecraftservices.com/minecraft/profile/lookup/name/" + name);
            using var profile = JsonDocument.Parse(await downloader.GetBytesAsync(uri, 4096, ct, DownloadSource.MinecraftProfile));
            var root = profile.RootElement;
            var canonical = root.GetProperty("name").GetString();
            if (canonical is null || !IsPlayerName(canonical) || !canonical.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(root.GetProperty("id").GetString(), "N", out var uuid) || uuid == Guid.Empty ||
                root.TryGetProperty("demo", out var demo) && demo.ValueKind == JsonValueKind.True)
                throw new InvalidDataException("ServerPlayerLookupFailed");
            return new(uuid, canonical);
        }
        catch (Exception error) when (error is HttpRequestException or InvalidDataException or InvalidOperationException or JsonException or KeyNotFoundException ||
                                      error is OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("ServerPlayerLookupFailed", error);
        }
    }

    private async Task ChangeLivePlayerAsync(LocalVanillaServer server, VanillaServerSession session, ServerPlayerList list, Guid uuid,
        string? name, bool add, CancellationToken ct)
    {
        var path = SafePath.Join(DirectoryFor(server.Id), PlayerListFile(list));
        await RunConfirmedAsync(server, session, async () =>
        {
            var player = (await ReadPlayerListAsync(path, ct)).Select(ReadPlayer).SingleOrDefault(entry => entry.Uuid == uuid);
            name ??= player?.Name;
            return (player is not null) == add;
        }, () => (list, add) switch
        {
            (ServerPlayerList.Allowed, true) => "whitelist add " + name,
            (ServerPlayerList.Allowed, false) => "whitelist remove " + name,
            (ServerPlayerList.Blocked, true) => "ban " + name,
            (ServerPlayerList.Blocked, false) => "pardon " + name,
            (ServerPlayerList.Operators, true) => "op " + name,
            (ServerPlayerList.Operators, false) => "deop " + name,
            _ => throw new ArgumentOutOfRangeException(nameof(list))
        }, ct);
    }

    private async Task RunConfirmedAsync(LocalVanillaServer server, VanillaServerSession session, Func<Task<bool>> confirmed,
        Func<string> command, CancellationToken ct)
    {
        await session.PlayerGate.WaitAsync(ct);
        try
        {
            CheckLiveSession(server, session);
            await CheckIdentityAsync(server, DirectoryFor(server.Id), ct);
            if (await confirmed()) return;
            await session.SendAsync(command(), ct);
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                CheckLiveSession(server, session);
                try { if (await confirmed()) return; }
                catch (InvalidDataException) { } // The server can be rewriting its JSON while we read.
                catch (IOException) { }
                await Task.Delay(150, ct);
            }
            throw new InvalidOperationException("ServerPlayerUnconfirmed");
        }
        finally { session.PlayerGate.Release(); }
    }

    private static void CheckLiveSession(LocalVanillaServer server, VanillaServerSession session)
    {
        if (session.Server != server || !session.Ready.IsCompletedSuccessfully || !session.Ready.Result || session.Completion.IsCompleted)
            throw new InvalidOperationException("ServerNotRunning");
    }

    private static bool IsPlayerName(string name) => name.Length is >= 1 and <= 16 &&
        name.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_');

    private static async Task EnableWhitelistEnforcementAsync(string folder, LocalVanillaServer server, CancellationToken ct)
    {
        var properties = ParseProperties(await ReadPropertiesBytesAsync(folder, ct), server);
        if (properties.GetValueOrDefault("enforce-whitelist") == "true") return;
        properties["enforce-whitelist"] = "true";
        var next = Encoding.Latin1.GetBytes(string.Join('\n', properties.Select(pair => pair.Key + "=" + pair.Value)) + "\n");
        await AtomicFile.WriteAsync(SafePath.Join(folder, "server.properties"), next, ct);
    }

    private static ServerPlayer ReadPlayer(JsonNode? row)
    {
        if (row is not JsonObject record || record["name"] is not JsonValue nameValue || !nameValue.TryGetValue<string>(out var name) ||
            (name is null || !IsPlayerName(name)) || record["uuid"] is not JsonValue uuidValue || !uuidValue.TryGetValue<string>(out var id) ||
            !Guid.TryParseExact(id, "D", out var uuid) || uuid == Guid.Empty)
            throw new InvalidDataException("ServerPlayerListInvalid");
        return new(uuid, name);
    }

    private static async Task<JsonArray> ReadPlayerListAsync(string path, CancellationToken ct)
    {
        byte[] bytes;
        try { bytes = await AtomicFile.ReadBytesAsync(path, 1024 * 1024, ct); }
        catch (FileNotFoundException) { return []; }
        catch (InvalidDataException error) { throw new InvalidDataException("ServerPlayerListInvalid", error); }
        try
        {
            if (JsonNode.Parse(bytes) is not JsonArray array || array.Count > 10000)
                throw new InvalidDataException("ServerPlayerListInvalid");
            var players = array.Select(ReadPlayer).ToArray();
            if (players.Select(player => player.Uuid).Distinct().Count() != array.Count ||
                players.Select(player => player.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != array.Count)
                throw new InvalidDataException("ServerPlayerListInvalid");
            return array;
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            throw new InvalidDataException("ServerPlayerListInvalid", error);
        }
    }
}
