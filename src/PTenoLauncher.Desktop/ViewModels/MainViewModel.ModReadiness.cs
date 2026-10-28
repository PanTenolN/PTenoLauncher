using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Mods;
namespace PTenoLauncher.Desktop.ViewModels;
public sealed partial class MainViewModel
{
    public Task<ModBaselinePreview> GetModBaselineAsync() => services.Mods.PreviewBaselineAsync(RequireProfile());
    public Task ConfirmModBaselineAsync(ModBaselinePreview preview) => RunAsync(async ct =>
    {
        await services.Mods.ConfirmBaselineAsync(RequireProfile(), preview, true, ct);
        Readiness = (await services.Readiness.CheckAsync(RequireProfile(), ct)).Summary;
        Status = TextCatalog.Get("Message042");
    });
}
