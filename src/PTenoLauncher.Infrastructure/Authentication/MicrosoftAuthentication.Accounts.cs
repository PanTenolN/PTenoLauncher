using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Authentication;

public sealed record AuthAccountSummary(string Uuid, string Name, string MicrosoftAccountId, DateTimeOffset VerifiedAt)
{
    public override string ToString() => $"{Name} ({Uuid[..Math.Min(8, Uuid.Length)]})";
}
internal sealed record SavedAccount(string MicrosoftId, MinecraftSession Session);
internal sealed record AccountState(string? ActiveUuid, IReadOnlyList<SavedAccount> Accounts);
public sealed partial class MicrosoftAuthentication
{
    private AccountState LoadAccounts()
    {
        var bytes = vault.Read("minecraft-accounts");
        if (bytes is not null) return JsonSerializer.Deserialize<AccountState>(bytes, AtomicFile.JsonOptions) ?? new(null, []);
        var legacy = vault.Read("minecraft-session");
        var session = legacy is null ? null : JsonSerializer.Deserialize<MinecraftSession>(legacy, AtomicFile.JsonOptions);
        return session is null ? new(null, []) : new(session.Account.Uuid, [new("", session)]);
    }
    private void SaveAccounts(AccountState state)
    {
        vault.Write("minecraft-accounts", JsonSerializer.SerializeToUtf8Bytes(state, AtomicFile.JsonOptions));
        vault.Delete("minecraft-session");
    }
    public IReadOnlyList<AuthAccountSummary> GetAccounts() => LoadAccounts().Accounts.Select(a =>
        new AuthAccountSummary(a.Session.Account.Uuid, a.Session.Account.Name, a.MicrosoftId, a.Session.Account.VerifiedAt)).ToArray();
    private void SaveSession(string microsoftId, MinecraftSession session)
    {
        var existing = LoadAccounts();
        SaveAccounts(new(session.Account.Uuid, existing.Accounts.Where(a => a.Session.Account.Uuid != session.Account.Uuid).Append(new(microsoftId, session)).ToArray()));
    }
    public async Task SelectAccountAsync(string uuid, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var state = LoadAccounts();
            if (!state.Accounts.Any(a => a.Session.Account.Uuid == uuid)) throw new InvalidOperationException("Акаунт не підтверджений у локальному сховищі.");
            SaveAccounts(state with { ActiveUuid = uuid });
        }
        finally { gate.Release(); }
    }
    public async Task SignOutAllAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (app is not null) foreach (var account in await app.GetAccountsAsync()) await app.RemoveAsync(account);
            SaveAccounts(new(null, [])); vault.Delete("msal"); vault.Delete("minecraft-session");
        }
        finally { gate.Release(); }
    }
}
