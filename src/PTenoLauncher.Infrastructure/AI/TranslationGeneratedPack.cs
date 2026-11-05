using PTenoLauncher.Infrastructure.Storage;
using PTenoLauncher.Infrastructure.Network;
namespace PTenoLauncher.Infrastructure.AI;

public sealed partial class TranslationWorkspace
{
    private sealed record GeneratedPackProof(string RelativePath, string Sha256, long Size);
    private sealed record GeneratedPackTransaction(GeneratedPackProof? Previous, GeneratedPackProof Next);
    private static Task<bool> VerifyGeneratedPack(string file, string relative, GeneratedPackProof? proof, CancellationToken ct)
        => proof is null || proof.RelativePath != relative || proof.Size is < 1 or > 32 * 1024 * 1024 ||
           proof.Sha256 is null || !System.Text.RegularExpressions.Regex.IsMatch(proof.Sha256, "\\A[0-9A-F]{64}\\z")
            ? Task.FromResult(false) : VerifiedDownloader.VerifyAsync(file, proof.Sha256, "SHA256", proof.Size, ct);
    private async Task RecoverGeneratedPack(Guid profile, string relative, string target, string proofPath, string pendingPath, CancellationToken ct)
    {
        var pending = await AtomicFile.ReadJsonAsync<GeneratedPackTransaction>(pendingPath, ct);
        if (pending is null) return;
        if (await VerifyGeneratedPack(target, relative, pending.Next, ct))
        {
            await store.RegisterArtifactAsync(new(profile, relative, "translation-resourcepack"), ct);
            await AtomicFile.WriteJsonAsync(proofPath, pending.Next, ct);
        }
        else if (pending.Previous is not null && await VerifyGeneratedPack(target, relative, pending.Previous, ct))
            await AtomicFile.WriteJsonAsync(proofPath, pending.Previous, ct);
        else if (File.Exists(target) || pending.Previous is not null)
            throw new IOException("Interrupted resource pack publication conflicts with current files. The previous pack backup was preserved.");
        SafePath.RejectLinks(pendingPath); File.Delete(pendingPath);
    }
}
