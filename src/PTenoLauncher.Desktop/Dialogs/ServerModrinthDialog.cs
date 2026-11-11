using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Mods;

namespace PTenoLauncher.Desktop.Dialogs;

internal sealed class ServerModrinthDialog : Window
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBox query = new() { MaxLength = 100, MinWidth = 220, PlaceholderText = TextCatalog.Get("Search") };
    private readonly ListBox hits = new();
    private readonly ComboBox versions = new() { MinWidth = 180, PlaceholderText = TextCatalog.Get("Version") };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button select = new() { Content = TextCatalog.Get("FirstRunContinue"), IsEnabled = false, Classes = { "primary" } };
    private bool busy;

    internal ServerModrinthDialog(ModrinthClient catalog, string minecraft, ModLoader loader)
    {
        Title = TextCatalog.Get("ServerAddModrinthMod");
        Width = 630;
        Height = 495;
        MinWidth = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var search = new Button { Content = TextCatalog.Get("Search") };
        var close = new Button { Content = TextCatalog.Get("Cancel"), IsCancel = true };
        var panel = new Grid { Margin = new Thickness(16), RowDefinitions = new("Auto,*,Auto,Auto"), RowSpacing = 8 };
        panel.Children.Add(new WrapPanel { ItemSpacing = 8, Children = { query, search } });
        Grid.SetRow(hits, 1); panel.Children.Add(hits);
        var footer = new WrapPanel { ItemSpacing = 8, Children = { versions, select, close } };
        Grid.SetRow(footer, 2); panel.Children.Add(footer);
        Grid.SetRow(status, 3); panel.Children.Add(status);
        Content = panel;
        Closed += (_, _) => lifetime.Cancel();
        close.Click += (_, _) => Close();
        select.Click += (_, _) => { if (versions.SelectedItem is CatalogModVersion version) Close(version); };
        versions.SelectionChanged += (_, _) => select.IsEnabled = !busy && versions.SelectedItem is not null;
        search.Click += async (_, _) => await SearchAsync();
        query.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await SearchAsync();
        };
        hits.SelectionChanged += async (_, _) =>
        {
            if (hits.SelectedItem is not CatalogSearchHit hit) return;
            await RunAsync(async ct =>
            {
                versions.ItemsSource = null;
                var project = await catalog.GetProjectAsync(hit.Id, ct);
                if (!project.SupportsServer)
                {
                    status.Text = project.Title + " · " + TextCatalog.Get("ServerModClient");
                    return;
                }
                var choices = await catalog.GetVersionsAsync(project.Id, minecraft, loader, ct);
                versions.ItemsSource = choices;
                if (choices.Count > 0) versions.SelectedIndex = 0;
                else status.Text = TextCatalog.Get("ServerBuildUnavailable");
            });
        };

        Task SearchAsync() => RunAsync(async ct =>
        {
            hits.ItemsSource = null;
            versions.ItemsSource = null;
            hits.ItemsSource = await catalog.SearchAsync(query.Text ?? "", minecraft, loader, ct);
        });

        async Task RunAsync(Func<CancellationToken, Task> action)
        {
            if (busy) return;
            busy = true;
            query.IsEnabled = search.IsEnabled = hits.IsEnabled = select.IsEnabled = false;
            status.Text = "";
            try { await action(lifetime.Token); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error) { status.Text = TextCatalog.Get(error.Message); }
            finally
            {
                busy = false;
                query.IsEnabled = search.IsEnabled = hits.IsEnabled = true;
                select.IsEnabled = versions.SelectedItem is not null;
            }
        }
    }
}
