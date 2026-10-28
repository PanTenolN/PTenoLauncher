using Microsoft.Data.Sqlite;
using PTenoLauncher.Core;

namespace PTenoLauncher.Infrastructure.Storage;

public sealed partial class SqliteLauncherStore(LauncherPaths paths) : ILauncherStore
{
    private SqliteConnection Open()
    {
        SafePath.RejectLinks(paths.Database);
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.Database, ForeignKeys = true, DefaultTimeout = 10 }.ToString());
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA synchronous=FULL;";
        cmd.ExecuteNonQuery();
        return db;
    }
    public Task InitializeAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        paths.Initialize();
        using var db = Open();
        using var version = db.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        if (Convert.ToInt32(version.ExecuteScalar()) > 2) throw new InvalidDataException("Сховище створене новішою версією PTenoLauncher.");
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            BEGIN IMMEDIATE;
            CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS profiles(id TEXT PRIMARY KEY, name TEXT NOT NULL, version TEXT NOT NULL, loader INTEGER NOT NULL, memory INTEGER NOT NULL, java TEXT, created TEXT NOT NULL, archived INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS history(id INTEGER PRIMARY KEY, profile_id TEXT, at TEXT NOT NULL, action TEXT NOT NULL, details TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS owned_artifacts(profile_id TEXT NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,relative_path TEXT PRIMARY KEY,kind TEXT NOT NULL);
            PRAGMA user_version=2;
            COMMIT;
            """;
        cmd.ExecuteNonQuery();
    }, ct);
    public Task<IReadOnlyList<GameProfile>> GetProfilesAsync(CancellationToken ct = default) => Task.Run<IReadOnlyList<GameProfile>>(() =>
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id,name,version,loader,memory,java,created,archived FROM profiles ORDER BY created";
        using var reader = cmd.ExecuteReader(); var list = new List<GameProfile>();
        while (reader.Read())
        {
            var p = new GameProfile(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), (ModLoader)reader.GetInt32(3), reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetString(5), DateTimeOffset.Parse(reader.GetString(6)), reader.GetBoolean(7));
            ProfileRules.Validate(p); list.Add(p);
        }
        return list;
    }, ct);
    public Task SaveProfileAsync(GameProfile profile, CancellationToken ct = default) => Task.Run(() =>
    {
        ProfileRules.Validate(profile); paths.CreateProfile(profile);
        using var db = Open(); using var tx = db.BeginTransaction(); using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO profiles VALUES($id,$name,$version,$loader,$memory,$java,$created,$archived)
            ON CONFLICT(id) DO UPDATE SET name=$name,version=$version,loader=$loader,memory=$memory,java=$java,archived=$archived;
            INSERT INTO history(profile_id,at,action,details) VALUES($id,$at,'profile.saved',$name);
            """;
        cmd.Parameters.AddWithValue("$id", profile.Id.ToString("N"));
        cmd.Parameters.AddWithValue("$name", profile.Name.Trim());
        cmd.Parameters.AddWithValue("$version", profile.MinecraftVersion);
        cmd.Parameters.AddWithValue("$loader", (int)profile.Loader);
        cmd.Parameters.AddWithValue("$memory", profile.MemoryMb);
        cmd.Parameters.AddWithValue("$java", (object?)profile.JavaPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", profile.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$archived", profile.Archived);
        cmd.ExecuteNonQuery(); tx.Commit();
    }, ct);
    public Task<string?> GetSettingAsync(string key, CancellationToken ct = default) => Task.Run(() =>
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key=$key";
        cmd.Parameters.AddWithValue("$key", key); return cmd.ExecuteScalar() as string;
    }, ct);
    public Task SetSettingAsync(string key, string value, CancellationToken ct = default) => Task.Run(() =>
    {
        if (key.Contains("token", StringComparison.OrdinalIgnoreCase) || key.Contains("secret", StringComparison.OrdinalIgnoreCase) || key.Contains("password", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Секрети мають зберігатися у захищеному сховищі.");
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO settings VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=$value";
        cmd.Parameters.AddWithValue("$key", key); cmd.Parameters.AddWithValue("$value", value); cmd.ExecuteNonQuery();
    }, ct);
    public Task AddHistoryAsync(Guid? profileId, string action, string details, CancellationToken ct = default) => Task.Run(() =>
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO history(profile_id,at,action,details) VALUES($id,$at,$action,$details)";
        cmd.Parameters.AddWithValue("$id", profileId is null ? DBNull.Value : profileId.Value.ToString("N"));
        cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$action", action); cmd.Parameters.AddWithValue("$details", details); cmd.ExecuteNonQuery();
    }, ct);
    public Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(Guid? profileId, CancellationToken ct = default) => Task.Run<IReadOnlyList<HistoryEntry>>(() =>
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT at,action,details FROM history WHERE $id IS NULL OR profile_id=$id ORDER BY id DESC LIMIT 100";
        cmd.Parameters.AddWithValue("$id", profileId is null ? DBNull.Value : profileId.Value.ToString("N"));
        using var r = cmd.ExecuteReader(); var list = new List<HistoryEntry>();
        while (r.Read()) list.Add(new(DateTimeOffset.Parse(r.GetString(0)), r.GetString(1), r.GetString(2)));
        return list;
    }, ct);
}