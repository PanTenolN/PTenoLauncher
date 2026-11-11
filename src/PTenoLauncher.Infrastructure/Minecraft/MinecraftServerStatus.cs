using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using PTenoLauncher.Infrastructure.Network;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed record MinecraftStatusReply(string Version, int? Online, int? Maximum);

public sealed class MinecraftServerStatus(NetworkPolicy network)
{
    private const int DefaultPort = 25565;
    private const int MaximumPacket = 128 * 1024;
    private readonly Func<string, (string Host, int Port)?> resolveSrv = MinecraftStatusDns.Resolve;
    private readonly TimeSpan timeout = TimeSpan.FromSeconds(8);

    internal MinecraftServerStatus(NetworkPolicy network, Func<string, (string Host, int Port)?> resolveSrv, TimeSpan timeout)
        : this(network) => (this.resolveSrv, this.timeout) = (resolveSrv, timeout);

    public async Task<MinecraftStatusReply?> QueryAsync(string address, CancellationToken ct = default)
    {
        var (host, port, useSrv) = ParseAddress(address);
        network.RequireOnline();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        var token = deadline.Token;
        try
        {
            var endpoint = useSrv ? await Task.Run(() => resolveSrv(host), token).WaitAsync(token) : null;
            using var client = new TcpClient();
            await client.ConnectAsync(endpoint?.Host ?? host, endpoint?.Port ?? port, token);
            using var stream = client.GetStream();
            await stream.WriteAsync(CreateRequest(host, port), token);
            var length = await ReadVarIntAsync(stream, token);
            if (length is < 2 or > MaximumPacket) return null;
            var packet = new byte[length];
            await stream.ReadExactlyAsync(packet, token);
            using var payload = new MemoryStream(packet, writable: false);
            if (await ReadVarIntAsync(payload, token) != 0) return null;
            var jsonLength = await ReadVarIntAsync(payload, token);
            if (jsonLength != payload.Length - payload.Position) return null;
            using var json = JsonDocument.Parse(packet.AsMemory((int)payload.Position, jsonLength), new JsonDocumentOptions { MaxDepth = 24 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.Object || !version.TryGetProperty("name", out var name) ||
                name.ValueKind != JsonValueKind.String || name.GetString() is not { Length: > 0 and <= 100 } label ||
                label.Any(char.IsControl)) return null;
            var hasPlayers = root.TryGetProperty("players", out var players) && players.ValueKind == JsonValueKind.Object;
            return new(label, hasPlayers ? Count(players, "online") : null, hasPlayers ? Count(players, "max") : null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (Exception error) when (error is IOException or SocketException or JsonException or InvalidDataException)
        {
            ct.ThrowIfCancellationRequested();
            return null;
        }
    }

    internal static (string Host, int Port, bool UseSrv) ParseAddress(string address)
    {
        address = address.Trim();
        if (address.Length is 0 or > 300 || address.Any(ch => char.IsControl(ch) || char.IsWhiteSpace(ch)))
            throw new ArgumentException("AternosStatusInvalidAddress");
        var unbracketed = address.StartsWith('[') && address.EndsWith(']') ? address[1..^1] : address;
        if (!unbracketed.Contains('[') && !unbracketed.Contains(']') && !unbracketed.Contains('%') &&
            IPAddress.TryParse(unbracketed, out var literal) && literal.AddressFamily == AddressFamily.InterNetworkV6)
            return (literal.ToString(), DefaultPort, false);

        var split = address.LastIndexOf(':');
        var port = DefaultPort;
        var host = address;
        if (split >= 0)
        {
            if (!int.TryParse(address.AsSpan(split + 1), NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535)
                throw new ArgumentException("AternosStatusInvalidAddress");
            host = address[..split];
        }
        if (host.StartsWith('[') && host.EndsWith(']') && !host.Contains('%') &&
            IPAddress.TryParse(host[1..^1], out var ip6) && ip6.AddressFamily == AddressFamily.InterNetworkV6)
            return (ip6.ToString(), port, false);
        if (IPAddress.TryParse(host, out var ip4) && ip4.AddressFamily == AddressFamily.InterNetwork && ip4.ToString() == host)
            return (host, port, false);
        host = NormalizeHostname(host) ?? throw new ArgumentException("AternosStatusInvalidAddress");
        return (host, port, split < 0 && host.Contains('.'));
    }

    internal static string? NormalizeHostname(string host)
    {
        if (host.EndsWith('.')) host = host[..^1];
        try { host = new IdnMapping { UseStd3AsciiRules = true }.GetAscii(host).ToLowerInvariant(); }
        catch (ArgumentException) { return null; }
        if (host.Length > 253 || !host.Split('.').All(label => label.Length is > 0 and <= 63 &&
            label[0] != '-' && label[^1] != '-' && label.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'))) return null;
        return host;
    }

    private static byte[] CreateRequest(string host, int port)
    {
        using var handshake = new MemoryStream();
        handshake.WriteByte(0); // Handshake packet ID.
        WriteVarInt(handshake, -1); // A status request does not negotiate a login protocol.
        var bytes = Encoding.UTF8.GetBytes(host);
        WriteVarInt(handshake, bytes.Length);
        handshake.Write(bytes);
        Span<byte> portBytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(portBytes, (ushort)port);
        handshake.Write(portBytes);
        handshake.WriteByte(1); // Status state.

        using var request = new MemoryStream();
        WriteVarInt(request, (int)handshake.Length);
        handshake.WriteTo(request);
        request.WriteByte(1); // Length of status request.
        request.WriteByte(0); // Status request packet ID.
        return request.ToArray();
    }

    private static void WriteVarInt(Stream stream, int value)
    {
        var bits = unchecked((uint)value);
        while (bits >= 128)
        {
            stream.WriteByte((byte)((bits & 127) | 128));
            bits >>= 7;
        }
        stream.WriteByte((byte)bits);
    }

    private static async ValueTask<int> ReadVarIntAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[1];
        var result = 0;
        for (var shift = 0; shift <= 28; shift += 7)
        {
            await stream.ReadExactlyAsync(buffer, ct);
            var next = buffer[0];
            if (shift == 28 && next > 7) break;
            result |= (next & 127) << shift;
            if (next < 128) return result;
        }
        throw new InvalidDataException("Invalid status packet integer.");
    }

    private static int? Count(JsonElement players, string key) => players.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var count) && count >= 0 ? count : null;
}

internal static class MinecraftStatusDns
{
    private const ushort SrvType = 33;

    internal static (string Host, int Port)? Resolve(string host)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var code = DnsQuery("_minecraft._tcp." + host + ".", SrvType, 0, IntPtr.Zero, out var records, IntPtr.Zero);
        try
        {
            if (code != 0) return null;
            var candidates = new List<(string Host, int Port, ushort Priority, ushort Weight)>();
            var current = records;
            for (var i = 0; current != IntPtr.Zero && i < 128; i++)
            {
                var header = Marshal.PtrToStructure<RecordHeader>(current);
                if (header.Type == SrvType && header.DataLength >= Marshal.SizeOf<SrvData>())
                {
                    var srv = Marshal.PtrToStructure<SrvData>(IntPtr.Add(current, Marshal.SizeOf<RecordHeader>()));
                    if (srv.Port != 0 && Marshal.PtrToStringUni(srv.Target) is { } target &&
                        MinecraftServerStatus.NormalizeHostname(target) is { } name)
                        candidates.Add((name, srv.Port, srv.Priority, srv.Weight));
                }
                current = header.Next;
            }
            if (candidates.Count == 0) return null;
            var priority = candidates.Min(entry => entry.Priority);
            var group = candidates.Where(entry => entry.Priority == priority).ToArray();
            var total = group.Sum(entry => (int)entry.Weight);
            if (total == 0)
            {
                var random = group[Random.Shared.Next(group.Length)];
                return (random.Host, random.Port);
            }
            var choice = Random.Shared.Next(total);
            foreach (var entry in group)
            {
                choice -= entry.Weight;
                if (choice < 0) return (entry.Host, entry.Port);
            }
            return null;
        }
        finally { if (records != IntPtr.Zero) DnsRecordListFree(records, 1); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RecordHeader
    {
        public IntPtr Next, Name;
        public ushort Type, DataLength;
        public uint Flags, Ttl, Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SrvData
    {
        public IntPtr Target;
        public ushort Priority, Weight, Port, Pad;
    }

    [DllImport("dnsapi.dll", EntryPoint = "DnsQuery_W", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int DnsQuery(string name, ushort type, uint options, IntPtr extra, out IntPtr result, IntPtr reserved);

    [DllImport("dnsapi.dll", ExactSpelling = true)]
    private static extern void DnsRecordListFree(IntPtr records, int freeType);
}
