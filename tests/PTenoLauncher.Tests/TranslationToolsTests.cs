using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.AI;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;
namespace PTenoLauncher.Tests;

public sealed class TranslationToolsTests
{
    internal static string Jar(AiUxFixture f, int count = 12, string source = "fr_fr", bool extra = false)
    {
        var file = Path.Combine(f.Workspace.Paths.Root, Guid.NewGuid().ToString("N") + ".jar");
        using var z = ZipFile.Open(file, ZipArchiveMode.Create);
        using (var stream = z.CreateEntry("assets/demo/lang/" + source + ".json").Open()) JsonSerializer.Serialize(stream, Enumerable.Range(0, count).ToDictionary(n => "key." + n.ToString("D4"), n => "Original " + n + " %s"));
        if (extra)
        {
            foreach (var path in new[] { "assets/demo/patchouli_books/book/fr_fr/entries/chapter.json", "META-INF/jars/nested.jar", "assets/demo/lang/en_us.lang", "assets/demo/lang/de_de.json" })
            { using var stream = z.CreateEntry(path).Open(); stream.Write("{\"component\":{\"text\":\"Not supported\"}}"u8); }
        }
        return file;
    }
    internal static async Task<GameProfile> Profile(AiUxFixture f)
    { await f.Workspace.Store.InitializeAsync(); var p = f.Workspace.Profile() with { MinecraftVersion = "1.21" }; await f.Workspace.Store.SaveProfileAsync(p); return p; }
    [Fact] public async Task DiscoveryReportsNamespaceCountsAndUnsupportedBookNestedLegacyAndComponentResources()
    {
        using var f = new AiUxFixture(); var jar = Jar(f, extra: true); var before = File.ReadAllBytes(jar);
        var found = Assert.Single(await f.Translations.DiscoverAsync([jar])); Assert.Equal("fr_fr", Assert.Single(found.Locales)); Assert.Equal(12, Assert.Single(found.Resources).Strings); Assert.Equal("demo", found.Resources[0].Namespace); Assert.Equal(4, found.Unsupported.Count);
        Assert.Equal(before, File.ReadAllBytes(jar)); Assert.Equal(0, f.Calls); Assert.Equal(0, f.KeyReads);
    }
    [Fact] public async Task TrialSendsExactlyChosenUntrustedSourceKeysAndPreservesOtherPendingAndManualRows()
    {
        using var f = new AiUxFixture(); var p = await Profile(f); await f.Translations.ImportAsync(p, [Jar(f)], "uk_ua", sourceLocale: "fr_fr");
        await f.Translations.SaveManualAsync(p, "uk_ua", "demo", new Dictionary<string,string> { ["key.0000"] = "Manual %s" }, sourceLocale: "fr_fr");
        var plan = await f.Translations.PreviewSelectionAsync(p, "fr_fr", "uk_ua", "demo", "fixture-model", ["key.0002", "key.0001"]); Assert.Equal(0, f.Calls); Assert.Equal(0, f.KeyReads);
        f.Policy.SetEnabled(true); f.Respond = async (r, ct) =>
        {
            using var body = JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct)); var root = body.RootElement;
            Assert.Contains("untrusted", root.GetProperty("instructions").GetString()); Assert.False(root.GetProperty("store").GetBoolean()); Assert.False(root.TryGetProperty("tools", out _));
            using var input = JsonDocument.Parse(root.GetProperty("input").GetString()!); Assert.Equal(2, input.RootElement.EnumerateObject().Count()); Assert.Equal("Original 1 %s", input.RootElement.GetProperty("key.0001").GetString()); Assert.False(input.RootElement.TryGetProperty("key.0000", out _));
            return AiUxFixture.Completed("{\"translations\":{\"key.0001\":\"Один %s\",\"key.0002\":\"Два %s\"}}");
        };
        await f.Translations.TranslateSelectionAsync(p, plan, true); var draft = (await f.Translations.ReadAsync(p.Id, "uk_ua", "demo", sourceLocale: "fr_fr"))!;
        Assert.Equal(3, draft.Values.Count); Assert.True(draft.Values["key.0000"].Manual); Assert.False(draft.Values.ContainsKey("key.0003")); Assert.Equal(1, f.Calls); Assert.Equal(1, f.KeyReads);
    }
    [Theory][InlineData("same")][InlineData("empty")][InlineData("large")][InlineData("unknown")][InlineData("translated")]
    public async Task InvalidOrSameLanguageTrialNeverReadsKey(string kind)
    {
        using var f = new AiUxFixture(); var p = await Profile(f); await f.Translations.ImportAsync(p, [Jar(f, 40)], "uk_ua", sourceLocale: "fr_fr");
        if (kind == "translated") await f.Translations.SaveManualAsync(p, "uk_ua", "demo", new Dictionary<string,string> { ["key.0000"] = "Manual %s" }, sourceLocale: "fr_fr");
        IReadOnlyList<string> keys = kind switch { "empty" => [], "large" => Enumerable.Range(0,33).Select(n => "key." + n.ToString("D4")).ToArray(), "unknown" => ["missing"], _ => ["key.0000"] };
        await Assert.ThrowsAsync<AiServiceException>(() => f.Translations.PreviewSelectionAsync(p, "fr_fr", kind == "same" ? "fr_fr" : "uk_ua", "demo", "fixture-model", keys)); Assert.Equal(0, f.KeyReads); Assert.Equal(0, f.Calls);
    }
    [Fact] public async Task ChangedSelectionModelOrDraftRequiresFreshConsentBeforeProviderCall()
    {
        using var f = new AiUxFixture(); var p = await Profile(f); f.Policy.SetEnabled(true); await f.Translations.ImportAsync(p, [Jar(f)], "uk_ua", sourceLocale: "fr_fr"); var plan = await f.Translations.PreviewSelectionAsync(p, "fr_fr", "uk_ua", "demo", "fixture-model", ["key.0001"]);
        await Assert.ThrowsAsync<AiServiceException>(() => f.Translations.TranslateSelectionAsync(p, plan, false));
        await Assert.ThrowsAsync<AiServiceException>(() => f.Translations.TranslateSelectionAsync(p, plan with { Model = "other" }, true));
        await Assert.ThrowsAsync<AiServiceException>(() => f.Translations.TranslateSelectionAsync(p, plan with { Keys = ["key.0002"] }, true));
        await f.Translations.SaveManualAsync(p, "uk_ua", "demo", new Dictionary<string,string> { ["key.0000"] = "Manual %s" }, sourceLocale: "fr_fr");
        await Assert.ThrowsAsync<AiServiceException>(() => f.Translations.TranslateSelectionAsync(p, plan, true)); Assert.Equal(0, f.Calls); Assert.Equal(0, f.KeyReads);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task CancelOrDamagedTrialOutputCannotWritePartialTranslations(bool cancel)
    {
        using var f = new AiUxFixture(); var p = await Profile(f); f.Policy.SetEnabled(true); await f.Translations.ImportAsync(p, [Jar(f)], "uk_ua", sourceLocale: "fr_fr"); var plan = await f.Translations.PreviewSelectionAsync(p, "fr_fr", "uk_ua", "demo", "fixture-model", ["key.0001"]);
        using var stop = new CancellationTokenSource(); f.Respond = (_, _) => { if (cancel) stop.Cancel(); return Task.FromResult(AiUxFixture.Completed("{\"translations\":{\"key.0001\":\"Broken no placeholder\"}}")); };
        await Assert.ThrowsAnyAsync<Exception>(() => f.Translations.TranslateSelectionAsync(p, plan, true, stop.Token)); Assert.Empty((await f.Translations.ReadAsync(p.Id, "uk_ua", "demo", sourceLocale: "fr_fr"))!.Values);
    }
    [Fact] public async Task ConfirmedDeletionKeepsPackOtherDirectionsWorldsAndMigrationBackupsWithoutResurrection()
    {
        using var f = new AiUxFixture(); var p = await Profile(f); var jar = f.Jar("en_us"); await f.Translations.ImportAsync(p, [jar], "uk_ua"); await f.Translations.SaveManualAsync(p, "uk_ua", "demo", new Dictionary<string,string> { ["hello"] = "Manual %s" });
        var pack = await f.Translations.ExportAsync(p, "uk_ua", 34); var before = File.ReadAllBytes(pack.Path);
        await f.Translations.ImportAsync(p, [jar], "de_de"); var legacy = Path.Combine(f.Workspace.Paths.Profile(p.Id), "translations/uk_ua/demo.json"); Directory.CreateDirectory(Path.GetDirectoryName(legacy)!); await File.WriteAllBytesAsync(legacy, await File.ReadAllBytesAsync(Path.Combine(f.Workspace.Paths.Profile(p.Id), "translations/v2/en_us/uk_ua/demo.json")));
        var world = Path.Combine(f.Workspace.Paths.Game(p.Id), "saves/synthetic.txt"); File.WriteAllText(world, "KEEP");
        var preview = await f.Translations.PreviewDeleteAsync(p, "en_us", "uk_ua", "demo"); Assert.True(preview.Bytes > 0); await Assert.ThrowsAsync<AiServiceException>(() => f.Translations.DeleteAsync(p, preview, false));
        await f.Translations.DeleteAsync(p, preview, true); Assert.Null(await f.Translations.ReadAsync(p.Id, "uk_ua", "demo")); Assert.Single(await f.Translations.LibraryAsync(p.Id));
        Assert.Equal(before, File.ReadAllBytes(pack.Path)); Assert.True(File.Exists(legacy)); Assert.Equal("KEEP", File.ReadAllText(world)); Assert.Equal(0, f.Calls); Assert.Equal(0, f.KeyReads);
        await f.Translations.ImportAsync(p, [jar], "uk_ua"); Assert.NotNull(await f.Translations.ReadAsync(p.Id, "uk_ua", "demo"));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task DeleteRejectsNewerDraftOrOtherProfileAndInterruptedIntentIsRetryable(bool interrupted)
    {
        using var f = new AiUxFixture(); var p = await Profile(f); await f.Translations.ImportAsync(p, [f.Jar()], "uk_ua", sourceLocale: "fr_fr"); var plan = await f.Translations.PreviewDeleteAsync(p, "fr_fr", "uk_ua", "demo");
        if (interrupted)
        {
            f.Translations.TranslationFault = _ => throw new IOException("Controlled interruption"); await Assert.ThrowsAsync<IOException>(() => f.Translations.DeleteAsync(p, plan, true));
            Assert.NotNull(await f.Translations.ReadAsync(p.Id, "uk_ua", "demo", sourceLocale: "fr_fr")); f.Translations.TranslationFault = null; await f.Translations.DeleteAsync(p, plan, true); Assert.Null(await f.Translations.ReadAsync(p.Id, "uk_ua", "demo", sourceLocale: "fr_fr"));
        }
        else
        { await f.Translations.SaveManualAsync(p, "uk_ua", "demo", new Dictionary<string,string> { ["hello"] = "Newer %s" }, sourceLocale: "fr_fr"); await Assert.ThrowsAsync<AiServiceException>(() => f.Translations.DeleteAsync(p, plan, true)); Assert.Equal("Newer %s", (await f.Translations.ReadAsync(p.Id, "uk_ua", "demo", sourceLocale: "fr_fr"))!.Values["hello"].Value); }
    }
    [Fact] public async Task ExplicitLocalClientEvidenceSupportsUnknownExactVersionOfflineWithoutInstallingGame()
    {
        using var f = new AiUxFixture(); var p = (await Profile(f)) with { MinecraftVersion = "1.20.1" }; await f.Workspace.Store.SaveProfileAsync(p);
        var jar = Path.Combine(f.Workspace.Paths.Root, "synthetic-client.jar"); using (var z = ZipFile.Open(jar, ZipArchiveMode.Create)) { using var stream = z.CreateEntry("version.json").Open(); JsonSerializer.Serialize(stream, new { id = "1.20.1", pack_version = new { resource = 15 } }); }
        var before = File.ReadAllBytes(jar); await TranslationPackMetadataResolver.ImportLocalClientAsync(f.Workspace.Paths, p, jar); File.Delete(jar);
        Assert.Equal(15, (await TranslationPackMetadataResolver.ResolveAsync(f.Workspace.Paths, p)).Format); Assert.False(File.Exists(f.Workspace.Paths.Receipt(p.Id))); Assert.Equal(0, f.Calls); Assert.NotEmpty(before);
        await Assert.ThrowsAsync<AiServiceException>(() => TranslationPackMetadataResolver.ResolveAsync(f.Workspace.Paths, p with { MinecraftVersion = "1.20.1-new" }));
    }
    [Theory][InlineData("1.19.4",13)][InlineData("1.20.2",18)][InlineData("1.20.4",22)][InlineData("1.20.5",32)]
    public async Task ExtendedCatalogContainsOnlyExplicitPrimaryVersionEvidence(string version,int format)
    { using var f = new AiUxFixture(); var p = (await Profile(f)) with { MinecraftVersion = version }; var metadata = await TranslationPackMetadataResolver.ResolveAsync(f.Workspace.Paths,p); Assert.Equal(format,metadata.Format); Assert.StartsWith("https://www.minecraft.net/",metadata.Evidence); }
}
