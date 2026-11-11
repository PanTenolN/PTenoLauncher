using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Desktop.Dialogs;

internal sealed class AternosChecklistDialog : Window
{
    private sealed record Source(GameProfile? Profile, LocalVanillaServer? Server)
    {
        public override string ToString() => Profile is { } profile
            ? $"{profile.Name} · {profile.MinecraftVersion} · {profile.Loader}"
            : Server?.ToString() ?? "";
    }

    private sealed class Row(AternosItem item)
    {
        public AternosItem Item { get; set; } = item;
    }

    private readonly AternosChecklistService service;
    private readonly ComboBox source = new() { MinWidth = 270 };
    private readonly ComboBox category = new() { Name = "AternosCategoryPicker", MinWidth = 160 };
    private readonly TextBox search = new() { Name = "AternosSearchField", MinWidth = 160, MaxLength = 180, PlaceholderText = TextCatalog.Get("AternosSearch") };
    private readonly ListBox list = new() { Name = "AternosRows" };
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { Name = "AternosChecklistStatus", TextWrapping = TextWrapping.Wrap };
    private readonly Button match = new() { Content = TextCatalog.Get("AternosFindProject") };
    private readonly StackPanel toolbar = new() { Spacing = 8 };
    private CancellationTokenSource? statusCheck;
    private readonly Dictionary<Source, (AternosChecklist Checklist, Row[] Rows)> drafts = [];
    private AternosChecklist? checklist;
    private Row[] rows = [];
    private bool busy;

    public AternosChecklistDialog(LauncherServices services, IReadOnlyList<GameProfile> profiles,
        IReadOnlyList<LocalVanillaServer> servers, Guid? selectedProfile)
    {
        service = new(services.Paths, services.Mods, services.Content, services.Modrinth, services.Servers);
        Title = TextCatalog.Get("AternosTitle");
        Width = 900;
        Height = 670;
        MinWidth = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var choices = profiles.Where(profile => !profile.Archived).OrderByDescending(profile => profile.Id == selectedProfile)
            .Select(profile => new Source(profile, null)).Concat(servers.Where(server => server.HasPlugins).Select(server => new Source(null, server))).ToArray();
        source.ItemsSource = choices;
        source.PlaceholderText = TextCatalog.Get("AternosSource");
        source.SelectedIndex = choices.Length == 0 ? -1 : 0;
        category.ItemsSource = new[] { TextCatalog.Get("AternosAll") }.Concat(Enum.GetValues<AternosItemKind>().Select(KindLabel)).ToArray();
        category.SelectedIndex = 0;
        list.ItemTemplate = new FuncDataTemplate<Row>((row, _) => row is null ? new TextBlock() : PresentRow(row));

        toolbar.Children.Add(new TextBlock { Text = TextCatalog.Get("AternosHint"), TextWrapping = TextWrapping.Wrap });
        toolbar.Children.Add(new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { source, search, category } });
        toolbar.Children.Add(summary);
        var links = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        links.Children.Add(Link("AternosOpen", AternosChecklistService.PanelUrl));
        links.Children.Add(Link("AternosCatalog", AternosChecklistService.CatalogUrl));
        links.Children.Add(Link("AternosResourceGuide", AternosChecklistService.ResourcePackGuide));
        toolbar.Children.Add(links);
        toolbar.Children.Add(CreateStatusPanel(services));

        var copy = new Button { Content = TextCatalog.Get("AternosCopy") };
        copy.Click += async (_, _) =>
        {
            try
            {
                if (Selection() is { } selected && Clipboard is { } clipboard)
                {
                    await clipboard.SetTextAsync(Encoding.UTF8.GetString(AternosChecklistService.Export(selected, AternosFormat.Txt)));
                    status.Text = TextCatalog.Get("AternosCopied");
                }
            }
            catch (Exception error) { status.Text = TextCatalog.Get(error.Message); }
        };
        var footer = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { match, copy } };
        foreach (var format in Enum.GetValues<AternosFormat>())
        {
            var button = new Button { Content = TextCatalog.Format("AternosExport", format.ToString().ToUpperInvariant()) };
            button.Click += async (_, _) => await ExportAsync(format);
            footer.Children.Add(button);
        }

        var panel = new Grid { Margin = new Thickness(18), RowDefinitions = new("Auto,*,Auto,Auto"), RowSpacing = 10 };
        panel.Children.Add(toolbar);
        Grid.SetRow(list, 1); panel.Children.Add(list);
        Grid.SetRow(footer, 2); panel.Children.Add(footer);
        Grid.SetRow(status, 3); panel.Children.Add(status);
        Content = panel;
        Opened += async (_, _) => await LoadSourceAsync();
        Closing += (_, e) => e.Cancel = busy;
        Closed += (_, _) => statusCheck?.Cancel();
        source.SelectionChanged += async (_, _) => await LoadSourceAsync();
        search.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) ApplyFilter(); };
        category.SelectionChanged += (_, _) => ApplyFilter();
        list.SelectionChanged += (_, _) => UpdateMatchButton();
        match.Click += async (_, _) => await MatchProjectAsync();
        UpdateMatchButton();
    }

    private Control CreateStatusPanel(LauncherServices services)
    {
        var probe = new MinecraftServerStatus(services.Network);
        var name = new TextBox { Name = "AternosSavedName", MinWidth = 160, MaxLength = 80, PlaceholderText = TextCatalog.Get("AternosSavedName") };
        var address = new TextBox { Name = "AternosStatusAddress", MinWidth = 270, MaxLength = 300, PlaceholderText = TextCatalog.Get("AternosStatusAddress") };
        var check = new Button { Name = "AternosStatusCheck", Content = TextCatalog.Get("AternosStatusCheck") };
        var save = new Button { Name = "SaveAternosServer", Content = TextCatalog.Get("AternosSavedAddressAction") };
        var copy = new Button { Name = "AternosCopyServerName", Content = TextCatalog.Get("AternosCopyName"), IsEnabled = false };
        var result = new TextBlock { Name = "AternosStatusResult", TextWrapping = TextWrapping.Wrap };
        string? savedName = null;
        Opened += async (_, _) =>
        {
            try
            {
                if (await AternosSavedServer.LoadAsync(services.Store) is not { } saved ||
                    !string.IsNullOrEmpty(name.Text) || !string.IsNullOrEmpty(address.Text)) return;
                name.Text = saved.Name;
                address.Text = saved.Address;
                savedName = saved.Name;
                copy.IsEnabled = true;
            }
            catch (Exception error)
            {
                services.Log.Error("aternos.saved-server", error);
                result.Text = TextCatalog.Get(error.Message);
            }
        };
        save.Click += async (_, _) =>
        {
            if (!save.IsEnabled) return;
            save.IsEnabled = false;
            try
            {
                var saved = new AternosSavedServer(name.Text?.Trim() ?? "", address.Text?.Trim() ?? "");
                await saved.SaveAsync(services.Store);
                savedName = saved.Name;
                copy.IsEnabled = true;
                result.Text = TextCatalog.Get("AternosSavedAddressDone");
            }
            catch (Exception error) { result.Text = TextCatalog.Get(error.Message); }
            finally { save.IsEnabled = true; }
        };
        copy.Click += async (_, _) =>
        {
            if (savedName is null || Clipboard is not { } clipboard) return;
            try
            {
                await clipboard.SetTextAsync(savedName);
                result.Text = TextCatalog.Get("AternosNameCopied");
            }
            catch (Exception error) { result.Text = TextCatalog.Get(error.Message); }
        };
        check.Click += async (_, _) =>
        {
            if (statusCheck is not null) return;
            if (services.Network.Offline)
            {
                result.Text = TextCatalog.Get("AternosStatusOffline");
                return;
            }
            using var cancellation = new CancellationTokenSource();
            statusCheck = cancellation;
            address.IsEnabled = check.IsEnabled = false;
            result.Text = TextCatalog.Get("AternosStatusChecking");
            try
            {
                var reply = await probe.QueryAsync(address.Text ?? "", cancellation.Token);
                result.Text = reply switch
                {
                    { Online: int online, Maximum: int maximum } => TextCatalog.Format("AternosStatusPlayers", reply.Version, online, maximum),
                    not null => TextCatalog.Format("AternosStatusResponded", reply.Version),
                    _ => TextCatalog.Get("AternosStatusNoReply")
                };
            }
            catch (ArgumentException) { result.Text = TextCatalog.Get("AternosStatusInvalidAddress"); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (InvalidOperationException) when (services.Network.Offline) { result.Text = TextCatalog.Get("AternosStatusOffline"); }
            finally
            {
                statusCheck = null;
                address.IsEnabled = check.IsEnabled = true;
            }
        };
        return new StackPanel
        {
            Spacing = 4,
            Children = { new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { name, address, save, copy, check } }, result }
        };
    }

    private Control PresentRow(Row row)
    {
        var item = row.Item;
        var copyName = new Button
        {
            Name = "AternosCopyProjectName",
            Content = new TextBlock { Text = item.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        var copyHint = TextCatalog.Get("AternosCopyName");
        AutomationProperties.SetName(copyName, item.Name);
        AutomationProperties.SetHelpText(copyName, copyHint);
        ToolTip.SetTip(copyName, copyHint);
        copyName.Click += async (_, _) =>
        {
            try
            {
                if (Clipboard is not { } clipboard) return;
                await clipboard.SetTextAsync(item.Name);
                status.Text = TextCatalog.Get("AternosNameCopied");
            }
            catch (Exception error) { status.Text = TextCatalog.Get(error.Message); }
        };
        var choose = new CheckBox { Content = TextCatalog.Get("AternosInclude"), IsChecked = item.Included };
        var installed = new CheckBox { Content = TextCatalog.Get("AternosInstalled"), IsChecked = item.Installed };
        choose.IsCheckedChanged += (_, _) => row.Item = row.Item with { Included = choose.IsChecked == true };
        installed.IsCheckedChanged += (_, _) => row.Item = row.Item with { Installed = installed.IsChecked == true };
        var actions = new WrapPanel { ItemSpacing = 8, Children = { choose, installed } };
        if (item.Project is { } project) actions.Children.Add(Link("AternosOpenProject", project.AbsoluteUri));
        var details = string.Join(" · ", new[] { KindLabel(item.Kind), item.File, item.Version ?? TextCatalog.Get("AternosUnknownVersion") }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var stack = new StackPanel
        {
            Margin = new Thickness(4),
            Spacing = 3,
            Children =
            {
                copyName,
                new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap },
                actions
            }
        };
        if (item.Dependencies.Count != 0)
            stack.Children.Insert(2, new TextBlock { Text = TextCatalog.Format("AternosRequires", string.Join(", ", item.Dependencies)), TextWrapping = TextWrapping.Wrap });
        if (item.NeedsReview || item.Project is null)
            stack.Children.Insert(stack.Children.Count - 1, new TextBlock { Text = TextCatalog.Get("AternosManualReview") });
        return stack;
    }

    private Button Link(string key, string url)
    {
        var button = new Button { Content = TextCatalog.Get(key) };
        button.Click += (_, _) =>
        {
            try { using var browser = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception error) { status.Text = TextCatalog.Get(error.Message); }
        };
        return button;
    }

    private async Task LoadSourceAsync()
    {
        if (busy) return;
        status.Text = "";
        if (source.SelectedItem is not Source choice)
        {
            status.Text = TextCatalog.Get("AternosNoSources");
            return;
        }
        BeginWork();
        try
        {
            if (!drafts.TryGetValue(choice, out var draft))
            {
                var scanned = choice.Profile is { } profile
                    ? await service.FromProfileAsync(profile)
                    : await service.FromPluginServerAsync(choice.Server!);
                draft = (scanned, scanned.Items.Select(item => new Row(item)).ToArray());
                drafts.Add(choice, draft);
            }
            (checklist, rows) = draft;
            summary.Text = string.Join(" · ", new[] { checklist.Source, checklist.Minecraft, checklist.Software, checklist.Build }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            ApplyFilter();
        }
        catch (Exception error)
        {
            checklist = null;
            rows = [];
            summary.Text = "";
            ApplyFilter();
            status.Text = TextCatalog.Get(error.Message);
        }
        finally { EndWork(); }
    }

    private void ApplyFilter(Row? selection = null)
    {
        var query = search.Text?.Trim() ?? "";
        var selectedCategory = category.SelectedIndex - 1;
        list.ItemsSource = rows.Where(row =>
            (selectedCategory < 0 || (int)row.Item.Kind == selectedCategory) &&
            (query.Length == 0 || new[] { row.Item.Name, row.Item.File, row.Item.Version }.Concat(row.Item.Dependencies)
                .Any(value => value?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true))).ToArray();
        if (selection is not null) list.SelectedItem = selection;
        UpdateMatchButton();
    }

    private void BeginWork()
    {
        busy = true;
        toolbar.IsEnabled = list.IsEnabled = false;
        UpdateMatchButton();
    }

    private void EndWork()
    {
        busy = false;
        toolbar.IsEnabled = list.IsEnabled = true;
        UpdateMatchButton();
    }

    private void UpdateMatchButton() => match.IsEnabled = !busy && checklist is not null &&
        list.SelectedItem is Row { Item: { LocalFile: not null, Project: null } };

    private async Task MatchProjectAsync()
    {
        if (busy || checklist is null || list.SelectedItem is not Row row) return;
        BeginWork();
        status.Text = TextCatalog.Get("AternosLookingUp");
        try
        {
            row.Item = await service.MatchProjectAsync(row.Item, checklist.Minecraft, checklist.Software);
            status.Text = TextCatalog.Get(row.Item.Project is null ? "AternosManualReview" : "AternosProjectMatched");
            ApplyFilter(row);
        }
        catch (Exception)
        {
            status.Text = TextCatalog.Get("AternosProjectUnavailable");
        }
        finally { EndWork(); }
    }

    private AternosChecklist? Selection()
    {
        if (!busy && checklist is not null && rows.Any(row => row.Item.Included))
            return checklist with { Items = rows.Select(row => row.Item).ToArray() };
        status.Text = TextCatalog.Get("AternosSelectItems");
        return null;
    }

    private async Task ExportAsync(AternosFormat format)
    {
        if (Selection() is not { } selected) return;
        try
        {
            var extension = format.ToString().ToLowerInvariant();
            var file = await StorageProvider.SaveFilePickerAsync(new()
            {
                Title = TextCatalog.Format("AternosExport", format.ToString().ToUpperInvariant()),
                SuggestedFileName = "Aternos-checklist",
                DefaultExtension = extension,
                FileTypeChoices = [new(format.ToString().ToUpperInvariant()) { Patterns = ["*." + extension] }]
            });
            if (file?.TryGetLocalPath() is not { } path) return;
            await AtomicFile.WriteAsync(path, AternosChecklistService.Export(selected, format));
            status.Text = TextCatalog.Get("AternosExported");
        }
        catch (Exception error) { status.Text = TextCatalog.Get(error.Message); }
    }

    private static string KindLabel(AternosItemKind kind) => TextCatalog.Get("AternosKind" + kind);
}
