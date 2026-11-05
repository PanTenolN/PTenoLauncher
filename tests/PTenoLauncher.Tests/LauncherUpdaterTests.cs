using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
using PTenoLauncher.Infrastructure.Updates;
using Xunit;
namespace PTenoLauncher.Tests;

public sealed class LauncherUpdaterTests
{
    internal sealed class Fixture : IDisposable
    {
        public TestWorkspace Ws { get; } = new();
        public LauncherPaths Paths { get; }
        public string AppRoot { get; }
        public NetworkPolicy Network { get; } = new();
        public LauncherBuild Current { get; } = new(10, "preview-ten", "0.2.0", "preview");
        public LauncherUpdateTrust Trust { get; }
        public LauncherUpdater Updater { get; }
        public SignedLauncherUpdates Envelope { get; set; }
        public LauncherRelease Release { get; }
        public byte[] Package { get; }
        public int Requests, PackageRequests;
        public bool CorruptPackage, FailServer;
        private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly HttpClient http;
        public Fixture(long sequence = 11, string channel = "preview", string mode = "Portable")
        {
            Paths = new(Path.Combine(Ws.Paths.Root, "data")); Paths.Initialize();
            AppRoot = Path.Combine(Ws.Paths.Root, "application"); Directory.CreateDirectory(AppRoot); File.WriteAllText(Path.Combine(AppRoot, "portable.flag"), "");
            var old = new Dictionary<string, byte[]> { ["PTenoLauncher.exe"] = "old synthetic exe"u8.ToArray(), ["PTenoLauncher.dll"] = "old synthetic dll"u8.ToArray() };
            var next = new Dictionary<string, byte[]> { ["PTenoLauncher.exe"] = "new synthetic exe"u8.ToArray(), ["PTenoLauncher.dll"] = "new synthetic dll"u8.ToArray(), ["new.dll"] = "new library"u8.ToArray() };
            foreach (var (name, data) in old) File.WriteAllBytes(Path.Combine(AppRoot, name), data);
            AtomicFile.Write(Path.Combine(AppRoot, LauncherUpdater.InstallationManifest), JsonSerializer.SerializeToUtf8Bytes(new ManagedLauncherInstallation(Current, Files(old)), AtomicFile.JsonOptions));
            using (var memory = new MemoryStream())
            {
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true)) foreach (var (name, data) in next) { using var s = zip.CreateEntry(name).Open(); s.Write(data); }
                Package = memory.ToArray();
            }
            var package = new LauncherUpdatePackage(mode, new("https://github.com/PanTenolN/PTenoLauncher/releases/download/test/package.zip"), Package.Length, Sha(Package), Files(next));
            Release = new(new(sequence, sequence == 10 ? Current.Id : "build-" + sequence, "0.2.0", channel), "Synthetic changes", [package]);
            Envelope = Sign(new(1, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1), [Release]));
            Trust = new(new Dictionary<string, string> { ["test-only"] = key.ExportSubjectPublicKeyInfoPem() });
            http = new(new StubHandler(request =>
            {
                Requests++; if (FailServer) return new(HttpStatusCode.ServiceUnavailable);
                if (request.RequestUri == LauncherUpdater.Feed) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(Envelope, AtomicFile.JsonOptions)) };
                PackageRequests++; var data = Package.ToArray(); if (CorruptPackage) data[^1] ^= 1;
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
            }));
            Updater = new(Paths, new(http, Network), Network, Trust, Current);
        }
        private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        private static LauncherUpdateFile[] Files(Dictionary<string, byte[]> files) => files.Select(p => new LauncherUpdateFile(p.Key, p.Value.Length, Sha(p.Value))).ToArray();
        public SignedLauncherUpdates Sign(LauncherUpdateIndex index)
        { var payload = JsonSerializer.SerializeToUtf8Bytes(index, AtomicFile.JsonOptions); return new("test-only", payload, key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)); }
        public async Task<LauncherUpdateOffer> Offer() => (await Updater.CheckAsync(Release.Packages[0].Mode, true)).Offer!;
        public void Dispose() { key.Dispose(); http.Dispose(); Ws.Dispose(); }
    }
    [Theory] [InlineData(9, "preview", false)] [InlineData(10, "preview", false)] [InlineData(11, "preview", true)] [InlineData(11, "stable", true)]
    public async Task OrderedBuildIdentityHandlesIdenticalBaseVersions(long sequence, string channel, bool expected)
    {
        using var f = new Fixture(sequence, channel); var result = await f.Updater.CheckAsync("Portable", true);
        Assert.Equal(expected, result.Offer is not null); Assert.Equal(0, f.PackageRequests);
    }
    [Fact] public async Task PreviewRequiresOptInAndStableCandidateIsAllowed()
    {
        using var f = new Fixture(); Assert.Null((await f.Updater.CheckAsync("Portable", false)).Offer);
        Assert.NotNull((await f.Updater.CheckAsync("Portable", true)).Offer);
        using var stable = new Fixture(channel: "stable"); Assert.NotNull((await stable.Updater.CheckAsync("Portable", false)).Offer);
    }
    [Fact] public async Task OfflineAndMissingProductionTrustMakeZeroRequests()
    {
        using var f = new Fixture(); f.Network.Offline = true;
        Assert.Equal("UpdaterOffline", (await f.Updater.CheckAsync("Portable", true)).State); Assert.Equal(0, f.Requests);
        Assert.False(LauncherUpdateTrust.Production.Configured);
        Assert.Throws<LauncherUpdateException>(() => LauncherUpdateTrust.Production.Verify(f.Envelope, DateTimeOffset.UtcNow));
    }
    [Theory] [InlineData("signature")] [InlineData("key")] [InlineData("expiry")] [InlineData("duplicate")] [InlineData("schema")]
    public async Task SignedMetadataFailsClosed(string kind)
    {
        using var f = new Fixture();
        if (kind == "signature") f.Envelope.Signature[0] ^= 1;
        if (kind == "key") f.Envelope = f.Envelope with { KeyId = "foreign" };
        if (kind == "expiry") f.Envelope = f.Sign(new(1, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1), [f.Release]));
        if (kind == "duplicate") f.Envelope = f.Sign(new(1, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1), [f.Release, f.Release]));
        if (kind == "schema") f.Envelope = f.Sign(new(1, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1), [f.Release with { Build = f.Release.Build with { DataSchema = 3 } }]));
        Assert.Null((await f.Updater.CheckAsync("Portable", true)).Offer); Assert.Equal(0, f.PackageRequests);
    }
    [Theory] [InlineData("../world.dat")] [InlineData("portable-data/world.dat")] [InlineData("secrets/token.dpapi")] [InlineData("unins000.exe")]
    public void UnsafePayloadPathsAreRejectedEvenWithTrustedSignature(string path)
    {
        using var f = new Fixture(); var p = f.Release.Packages[0]; var files = p.Files.Concat([p.Files[0] with { Path = path }]).ToArray();
        var envelope = f.Sign(new(1, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1), [f.Release with { Packages = [p with { Files = files }] }]));
        Assert.ThrowsAny<Exception>(() => f.Trust.Verify(envelope, DateTimeOffset.UtcNow));
    }
    [Fact] public async Task ExplicitConsentPrecedesAnyPackageDownloadAndCorruptDownloadNeverChangesApplication()
    {
        using var f = new Fixture(); var offer = await f.Offer();
        await Assert.ThrowsAsync<LauncherUpdateException>(() => f.Updater.PrepareAsync(offer, f.AppRoot, false)); Assert.Equal(0, f.PackageRequests);
        f.CorruptPackage = true; await Assert.ThrowsAsync<InvalidDataException>(() => f.Updater.PrepareAsync(offer, f.AppRoot, true));
        Assert.Equal("old synthetic exe", await File.ReadAllTextAsync(Path.Combine(f.AppRoot, "PTenoLauncher.exe")));
    }
    [Fact] public async Task UnavailableServerDoesNotBecomeAnUpdateOffer()
    { using var f = new Fixture { FailServer = true }; Assert.Equal("UpdaterUnavailable", (await f.Updater.CheckAsync("Portable", true)).State); }
    [Fact] public async Task PortableCoreApplyHealthAndBackupPreserveUnmanagedData()
    {
        using var f = new Fixture(); var sentinel = Path.Combine(f.Paths.Root, "synthetic-world.txt"); await File.WriteAllTextAsync(sentinel, "synthetic");
        await File.WriteAllTextAsync(Path.Combine(f.AppRoot, "user.txt"), "keep"); var transaction = await f.Updater.PrepareAsync(await f.Offer(), f.AppRoot, true);
        var read = await f.Updater.ReadTransactionAsync(transaction.Id); Assert.Equal(transaction.Id, read.Id); // Deserialized package/file identities remain exact.
        await f.Updater.ApplyPreparedAsync(transaction.Id);
        Assert.Equal("AwaitingHealth", (await f.Updater.ReadTransactionAsync(transaction.Id)).State);
        await Assert.ThrowsAsync<LauncherUpdateException>(() => f.Updater.ConfirmHealthAsync(transaction.Id, "wrong", f.Release.Build));
        await f.Updater.ConfirmHealthAsync(transaction.Id, transaction.HealthToken, f.Release.Build);
        Assert.Equal("Committed", (await f.Updater.ReadTransactionAsync(transaction.Id)).State);
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(f.AppRoot, "user.txt"))); Assert.Equal("synthetic", await File.ReadAllTextAsync(sentinel));
        Assert.True(File.Exists(Path.Combine(f.Updater.TransactionRoot(transaction.Id), "backup", "PTenoLauncher.exe")));
    }
    [Theory] [InlineData("applying")] [InlineData("old:PTenoLauncher.exe")] [InlineData("new:PTenoLauncher.exe")] [InlineData("awaiting-health")]
    public async Task PortableCoreInterruptionsRecoverOffline(string point)
    {
        using var f = new Fixture(); var t = await f.Updater.PrepareAsync(await f.Offer(), f.AppRoot, true);
        f.Updater.Fault = p => { if (p == point) throw new OperationCanceledException(); };
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Updater.ApplyPreparedAsync(t.Id));
        f.Updater.Fault = null; f.Network.Offline = true; var requests = f.Requests; await f.Updater.RollbackAsync(t.Id);
        Assert.Equal(requests, f.Requests); Assert.Equal("old synthetic exe", await File.ReadAllTextAsync(Path.Combine(f.AppRoot, "PTenoLauncher.exe"))); Assert.False(File.Exists(Path.Combine(f.AppRoot, "new.dll")));
        Assert.Equal("RolledBack", (await f.Updater.ReadTransactionAsync(t.Id)).State);
    }
    [Theory] [InlineData("user")] [InlineData("backup")]
    public async Task PortableCoreRefusesToOverwriteChangedFilesOrUseCorruptBackup(string what)
    {
        using var f = new Fixture(); var t = await f.Updater.PrepareAsync(await f.Offer(), f.AppRoot, true); await f.Updater.ApplyPreparedAsync(t.Id);
        var file = what == "user" ? Path.Combine(f.AppRoot, "PTenoLauncher.dll") : Path.Combine(f.Updater.TransactionRoot(t.Id), "backup", "PTenoLauncher.dll");
        await File.WriteAllTextAsync(file, "user bytes"); await Assert.ThrowsAsync<LauncherUpdateException>(() => f.Updater.RollbackAsync(t.Id));
        Assert.Equal("user bytes", await File.ReadAllTextAsync(file));
    }
    [Fact] public async Task InstalledModeNeverFallsBackToPortableOrBypassesRegistryGuard()
    {
        using var f = new Fixture(mode: "Installed"); var offer = await f.Offer(); var error = await Assert.ThrowsAsync<LauncherUpdateException>(() => f.Updater.PrepareAsync(offer, f.AppRoot, true));
        Assert.Equal("UpdaterInstalledBlocked", error.Code); Assert.Equal(0, f.PackageRequests);
    }
}
