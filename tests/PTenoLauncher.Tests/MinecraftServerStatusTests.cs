using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Network;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class MinecraftServerStatusTests
{
    [Theory]
    [InlineData(" example.aternos.me ", "example.aternos.me", 25565, true)]
    [InlineData("example.aternos.me:32123", "example.aternos.me", 32123, false)]
    [InlineData("127.0.0.1:32000", "127.0.0.1", 32000, false)]
    [InlineData("[::1]:32123", "::1", 32123, false)]
    [InlineData("[::1]", "::1", 25565, false)]
    [InlineData("localhost", "localhost", 25565, false)]
    public void ParsesGameAddresses(string address, string host, int port, bool srv) =>
        Assert.Equal((host, port, srv), MinecraftServerStatus.ParseAddress(address));

    [Theory]
    [InlineData("https://example.aternos.me")]
    [InlineData("example.aternos.me:0")]
    [InlineData("example.aternos.me:65536")]
    [InlineData("hello/example")]
    [InlineData("a..example")]
    [InlineData("example.aternos.me\nlocalhost")]
    public void RejectsInvalidAddresses(string address) =>
        Assert.Throws<ArgumentException>(() => MinecraftServerStatus.ParseAddress(address));

    [Fact]
    public async Task QueriesSrvEndpointWithOnlyMinecraftStatusPackets()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var query = new MinecraftServerStatus(new NetworkPolicy(), host =>
        {
            Assert.Equal("fixture.aternos.me", host);
            return ("127.0.0.1", port);
        }, TimeSpan.FromSeconds(4));
        var task = query.QueryAsync("fixture.aternos.me", ct.Token);
        using var accepted = await listener.AcceptTcpClientAsync(ct.Token);
        using var stream = accepted.GetStream();
        var hostBytes = "fixture.aternos.me"u8.ToArray();
        byte[] expected = [(byte)(10 + hostBytes.Length), 0, 255, 255, 255, 255, 15,
            (byte)hostBytes.Length, .. hostBytes, 0x63, 0xdd, 1, 1, 0];
        var actual = new byte[expected.Length];
        await stream.ReadExactlyAsync(actual, ct.Token);
        Assert.Equal(expected, actual);
        await stream.WriteAsync(Response("""{"version":{"name":"1.21.1","protocol":767},"players":{"online":3,"max":20}}"""), ct.Token);
        Assert.Equal(new MinecraftStatusReply("1.21.1", 3, 20), await task);
    }

    [Fact]
    public async Task TimeoutAndMalformedPacketsNeverProveShutdown()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var address = "127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
        var query = new MinecraftServerStatus(new NetworkPolicy(), _ => throw new InvalidOperationException(), TimeSpan.FromSeconds(1));
        var silent = query.QueryAsync(address, ct.Token);
        using (await listener.AcceptTcpClientAsync(ct.Token)) Assert.Null(await silent);

        var invalid = query.QueryAsync(address, ct.Token);
        using var peer = await listener.AcceptTcpClientAsync(ct.Token);
        await peer.GetStream().WriteAsync(Response("{bad}"), ct.Token);
        Assert.Null(await invalid);
    }

    [AvaloniaFact]
    public async Task AternosShowsNoReplyForAnEndpointThatClosesWithoutStatus()
    {
        using var ws = new TestWorkspace();
        using var services = new LauncherServices(ws.Paths);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dialog = new AternosChecklistDialog(services, [], [], null);
        dialog.Show();
        try
        {
            dialog.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "AternosStatusAddress").Text =
                "127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
            var check = dialog.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "AternosStatusCheck");
            var result = dialog.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Name == "AternosStatusResult");
            check.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            using (await listener.AcceptTcpClientAsync(ct.Token)) { }
            while (!check.IsEnabled) await Task.Delay(10, ct.Token);
            Assert.Equal(TextCatalog.Get("AternosStatusNoReply"), result.Text);
        }
        finally { dialog.Close(); }
    }

    private static byte[] Response(string json)
    {
        var data = Encoding.UTF8.GetBytes(json);
        Assert.True(data.Length < 126);
        return [(byte)(2 + data.Length), 0, (byte)data.Length, .. data];
    }
}
