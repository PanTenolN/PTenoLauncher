using PTenoLauncher.Core;
namespace PTenoLauncher.Infrastructure.Storage;

public sealed partial class SqliteLauncherStore
{
    public Task RegisterArtifactAsync(OwnedArtifact artifact, CancellationToken ct = default) => Task.Run(() =>
    {
        _ = SafePath.Join(paths.Root, artifact.RelativePath);
        if (artifact.Kind is not ("backup" or "export" or "translation-resourcepack")) throw new ArgumentException("Unknown artifact kind.");
        if (artifact.Kind == "translation-resourcepack" && !System.Text.RegularExpressions.Regex.IsMatch(artifact.RelativePath,
            @"\Ainstances/" + artifact.ProfileId.ToString("N") + @"/game/resourcepacks/pteno-translations-(?:uk_ua|en_us|pl_pl|de_de|es_es|fr_fr|pt_br|ru_ru)\.zip\z"))
            throw new InvalidDataException("Translation resource pack is outside its profile.");
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO owned_artifacts(profile_id,relative_path,kind) VALUES($id,$path,$kind) ON CONFLICT(relative_path) DO UPDATE SET profile_id=$id,kind=$kind";
        cmd.Parameters.AddWithValue("$id", artifact.ProfileId.ToString("N"));
        cmd.Parameters.AddWithValue("$path", artifact.RelativePath);
        cmd.Parameters.AddWithValue("$kind", artifact.Kind); cmd.ExecuteNonQuery();
    }, ct);
    public Task<IReadOnlyList<OwnedArtifact>> GetArtifactsAsync(Guid profileId, CancellationToken ct = default) => Task.Run<IReadOnlyList<OwnedArtifact>>(() =>
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT relative_path,kind FROM owned_artifacts WHERE profile_id=$id";
        cmd.Parameters.AddWithValue("$id", profileId.ToString("N"));
        using var reader = cmd.ExecuteReader(); var result = new List<OwnedArtifact>();
        while (reader.Read()) result.Add(new(profileId, reader.GetString(0), reader.GetString(1)));
        return result;
    }, ct);
    public Task DeleteProfileMetadataAsync(Guid profileId, CancellationToken ct = default) => Task.Run(() =>
    {
        using var db = Open(); using var tx = db.BeginTransaction(); using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM history WHERE profile_id=$id; DELETE FROM owned_artifacts WHERE profile_id=$id; DELETE FROM profiles WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", profileId.ToString("N")); cmd.ExecuteNonQuery(); tx.Commit();
    }, ct);
}
