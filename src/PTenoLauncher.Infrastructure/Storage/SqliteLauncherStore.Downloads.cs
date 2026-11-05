using System.Diagnostics;

namespace PTenoLauncher.Infrastructure.Storage;

public sealed record DownloadHistoryEntry(string Title, string Profile, DateTimeOffset At, string Result, string? Detail = null);

public sealed partial class SqliteLauncherStore
{
    public Task StartDownloadAsync(Guid id, string title, string profile) => Task.Run(() =>
    {
        using var process = Process.GetCurrentProcess();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO download_history VALUES($id,$title,$profile,$at,'Running',$pid,$start)";
        cmd.Parameters.AddWithValue("$id", id.ToString("N"));
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$profile", profile);
        cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$pid", process.Id);
        cmd.Parameters.AddWithValue("$start", process.StartTime.ToUniversalTime().Ticks);
        cmd.ExecuteNonQuery();
    });

    public Task FinishDownloadAsync(Guid id, string result, string? detail = null) => Task.Run(() =>
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE download_history SET result=$result WHERE id=$id AND result='Running'";
        cmd.Parameters.AddWithValue("$id", id.ToString("N"));
        cmd.Parameters.AddWithValue("$result", result);
        if (cmd.ExecuteNonQuery() == 1 && !string.IsNullOrEmpty(detail))
        {
            cmd.CommandText = "INSERT INTO download_history_details VALUES($id,$detail)";
            cmd.Parameters.AddWithValue("$detail", detail);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    });

    public Task<IReadOnlyList<DownloadHistoryEntry>> GetDownloadHistoryAsync() => Task.Run<IReadOnlyList<DownloadHistoryEntry>>(() =>
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT title,profile,at,result,pid,process_start,detail FROM download_history
            LEFT JOIN download_history_details USING(id) ORDER BY at DESC LIMIT 200
            """;
        using var reader = cmd.ExecuteReader();
        var entries = new List<DownloadHistoryEntry>();
        while (reader.Read())
        {
            var result = reader.GetString(3);
            if (result == "Running" && !OwnerRunning(reader.GetInt32(4), reader.GetInt64(5))) result = "Interrupted";
            entries.Add(new(reader.GetString(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2)), result,
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }
        return entries;
    });

    private static bool OwnerRunning(int id, long start)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == start;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return true; }
    }
}
