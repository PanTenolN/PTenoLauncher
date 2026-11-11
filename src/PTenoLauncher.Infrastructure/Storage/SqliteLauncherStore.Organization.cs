namespace PTenoLauncher.Infrastructure.Storage;

public sealed record ProfileOrganization(string Group, string Notes);

public sealed partial class SqliteLauncherStore
{
    public Task<IReadOnlyDictionary<Guid, ProfileOrganization>> ReadOrganizationAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyDictionary<Guid, ProfileOrganization>>(() =>
        {
            using var db = Open();
            using var command = db.CreateCommand();
            command.CommandText = "SELECT profile_id, group_name, notes FROM profile_organization";
            using var rows = command.ExecuteReader();
            var result = new Dictionary<Guid, ProfileOrganization>();
            while (rows.Read())
            {
                ct.ThrowIfCancellationRequested();
                result.Add(Guid.Parse(rows.GetString(0)), new(rows.GetString(1), rows.GetString(2)));
            }
            return result;
        }, ct);

    public Task SaveOrganizationAsync(Guid profileId, ProfileOrganization value, CancellationToken ct = default) => Task.Run(() =>
    {
        var group = value.Group.Trim();
        if (group.Length > 60 || group.Any(char.IsControl) || value.Notes.Length > 8000 || value.Notes.Contains('\0'))
            throw new ArgumentException("ProfileOrganizationInvalid");
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            INSERT INTO profile_organization (profile_id, group_name, notes) VALUES ($id, $group, $notes)
            ON CONFLICT(profile_id) DO UPDATE SET group_name=$group, notes=$notes
            """;
        command.Parameters.AddWithValue("$id", profileId.ToString("N"));
        command.Parameters.AddWithValue("$group", group);
        command.Parameters.AddWithValue("$notes", value.Notes);
        ct.ThrowIfCancellationRequested();
        command.ExecuteNonQuery();
    }, ct);
}
