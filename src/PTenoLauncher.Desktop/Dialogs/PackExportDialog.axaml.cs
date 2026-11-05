using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Modpacks;

namespace PTenoLauncher.Desktop.Dialogs;

public sealed partial class PackExportDialog : Window
{
    private MainViewModel? owner;
    private GameProfile? profile;
    private PackExportPlan? plan;
    private CancellationTokenSource? operation;
    private bool picking;
    public sealed record ExportRow(string File, string Decision, string? Link)
    {
        public bool HasLink => Link is not null;
    }

    public PackExportDialog() => InitializeComponent();

    internal PackExportDialog(MainViewModel owner, GameProfile profile) : this()
    {
        this.owner = owner;
        this.profile = profile;
        ExportProfile.Text = TextCatalog.Format("CartTarget", profile.Name);
        ExportName.Text = "Minecraft " + profile.MinecraftVersion;
        ExportName.TextChanged += (_, _) => Invalidate();
        ExportGraphics.IsCheckedChanged += (_, _) => Invalidate();
        Closing += (_, e) =>
        {
            if (operation is null && !picking) return;
            e.Cancel = true;
            operation?.Cancel();
        };
        Invalidate();
    }

    private void Invalidate()
    {
        plan = null;
        ExportFiles.ItemsSource = null;
        ExportTarget.Text = "";
        ExportStatus.Text = TextCatalog.Get("ExportReview");
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        var idle = operation is null && !picking;
        PreviewExport.IsEnabled = idle;
        ExportName.IsEnabled = ExportGraphics.IsEnabled = idle;
        ConfirmExport.IsEnabled = idle && plan is not null;
    }

    private async Task PreviewAsync(CancellationToken ct)
    {
        var source = profile!;
        var preview = await owner!.Services.PackExporter.PreviewAsync(source.Id, ExportName.Text ?? "", ExportGraphics.IsChecked == true, ct);
        plan = preview;
        ExportProfile.Text = TextCatalog.Format("CartTarget", preview.ProfileName);
        ExportTarget.Text = $"Minecraft {preview.Minecraft} · {preview.Loader} {preview.LoaderVersion}".TrimEnd();
        ExportFiles.ItemsSource = preview.Items.Select(item => new ExportRow(
            item.Path + (item.Version is null ? "" : " · " + item.Version),
            TextCatalog.Get("ExportAction" + item.Action) + " · " + TextCatalog.Get(item.Reason) + (item.License is null ? "" : " · " + item.License),
            item.Action == PackExportAction.Link ? item.Url?.AbsoluteUri : null)).ToArray();
        ExportStatus.Text = TextCatalog.Get("ExportReady");
    }

    private async void Preview(object? sender, RoutedEventArgs e)
    {
        if (operation is not null || picking || owner is null) return;
        Invalidate();
        await RunAsync(PreviewAsync);
    }

    private async void Save(object? sender, RoutedEventArgs e)
    {
        if (operation is not null || picking || plan is null) return;
        picking = true;
        RefreshButtons();
        string? destination = null;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new()
            {
                Title = TextCatalog.Get("ExportSave"), SuggestedFileName = "Minecraft-" + plan.Minecraft,
                DefaultExtension = "mrpack", FileTypeChoices = [new("Modrinth ZIP (.mrpack)") { Patterns = ["*.mrpack"] }]
            });
            destination = file?.TryGetLocalPath();
        }
        catch (Exception error)
        {
            owner!.Services.Log.Error("export.picker", error);
            ExportStatus.Text = TextCatalog.Get("ExportFailed");
        }
        finally { picking = false; RefreshButtons(); }
        if (destination is not null && await SaveToAsync(destination)) Close(destination);
    }

    internal async Task<bool> SaveToAsync(string destination)
    {
        if (operation is not null || picking || plan is not { } shown || owner is null) return false;
        return await RunAsync(async ct =>
        {
            if (owner.SelectedProfile?.Id != shown.ProfileId) { Invalidate(); throw new MrpackException("ExportChanged"); }
            try { await owner.Services.PackExporter.ExportAsync(shown, destination, true, ct); }
            catch (MrpackException e) when (e.Code == "ExportChanged")
            {
                Invalidate();
                await PreviewAsync(ct);
                throw;
            }
        });
    }

    private async Task<bool> RunAsync(Func<CancellationToken, Task> work)
    {
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        RefreshButtons();
        ExportStatus.Text = TextCatalog.Get("ExportWorking");
        try { await work(cancellation.Token); return true; }
        catch (OperationCanceledException) { ExportStatus.Text = TextCatalog.Get("Message020"); }
        catch (MrpackException e) { ExportStatus.Text = TextCatalog.Get(e.Code); }
        catch (Exception e)
        {
            owner!.Services.Log.Error("export.profile", e);
            ExportStatus.Text = TextCatalog.Get("ExportFailed");
        }
        finally { operation = null; RefreshButtons(); }
        return false;
    }

    private void Cancel(object? sender, RoutedEventArgs e)
    {
        if (operation is not null) operation.Cancel();
        else if (!picking) Close();
    }
}
