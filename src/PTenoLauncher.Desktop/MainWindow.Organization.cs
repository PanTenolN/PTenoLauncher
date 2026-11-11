using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Desktop;

public sealed partial class MainWindow
{
    private async void EditOrganization(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsIdle: true, WorkspaceProfileId: { } id, SelectedProfile: { } profile } vm || id != profile.Id) return;
        var title = TextCatalog.Get("ProfileOrganization");
        var saved = vm.OrganizationFor(id);
        var group = new TextBox { Name = "ProfileGroupInput", Text = saved.Group, MaxLength = 60 };
        var notes = new TextBox { Name = "ProfileNotesInput", Text = saved.Notes, MaxLength = 8000, AcceptsReturn = true,
            MinHeight = 140, MaxHeight = 240, TextWrapping = TextWrapping.Wrap };
        var save = new Button { Name = "SaveProfileOrganization", Content = TextCatalog.Get("Save"), IsDefault = true };
        var cancel = new Button { Content = TextCatalog.Get("Cancel"), IsCancel = true };
        var window = new Window { Title = title, Width = 520, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        save.Click += (_, _) => window.Close(new ProfileOrganization(group.Text ?? "", notes.Text ?? ""));
        cancel.Click += (_, _) => window.Close();
        window.Content = new StackPanel { Margin = new Thickness(18), Spacing = 10, Children =
        {
            new TextBlock { Text = profile.Name, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = TextCatalog.Get("ProfileGroup") }, group,
            new TextBlock { Text = TextCatalog.Get("ProfileNotes") }, notes,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, cancel } }
        } };
        if (await window.ShowDialog<ProfileOrganization?>(this) is { } result && vm.WorkspaceProfileId == id)
            await vm.SaveOrganizationAsync(id, result);
    }

    private async void ShowScreenshots(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsIdle: true, WorkspaceProfileId: { } id, SelectedProfile: { } profile } vm || id != profile.Id) return;
        IReadOnlyList<string>? names = null;
        await vm.RunAsync(async ct => names = await vm.Services.Screenshots.ListAsync(id, ct));
        if (names is null || vm.WorkspaceProfileId != id) return;

        using var session = new CancellationTokenSource();
        var token = session.Token;
        var list = new ListBox { Name = "ProfileScreenshotList", ItemsSource = names };
        var image = new Image { Stretch = Stretch.Uniform };
        var message = new TextBlock { Text = TextCatalog.Get(names.Count == 0 ? "ScreenshotsEmpty" : "ScreenshotSelect"), TextWrapping = TextWrapping.Wrap };
        var viewer = new Grid { ColumnDefinitions = new("220,*"), ColumnSpacing = 12, Children = { list, new Panel { Children = { image, message } } } };
        Grid.SetColumn(viewer.Children[1], 1);
        var window = new Window { Title = TextCatalog.Get("ProfileScreenshots") + " · " + profile.Name, Width = 840, Height = 550,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new Border { Padding = new Thickness(16), Child = viewer } };
        int revision = 0;
        void ReplaceImage(Bitmap? bitmap)
        {
            var previous = image.Source as Bitmap;
            image.Source = bitmap;
            previous?.Dispose();
        }
        list.SelectionChanged += async (_, _) =>
        {
            var selection = ++revision;
            ReplaceImage(null);
            if (list.SelectedItem is not string name) return;
            message.Text = "";
            try
            {
                var bytes = await vm.Services.Screenshots.ReadAsync(id, name, token);
                using var stream = new MemoryStream(bytes, writable: false);
                var bitmap = await Task.Run(() => Bitmap.DecodeToWidth(stream, Math.Min(1280, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)))), token);
                if (session.IsCancellationRequested || selection != revision) bitmap.Dispose();
                else ReplaceImage(bitmap);
            }
            catch (OperationCanceledException) when (session.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            { if (!session.IsCancellationRequested && selection == revision) message.Text = TextCatalog.Get("ScreenshotInvalid"); }
        };
        window.Closed += (_, _) => { session.Cancel(); ReplaceImage(null); };
        await window.ShowDialog(this);
    }

    private async void ShowStorageUsage(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsIdle: true } vm) return;
        StorageUsageSnapshot? snapshot = null;
        await vm.RunAsync(async ct => snapshot = await vm.Services.StorageUsage.ReadAsync(ct));
        if (snapshot is null) return;
        static string FormatBytes(long bytes) => bytes.ToString("N0") + " " + TextCatalog.Get("Bytes");
        var usage = string.Join(Environment.NewLine, new[]
        {
            TextCatalog.Format("StorageTotal", FormatBytes(snapshot.TotalBytes)),
            TextCatalog.Format("StorageBackups", FormatBytes(snapshot.BackupBytes)),
            TextCatalog.Format("StorageCache", FormatBytes(snapshot.CacheBytes)), "",
        }.Concat(snapshot.Profiles.Select(item => TextCatalog.Format("StorageProfile", item.Profile.Name,
            FormatBytes(item.ProfileBytes), FormatBytes(item.WorldBytes)))).Concat(snapshot.UnreadablePaths.Count == 0 ? [] :
            new[] { "", TextCatalog.Get("StorageIncomplete") }.Concat(snapshot.UnreadablePaths)));
        var summary = new TextBox { Text = usage, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var filter = new TextBox { PlaceholderText = TextCatalog.Get("Search") };
        var files = new ListBox { Name = "StorageCacheReferences" };
        var rows = snapshot.CacheReferences.Select(file => file.RelativePath + " · " + FormatBytes(file.Bytes) + " · " +
            string.Join(", ", file.Profiles.Select(profile => profile.Name))).ToArray();
        files.ItemsSource = rows;
        filter.TextChanged += (_, _) => files.ItemsSource = rows.Where(row => row.Contains(filter.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
        var cache = new Grid { RowDefinitions = new("Auto,Auto,*"), RowSpacing = 8, Margin = new Thickness(8), Children =
        { new TextBlock { Text = TextCatalog.Get("StorageReferenceHint"), TextWrapping = TextWrapping.Wrap }, filter, files } };
        Grid.SetRow(filter, 1);
        Grid.SetRow(files, 2);
        var tabs = new TabControl { Items = { new TabItem { Header = TextCatalog.Get("StorageUsage"), Content = summary },
            new TabItem { Header = TextCatalog.Get("StorageCacheFiles"), Content = cache } } };
        await new Window { Title = TextCatalog.Get("StorageUsage"), Width = 800, Height = 570,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new Border { Padding = new Thickness(14), Child = tabs } }.ShowDialog(this);
    }
}
