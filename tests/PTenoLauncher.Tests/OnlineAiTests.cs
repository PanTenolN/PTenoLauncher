using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.AI;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class OnlineAiTests
{
    private static HttpResponseMessage Completed(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "output_text", text } } } } }))
    };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; return respond(request, ct); }
    }
    private static OpenAiClient Client(HttpClient http, AiPolicy policy, NetworkPolicy? network = null) => new(http, policy, network ?? new(), () => "synthetic-api-key-only");
    private static async Task<HttpResponseMessage> Echo(HttpRequestMessage request, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        var input = JsonSerializer.Deserialize<Dictionary<string, string>>(json.RootElement.GetProperty("input").GetString()!)!;
        return Completed(JsonSerializer.Serialize(new { translations = input.ToDictionary(p => p.Key, p => "Переклад " + p.Value) }));
    }
    private static string Jar(TestWorkspace ws, string name, IReadOnlyDictionary<string, string> values, string resourceNamespace = "synthetic")
    {
        var file = Path.Combine(ws.Paths.Root, name);
        using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        using var stream = zip.CreateEntry($"assets/{resourceNamespace}/lang/en_us.json").Open();
        JsonSerializer.Serialize(stream, values); return file;
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public async Task DisabledOfflineOrUnconfirmedOperationNeverReadsKeyOrSendsRequest(bool enabled, bool offline, bool consent)
    {
        var policy = new AiPolicy(); policy.SetEnabled(enabled); var reads = 0;
        using var handler = new Handler((_, _) => Task.FromResult(Completed("must not happen")));
        using var http = new HttpClient(handler);
        var client = new OpenAiClient(http, policy, new() { Offline = offline }, () => { reads++; return "synthetic-api-key-only"; });
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.AskAsync("fixture-model", "question", consent));
        Assert.Equal(0, handler.Calls); Assert.Equal(0, reads);
    }

    [Fact]
    public async Task AssistantUsesFixedEndpointNoToolsNoStoreAndRedactedSelectedInput()
    {
        var policy = new AiPolicy(); policy.SetEnabled(true);
        using var handler = new Handler(async (request, ct) =>
        {
            Assert.Equal(OpenAiClient.Endpoint, request.RequestUri!.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("synthetic-api-key-only", request.Headers.Authorization.Parameter);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = json.RootElement;
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.False(root.TryGetProperty("tools", out _));
            Assert.Equal(2048, root.GetProperty("max_output_tokens").GetInt32());
            var input = root.GetProperty("input").GetString()!;
            Assert.DoesNotContain("private-token", input);
            Assert.DoesNotContain("person@example.com", input);
            Assert.DoesNotContain("192.168.1.12", input);
            return Completed("Synthetic advice; no actual game diagnosis.");
        });
        using var http = new HttpClient(handler); var client = Client(http, policy);
        var answer = await client.AskAsync("fixture-model", "Bearer private-token person@example.com 192.168.1.12", true, true);
        Assert.Contains("Synthetic advice", answer); Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task DisablingAiCancelsInFlightRequestWithoutRetry()
    {
        var policy = new AiPolicy(); policy.SetEnabled(true);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async (_, ct) => { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); return Completed("unreachable"); });
        using var http = new HttpClient(handler); var operation = Client(http, policy).AskAsync("fixture-model", "selected question", true);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); policy.SetEnabled(false);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("Hello %s", "Привіт")]
    [InlineData("Value {name}", "Значення")]
    [InlineData("§aHello", "Привіт")]
    [InlineData("%1$d items", "%2$d речей")]
    [InlineData("%s has %d items", "%d речей має %s")]
    public void TranslationCannotRemoveOrChangePlaceholders(string original, string translated) =>
        Assert.Throws<InvalidDataException>(() => TranslationRules.Value(original, translated));

    [Fact]
    public async Task InvalidAiOutputNeverCommitsTranslationAndHttpErrorsNeverExposeBody()
    {
        var policy = new AiPolicy(); policy.SetEnabled(true);
        using var bad = new Handler((_, _) => Task.FromResult(Completed("{\"translations\":{\"a\":\"lost placeholder\"}}")));
        using var http = new HttpClient(bad);
        await Assert.ThrowsAsync<InvalidDataException>(() => Client(http, policy).TranslateAsync("fixture-model", "uk_ua", new Dictionary<string, string> { ["a"] = "%s" }, true));
        using var failure = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("private-provider-body") }));
        using var failedHttp = new HttpClient(failure);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Client(failedHttp, policy).AskAsync("fixture-model", "question", true));
        Assert.Contains("429", error.Message); Assert.DoesNotContain("private-provider-body", error.Message); Assert.Equal(1, failure.Calls);
    }

    [Fact]
    public async Task IncrementalTranslationPreservesManualEditsAndExportsOfflineWithoutChangingJarOrWorlds()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync(); var profile = ws.Profile(); await ws.Store.SaveProfileAsync(profile);
        var world = Path.Combine(ws.Paths.Game(profile.Id), "saves", "synthetic-sentinel"); await File.WriteAllTextAsync(world, "keep");
        var policy = new AiPolicy(); policy.SetEnabled(true);
        using var handler = new Handler(Echo); using var http = new HttpClient(handler);
        var workspace = new TranslationWorkspace(ws.Paths, ws.Store, new(ws.Paths), policy, Client(http, policy));
        var original = Jar(ws, "source-one.jar", new Dictionary<string, string> { ["a"] = "Hello %s", ["b"] = "Bye" });
        var bytes = await File.ReadAllBytesAsync(original);
        await workspace.ImportAsync(profile, [original], "uk_ua");
        await workspace.TranslateAsync(profile, "uk_ua", "fixture-model", true);
        await workspace.TranslateAsync(profile, "uk_ua", "fixture-model", true);
        Assert.Equal(1, handler.Calls); // Already translated strings are never sent again.
        await workspace.SaveManualAsync(profile, "uk_ua", "synthetic", new Dictionary<string, string> { ["a"] = "Ручний %s" });
        var updated = Jar(ws, "source-two.jar", new Dictionary<string, string> { ["a"] = "Changed %s", ["b"] = "Bye", ["c"] = "New" });
        var preview = Assert.Single(await workspace.ImportAsync(profile, [updated], "uk_ua"));
        Assert.Equal(1, preview.Missing); Assert.Equal(1, preview.Manual); Assert.Equal(1, preview.Review);
        await workspace.TranslateAsync(profile, "uk_ua", "fixture-model", true); Assert.Equal(2, handler.Calls);
        var draft = (await workspace.ReadAsync(profile.Id, "uk_ua", "synthetic"))!;
        Assert.Equal("Ручний %s", draft.Values["a"].Value); Assert.True(draft.Values["a"].NeedsReview);
        policy.SetEnabled(false);
        var partial = await workspace.ExportAsync(profile, "uk_ua", 34);
        Assert.Equal(1, partial.Review); Assert.Equal(2, partial.Strings);
        await workspace.SaveManualAsync(profile, "uk_ua", "synthetic", new Dictionary<string, string> { ["a"] = "Перевірено %s" });
        var pack = await workspace.ExportAsync(profile, "uk_ua", 34);
        using var zip = ZipFile.OpenRead(pack.Path); Assert.NotNull(zip.GetEntry("pack.mcmeta"));
        using var lang = JsonDocument.Parse(zip.GetEntry("assets/synthetic/lang/uk_ua.json")!.Open());
        Assert.Equal("Перевірено %s", lang.RootElement.GetProperty("a").GetString()); Assert.Equal(3, pack.Strings);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(original)); Assert.Equal("keep", await File.ReadAllTextAsync(world));
        Assert.Equal(2, handler.Calls);
        var plan = await new ProfileDeletionService(ws.Paths, ws.Store, new(ws.Paths)).PreviewAsync(profile.Id, false);
        Assert.Contains(plan.Files, f => f.RelativePath.EndsWith("pteno-translations-uk_ua.zip"));
    }

    [Fact]
    public async Task SingleNamespaceSelectionDoesNotUploadOtherImportedMods()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync(); var profile = ws.Profile(); await ws.Store.SaveProfileAsync(profile);
        var policy = new AiPolicy(); policy.SetEnabled(true);
        using var handler = new Handler(Echo); using var http = new HttpClient(handler);
        var workspace = new TranslationWorkspace(ws.Paths, ws.Store, new(ws.Paths), policy, Client(http, policy));
        var a = Jar(ws, "selected.jar", new Dictionary<string, string> { ["a"] = "Selected" }, "selected");
        var b = Jar(ws, "other.jar", new Dictionary<string, string> { ["b"] = "Not selected" }, "other");
        await workspace.ImportAsync(profile, [a, b], "uk_ua");
        await workspace.TranslateAsync(profile, "uk_ua", "fixture-model", true, onlyNamespace: "selected");
        Assert.Single((await workspace.ReadAsync(profile.Id, "uk_ua", "selected"))!.Values);
        Assert.Empty((await workspace.ReadAsync(profile.Id, "uk_ua", "other"))!.Values);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task InterruptedBatchesResumeFromAtomicCheckpointAndDoNotOverwriteUntrackedPack()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync(); var profile = ws.Profile(); await ws.Store.SaveProfileAsync(profile);
        var policy = new AiPolicy(); policy.SetEnabled(true); var count = 0;
        using var handler = new Handler(async (request, ct) => { if (++count == 2) return new(HttpStatusCode.ServiceUnavailable); return await Echo(request, ct); });
        using var http = new HttpClient(handler);
        var workspace = new TranslationWorkspace(ws.Paths, ws.Store, new(ws.Paths), policy, Client(http, policy));
        var source = Jar(ws, "many.jar", Enumerable.Range(0, 66).ToDictionary(i => "k" + i.ToString("D3"), i => "Item " + i));
        await workspace.ImportAsync(profile, [source], "uk_ua");
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.TranslateAsync(profile, "uk_ua", "fixture-model", true));
        Assert.Equal(32, (await workspace.ReadAsync(profile.Id, "uk_ua", "synthetic"))!.Values.Count);
        await workspace.TranslateAsync(profile, "uk_ua", "fixture-model", true);
        Assert.Equal(66, (await workspace.ReadAsync(profile.Id, "uk_ua", "synthetic"))!.Values.Count); Assert.Equal(4, count);
        var target = Path.Combine(ws.Paths.Game(profile.Id), "resourcepacks", "pteno-translations-uk_ua.zip");
        await File.WriteAllTextAsync(target, "synthetic user file");
        await Assert.ThrowsAsync<IOException>(() => workspace.ExportAsync(profile, "uk_ua", 34));
        Assert.Equal("synthetic user file", await File.ReadAllTextAsync(target));
    }

    [AvaloniaFact]
    public async Task ActualAiTabIsHiddenByDefaultAndKeyIsProtectedLocallyWithoutNetwork()
    {
        using var ws = new TestWorkspace(); using var services = new LauncherServices(ws.Paths);
        var vm = new MainViewModel(services); await vm.InitializeAsync();
        var window = new MainWindow { DataContext = vm }; window.Show();
        try
        {
            Assert.False(vm.AiEnabled); Assert.False(window.FindControl<TabItem>("AiAssistantTab")!.IsVisible);
            vm.AiEnabled = true; Assert.True(window.FindControl<TabItem>("AiAssistantTab")!.IsVisible);
            Assert.NotEqual('\0', window.FindControl<TextBox>("AiKeyInput")!.PasswordChar);
            vm.AiKey = "synthetic-local-key-only"; vm.AiModel = "fixture-model";
            await vm.SaveAiConfigurationAsync(); Assert.True(vm.LastOperationSucceeded, vm.Status);
            Assert.Empty(vm.AiKey); Assert.NotNull(services.AiSecrets.Read("openai-api-key"));
            Assert.Null(await services.Store.GetSettingAsync("ai.openai.key"));
            vm.AiEnabled = false;
            await vm.DeleteAiKeyAsync(); Assert.Null(services.AiSecrets.Read("openai-api-key"));
        }
        finally { window.Close(); }
    }
}
