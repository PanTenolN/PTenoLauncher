using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Desktop;

public sealed partial class App
{
    private sealed record ForgeSmokeState(LocalVanillaServer Server, string PropertiesSha256);

    private static async Task ExerciseLiveForgeAsync(MainWindow window, MainViewModel vm, string? mode)
    {
        var operation = Environment.GetEnvironmentVariable("PTENO_VERIFICATION_ROOT");
        if (mode is not ("create" or "reopen") || operation is null ||
            !File.Exists(SafePath.Join(operation, "owned-test-root.json")) ||
            !string.Equals(vm.DataRoot, SafePath.Join(operation, "tmp/forge-exe"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Live Forge EXE verification requires owned isolated data.");

        var receipt = SafePath.Join(vm.DataRoot, "forge-smoke-state.json");
        window.FindControl<Button>("ServersNavButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitAsync(() => vm.IsServers && vm.CanEditServer, TimeSpan.FromSeconds(30), "read server list");
        if (mode == "reopen")
        {
            var saved = await AtomicFile.ReadJsonAsync<ForgeSmokeState>(receipt) ?? throw new InvalidDataException("Missing Forge smoke state.");
            var reopened = vm.ServerCards.Single().Server;
            if (reopened != saved.Server || await PropertiesHashAsync(reopened) != saved.PropertiesSha256 ||
                !File.Exists(SafePath.Join(vm.ServerManager.DirectoryFor(reopened.Id), "world/level.dat")))
                throw new IOException("Forge server card, settings or world did not persist after reopening the EXE.");
            Console.WriteLine("PASS: Forge card, settings and world persisted after reopening the EXE.");
            return;
        }

        var core = window.FindControl<ComboBox>("ServerCorePicker")!;
        core.SelectedItem = vm.ServerCores.Single(option => option.Core == ServerCore.Forge);
        await WaitAsync(() => !vm.ServerCatalogLoading, TimeSpan.FromSeconds(85), "official Forge catalog");
        if (vm.NewServerVersion?.Id != "1.21.1" || vm.NewServerBuild != "52.1.0")
            throw new InvalidDataException("Official Forge catalog: " + vm.ServerCatalogStatus);
        Console.WriteLine("PASS: selected official Minecraft 1.21.1 / Forge 52.1.0.");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { vm.NewServerPort = ((IPEndPoint)listener.LocalEndpoint).Port.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        finally { listener.Stop(); }
        window.FindControl<TextBox>("NewServerNameInput")!.Text = "Disposable Forge verification";
        var create = window.FindControl<Button>("CreateLocalServerButton")!;
        if (!create.IsEffectivelyEnabled) throw new InvalidOperationException("Forge create button is disabled.");
        create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitAsync(() => vm.CanEditServer, TimeSpan.FromMinutes(8), "create Forge server");
        if (vm.ServerCards.SingleOrDefault() is not { } card)
            throw new InvalidOperationException("Create Forge server: " + vm.ServerStatus);
        await vm.AcceptServerEulaAsync(); // This explicit smoke invocation applies only to the owned test server.
        var running = vm.StartServerAsync(card);
        try
        {
            await WaitAsync(() => running.IsCompleted || card.Console.Contains("Done (", StringComparison.Ordinal),
                TimeSpan.FromMinutes(4), "Forge console readiness");
            if (running.IsCompleted || card.Session is not { } session)
                throw new InvalidOperationException("Start Forge server: " + card.Status);
            Console.WriteLine("PASS: Forge console reported Done.");
            await vm.StopServerAsync(card);
            await running.WaitAsync(TimeSpan.FromSeconds(40));
            if (await session.Completion != 0)
                throw new IOException("Forge server did not exit cleanly.");
        }
        catch
        {
            foreach (var line in card.Console.Split('\n').TakeLast(35)) Console.Error.WriteLine(line);
            throw;
        }
        finally
        {
            if (!running.IsCompleted)
            {
                card.Preparation?.Cancel();
                await vm.StopServerAsync(card);
                await running.WaitAsync(TimeSpan.FromSeconds(40));
            }
        }
        if (!File.Exists(SafePath.Join(vm.ServerManager.DirectoryFor(card.Server.Id), "world/level.dat")))
            throw new IOException("Forge stopped without saving a world.");
        await AtomicFile.WriteJsonAsync(receipt, new ForgeSmokeState(card.Server, await PropertiesHashAsync(card.Server)));
        Console.WriteLine("PASS: Forge stopped and saved its world.");

        async Task<string> PropertiesHashAsync(LocalVanillaServer server) =>
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(
                SafePath.Join(vm.ServerManager.DirectoryFor(server.Id), "server.properties"))));

        static async Task WaitAsync(Func<bool> ready, TimeSpan duration, string phase)
        {
            using var timeout = new CancellationTokenSource(duration);
            try { while (!ready()) await Task.Delay(30, timeout.Token); }
            catch (OperationCanceledException error) { throw new TimeoutException("Timed out at " + phase, error); }
        }
    }
}
