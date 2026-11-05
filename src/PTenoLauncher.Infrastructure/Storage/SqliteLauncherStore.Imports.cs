using PTenoLauncher.Core;
namespace PTenoLauncher.Infrastructure.Storage;

public sealed partial class SqliteLauncherStore
{
    // Import publication is insert-only. The ordinary profile editor's upsert is never used for this boundary.
    public async Task CommitImportedProfileAsync(GameProfile profile, CancellationToken ct = default)
    {
        ProfileRules.Validate(profile);
        var prior = (await GetProfilesAsync(ct)).SingleOrDefault(p => p.Id == profile.Id);
        if (prior is not null) { if (prior != profile) throw new InvalidDataException("Import profile identity conflict."); return; }
        await Task.Run(() =>
        {
            using var db = Open(); using var tx = db.BeginTransaction(); using var cmd = db.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO profiles VALUES($id,$name,$version,$loader,$memory,NULL,$created,0);
                INSERT INTO history(profile_id,at,action,details) VALUES($id,$created,'modpack.imported',$name);
                """;
            cmd.Parameters.AddWithValue("$id", profile.Id.ToString("N")); cmd.Parameters.AddWithValue("$name", profile.Name);
            cmd.Parameters.AddWithValue("$version", profile.MinecraftVersion); cmd.Parameters.AddWithValue("$loader", (int)profile.Loader);
            cmd.Parameters.AddWithValue("$memory", profile.MemoryMb); cmd.Parameters.AddWithValue("$created", profile.CreatedAt.ToString("O"));
            cmd.ExecuteNonQuery(); tx.Commit();
        }, ct);
    }
}
