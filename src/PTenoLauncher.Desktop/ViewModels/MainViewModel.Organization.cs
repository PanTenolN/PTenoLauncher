using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Desktop.ViewModels;

public sealed record LibraryGroupOption(string? Name)
{
    public bool IsAll => Name is null;
}

public sealed partial class MainViewModel
{
    private static readonly LibraryGroupOption AllLibraryGroups = new(null);
    private static readonly ProfileOrganization EmptyOrganization = new("", "");
    private IReadOnlyDictionary<Guid, ProfileOrganization> organization = new Dictionary<Guid, ProfileOrganization>();
    private LibraryGroupOption libraryGroup = AllLibraryGroups;
    public IReadOnlyList<LibraryGroupOption> LibraryGroups { get; private set; } = [AllLibraryGroups];
    public bool HasLibraryGroups => LibraryGroups.Count > 1;

    public LibraryGroupOption LibraryGroup
    {
        get => libraryGroup;
        set { if (Set(ref libraryGroup, value ?? AllLibraryGroups)) Changed(nameof(LibraryProfiles)); }
    }

    internal ProfileOrganization OrganizationFor(Guid profile) => organization.GetValueOrDefault(profile, EmptyOrganization);

    private async Task ReloadOrganizationAsync(CancellationToken ct)
    {
        organization = await services.Store.ReadOrganizationAsync(ct);
        var previous = LibraryGroup.Name;
        LibraryGroups = [AllLibraryGroups, ..organization.Values.Select(item => item.Group).Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).Select(name => new LibraryGroupOption(name))];
        Changed(nameof(LibraryGroups));
        libraryGroup = LibraryGroups.FirstOrDefault(option => string.Equals(option.Name, previous, StringComparison.OrdinalIgnoreCase)) ?? AllLibraryGroups;
        Changed(nameof(LibraryGroup));
        Changed(nameof(HasLibraryGroups));
        Changed(nameof(LibraryProfiles));
    }

    internal Task SaveOrganizationAsync(Guid profile, ProfileOrganization value) => RunAsync(async ct =>
    {
        try { await services.Store.SaveOrganizationAsync(profile, value, ct); }
        catch (ArgumentException error) { throw new InvalidOperationException(TextCatalog.Get(error.Message), error); }
        await ReloadOrganizationAsync(ct);
        Status = TextCatalog.Get("ProfileOrganizationSaved");
    });
}
