using PTenoLauncher.Core;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Modpacks;

namespace PTenoLauncher.Desktop.ViewModels;

public sealed record EditorLoaderOption(string Version, string? Badge = null)
{
    public override string ToString() => Badge is null ? Version : $"{Version} · {TextCatalog.Get(Badge)}";
}

public sealed partial class MainViewModel
{
    private IReadOnlyList<string> editorLoaderVersions = [];
    private IReadOnlyList<EditorLoaderOption> editorLoaderOptions = [];
    public IReadOnlyList<EditorLoaderOption> EditorLoaderOptions => editorLoaderOptions;
    public EditorLoaderOption? SelectedEditorLoaderOption
    {
        get => editorLoaderOptions.FirstOrDefault(o => o.Version == EditorLoaderVersion);
        set => EditorLoaderVersion = value?.Version;
    }
    private string? editorLoaderVersion;
    private int editorLoaderRevision;
    private string editorCatalogState = "LoaderVersionPrompt";
    public string EditorCatalogState => TextCatalog.Get(editorCatalogState);
    public bool IsCreatingProfile => SelectedProfile is null;
    public string ProfileDraftSummary => $"{ProfileName.Trim()} · {VersionId} · {Loader}{(EditorLoaderVersion is null ? "" : " " + EditorLoaderVersion)}";
    public bool HasEditorLoader => Loader is not ModLoader.Vanilla;
    public bool UsesSavedLoaderVersion => SelectedProfile?.PreferredLoaderVersion is not null;
    public IReadOnlyList<string> EditorLoaderVersions
    {
        get => editorLoaderVersions;
        private set => Set(ref editorLoaderVersions, value);
    }
    public string? EditorLoaderVersion
    {
        get => editorLoaderVersion;
        set { if (Set(ref editorLoaderVersion, value)) { RefreshProfileFields(); Changed(nameof(SelectedEditorLoaderOption)); } }
    }

    public bool MissingProfileName => string.IsNullOrWhiteSpace(ProfileName);
    public bool MissingProfileMinecraft => !HasVersionChoice;
    public bool MissingProfileMemory => string.IsNullOrWhiteSpace(Memory);
    public bool MissingProfileLoaderVersion => SelectedProfile is null && HasEditorLoader && string.IsNullOrWhiteSpace(EditorLoaderVersion);
    public bool ProfileFieldsComplete => !(MissingProfileName || MissingProfileMinecraft || MissingProfileMemory || MissingProfileLoaderVersion);
    public string MissingProfileFieldsText => TextCatalog.Get("ProfileFieldsMissing") + " " +
        string.Join(", ", new (bool Missing, string Key)[] {
            (MissingProfileName, "Name"), (MissingProfileMinecraft, "Version"),
            (MissingProfileMemory, "Memory"), (MissingProfileLoaderVersion, "LoaderVersion")
        }.Where(item => item.Missing).Select(item => TextCatalog.Get(item.Key)));

    private void RefreshProfileFields()
    {
        Changed(nameof(MissingProfileName));
        Changed(nameof(MissingProfileMinecraft));
        Changed(nameof(MissingProfileMemory));
        Changed(nameof(MissingProfileLoaderVersion));
        Changed(nameof(ProfileFieldsComplete));
        Changed(nameof(MissingProfileFieldsText));
        Changed(nameof(ProfileDraftSummary)); Changed(nameof(IsCreatingProfile));
    }

    public void OpenProfileFolder()
    {
        if (SelectedProfile is not { } profile) return;
        try
        {
            var folder = services.Paths.Game(profile.Id);
            PTenoLauncher.Infrastructure.Storage.SafePath.RejectLinks(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    private void ResetEditorLoaderVersions(string? preferred = null)
    {
        editorLoaderRevision++;
        editorCatalogState = "LoaderVersionPrompt"; Changed(nameof(EditorCatalogState));
        EditorLoaderVersions = preferred is null ? [] : [preferred];
        SetEditorLoaderOptions(EditorLoaderVersions, null, null);
        EditorLoaderVersion = preferred;
    }

    private void SetEditorLoaderOptions(IReadOnlyList<string> versions, string? recommended, string? stable)
    {
        editorLoaderOptions = versions.Select(v => new EditorLoaderOption(v,
            v == recommended ? "LoaderRecommended" : v == stable ? "LoaderLatestStable" : null)).ToArray();
        Changed(nameof(EditorLoaderOptions));
        Changed(nameof(SelectedEditorLoaderOption));
    }

    public void ResetEditorLoaderChoice() => ResetEditorLoaderVersions(SelectedProfile?.PreferredLoaderVersion);

    public Task LoadEditorLoaderVersionsAsync(CancellationToken lifetime) => RunAsync(async ct =>
    {
        if (!HasEditorLoader || !HasVersionChoice) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime);
        var token = linked.Token;
        var loader = Loader;
        var minecraft = VersionId;
        var revision = editorLoaderRevision;
        var selected = SelectedProfile;
        var preferred = EditorLoaderVersion;
        var imported = selected is null ? null : await MrpackRequirements.ReadAsync(services.Paths, selected, token);
        editorCatalogState = "EditorCatalogLoading"; Changed(nameof(EditorCatalogState));
        try
        {
        string? stable = null;
        string? recommended = null;
        string[] versions;
        switch (loader)
        {
            case ModLoader.Fabric:
                var fabric = await services.Fabric.GetVersionsAsync(minecraft, token);
                stable = fabric.FirstOrDefault(v => v.Stable)?.Version;
                versions = fabric.OrderByDescending(v => v.Stable).Select(v => v.Version).ToArray();
                break;
            case ModLoader.Quilt:
                var quilt = await services.Quilt.GetVersionsAsync(minecraft, token);
                stable = quilt.FirstOrDefault(v => !v.IsPrerelease)?.Version;
                versions = quilt.Select(v => v.Version).ToArray();
                break;
            case ModLoader.Forge:
                versions = (await services.ForgeVersions.GetVersionsAsync(loader, minecraft, refresh: true, ct: token)).Select(v => v.Version).ToArray();
                if (versions.Length > 0) recommended = await services.ForgeVersions.GetRecommendedForgeVersionAsync(minecraft, token);
                break;
            case ModLoader.NeoForge:
                versions = (await services.ForgeVersions.GetVerifiedNeoForgeVersionsAsync(minecraft, refresh: true, ct: token)).Select(v => v.Version).ToArray();
                break;
            default: versions = []; break;
        }
        token.ThrowIfCancellationRequested();
        if (editorLoaderRevision != revision || Loader != loader || VersionId != minecraft || SelectedProfile?.Id != selected?.Id) return;
        if (imported is not null)
        {
            preferred = imported.LoaderVersion;
            versions = versions.Where(v => v == preferred).ToArray();
        }
        if (preferred is not null && !versions.Contains(preferred, StringComparer.Ordinal)) versions = [preferred, .. versions];
        EditorLoaderVersions = versions.Distinct(StringComparer.Ordinal).ToArray();
        SetEditorLoaderOptions(EditorLoaderVersions, recommended, stable);
        EditorLoaderVersion = preferred;
        editorCatalogState = versions.Length == 0 ? "VersionNoResults" :
            services.Network.Offline || loader == ModLoader.Fabric && services.Fabric.UsedCachedMetadata ||
            loader == ModLoader.Quilt && services.Quilt.UsedCachedMetadata ||
            loader is ModLoader.Forge or ModLoader.NeoForge && services.ForgeVersions.UsedCachedMetadata ? "EditorCatalogCached" : "EditorCatalogReady";
        Status = versions.Length == 0 ? TextCatalog.Get("VersionNoResults") : loader switch
        {
            ModLoader.Fabric => TextCatalog.Format("FabricCount", versions.Length),
            ModLoader.Quilt => TextCatalog.Get("Message065") + versions.Length,
            _ => TextCatalog.Format("ForgeCount", versions.Length)
        };
        }
        finally
        {
            if (editorCatalogState == "EditorCatalogLoading") editorCatalogState = "LoaderVersionPrompt";
            Changed(nameof(EditorCatalogState));
        }
    });
}
