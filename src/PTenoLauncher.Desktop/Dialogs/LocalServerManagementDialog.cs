using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Minecraft;

namespace PTenoLauncher.Desktop.Dialogs;

internal sealed class LocalServerSettingsDialog : Window
{
    private static readonly string[] Modes = ["survival", "creative", "adventure", "spectator"];
    private static readonly string[] Difficulties = ["peaceful", "easy", "normal", "hard"];
    private readonly TextBox name = new() { MaxLength = 80 };
    private readonly TextBox motd = new() { MaxLength = 255 };
    private readonly TextBox players = new() { MaxLength = 4 };
    private readonly TextBox port = new() { MaxLength = 5 };
    private readonly TextBox memory = new() { MaxLength = 5 };
    private readonly ComboBox mode = new() { ItemsSource = Modes.Select(OptionLabel).ToArray() };
    private readonly ComboBox difficulty = new() { ItemsSource = Difficulties.Select(OptionLabel).ToArray() };
    private readonly CheckBox pvp = new() { Content = "PvP" };
    private readonly TextBlock status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private bool busy;

    public LocalServerSettingsDialog(VanillaServerManager manager, LocalServerSettingsSnapshot snapshot)
    {
        Title = TextCatalog.Get("ServerSettingsTitle");
        Width = 580;
        MaxHeight = 680;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var form = new StackPanel { Margin = new Thickness(18), Spacing = 8 };
        var save = new Button { Content = TextCatalog.Get("ServerSettingsPreview"), Classes = { "primary" } };
        var restore = new Button { Content = TextCatalog.Get("ServerSettingsRestore"), IsEnabled = snapshot.Previous is not null };
        var cancel = new Button { Content = TextCatalog.Get("Cancel"), IsCancel = true };
        form.Children.Add(new TextBlock { Text = TextCatalog.Get("ServerSettingsRestart"), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        foreach (var (label, control) in new (string, Control)[]
        {
            ("Name", name), ("ServerMotd", motd), ("ServerGameMode", mode), ("ServerDifficulty", difficulty),
            ("ServerMaxPlayers", players), ("ServerPort", port), ("ServerMemory", memory)
        })
        {
            form.Children.Add(new TextBlock { Text = TextCatalog.Get(label) });
            form.Children.Add(control);
        }
        form.Children.Add(pvp);
        form.Children.Add(status);
        form.Children.Add(new WrapPanel { ItemSpacing = 8, Children = { save, restore, cancel } });
        Content = new ScrollViewer { Content = form };
        Load(snapshot.Settings);
        MemoryInput.Attach(memory);
        Closing += (_, e) => e.Cancel = busy;
        restore.Click += (_, _) => { if (snapshot.Previous is { } previous) Load(previous); };
        cancel.Click += (_, _) => Close();
        save.Click += async (_, _) =>
        {
            if (busy) return;
            try
            {
                if (mode.SelectedIndex < 0 || difficulty.SelectedIndex < 0 ||
                    !int.TryParse(players.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var max) ||
                    !int.TryParse(port.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var portNumber) ||
                    !MemoryInput.TryNormalize(memory.Text, out var ram))
                    throw new InvalidDataException("ServerSettingsInvalid");
                memory.Text = ram.ToString(CultureInfo.InvariantCulture);
                var next = new LocalServerSettings(name.Text?.Trim() ?? "", motd.Text ?? "", Modes[mode.SelectedIndex],
                    Difficulties[difficulty.SelectedIndex], pvp.IsChecked == true, max, portNumber, ram,
                    snapshot.Settings.AllowList, snapshot.Settings.EnforceAllowList);
                VanillaServerManager.ValidateSettings(snapshot.Server, next);
                var diff = DescribeChanges(snapshot.Settings, next);
                if (diff.Length == 0) { status.Text = TextCatalog.Get("ServerSettingsUnchanged"); return; }
                var confirm = new ContentActionDialog(Title!, TextCatalog.Get("ServerSettingsApply"), TextCatalog.Get("ServerSettingsRestart"), diff, next.Name);
                if (!(await confirm.ShowDialog<bool?>(this)).HasValue) return;
                busy = true;
                form.IsEnabled = false;
                var updated = await manager.ApplySettingsAsync(snapshot, next);
                busy = false;
                Close(updated);
            }
            catch (Exception error) { status.Text = TextCatalog.Get(error.Message); }
            finally { busy = false; form.IsEnabled = true; }
        };
    }

    private void Load(LocalServerSettings settings)
    {
        name.Text = settings.Name;
        motd.Text = settings.Motd;
        mode.SelectedIndex = Array.IndexOf(Modes, settings.GameMode);
        difficulty.SelectedIndex = Array.IndexOf(Difficulties, settings.Difficulty);
        pvp.IsChecked = settings.Pvp;
        players.Text = settings.MaxPlayers.ToString(CultureInfo.InvariantCulture);
        port.Text = settings.Port.ToString(CultureInfo.InvariantCulture);
        memory.Text = settings.MemoryMb.ToString(CultureInfo.InvariantCulture);
    }

    private static string OptionLabel(string value) => TextCatalog.Get("ServerOption" + value);

    private static string DescribeChanges(LocalServerSettings old, LocalServerSettings next)
    {
        var rows = new List<string>();
        Add("Name", old.Name, next.Name);
        Add("ServerMotd", old.Motd, next.Motd);
        Add("ServerGameMode", OptionLabel(old.GameMode), OptionLabel(next.GameMode));
        Add("ServerDifficulty", OptionLabel(old.Difficulty), OptionLabel(next.Difficulty));
        Add("PvP", Enabled(old.Pvp), Enabled(next.Pvp));
        Add("ServerMaxPlayers", old.MaxPlayers, next.MaxPlayers);
        Add("ServerPort", old.Port, next.Port);
        Add("ServerMemory", old.MemoryMb, next.MemoryMb);
        return string.Join(Environment.NewLine, rows);

        static string Enabled(bool value) => TextCatalog.Get(value ? "WorkingEnabled" : "WorkingDisabled");
        void Add<T>(string key, T before, T after)
        {
            if (!EqualityComparer<T>.Default.Equals(before, after)) rows.Add($"{TextCatalog.Get(key)} · {before} → {after}");
        }
    }
}

internal sealed class LocalServerPlayersDialog : Window
{
    private readonly TabControl tabs = new();
    private readonly ListBox[] lists = [new(), new(), new()];
    private readonly TextBox username = new() { MaxLength = 16, MinWidth = 180, PlaceholderText = TextCatalog.Get("ServerPlayerName") };
    private readonly TextBlock status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private bool busy;

    public LocalServerPlayersDialog(VanillaServerManager manager, LocalServerCard card, ServerPlayerAccess current)
    {
        var server = card.Server;
        Title = TextCatalog.Get("ServerPlayersTitle") + " · " + server.Name;
        Width = 690;
        Height = 510;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        tabs.ItemsSource = new[] { "ServerPlayersAllowed", "ServerPlayersBlocked", "ServerPlayersOperators" }
            .Select((key, index) => new TabItem { Header = TextCatalog.Get(key), Content = lists[index] }).ToArray();
        tabs.SelectedIndex = 0;
        var add = new Button { Content = TextCatalog.Get("ServerPlayerAdd"), Classes = { "primary" } };
        var remove = new Button { Content = TextCatalog.Get("ServerPlayerRemove") };
        var close = new Button { Content = TextCatalog.Get("Close"), IsCancel = true };
        var intro = new TextBlock
        {
            Text = TextCatalog.Get("ServerPlayersLiveHint"),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        var actions = new WrapPanel { ItemSpacing = 8, Children = { username, add, remove, close } };
        var panel = new Grid { Margin = new Thickness(18), RowDefinitions = new("Auto,*,Auto,Auto"), RowSpacing = 10 };
        var whitelist = new CheckBox { Content = TextCatalog.Get("ServerWhitelist"), IsChecked = current.Whitelist };
        panel.Children.Add(new StackPanel { Children = { intro, whitelist } });
        Grid.SetRow(tabs, 1); panel.Children.Add(tabs);
        Grid.SetRow(actions, 2); panel.Children.Add(actions);
        Grid.SetRow(status, 3); panel.Children.Add(status);
        Content = panel;
        Load(current.Players);
        Closing += (_, e) => e.Cancel = busy;
        tabs.SelectionChanged += (_, _) =>
        {
            var blocked = tabs.SelectedIndex == (int)ServerPlayerList.Blocked;
            add.Content = TextCatalog.Get(blocked ? "ServerPlayerBlock" : "ServerPlayerAdd");
            remove.Content = TextCatalog.Get(blocked ? "ServerPlayerUnblock" : "ServerPlayerRemove");
        };
        close.Click += (_, _) => Close();
        add.Click += async (_, _) =>
        {
            if (busy || tabs.SelectedIndex < 0) return;
            var list = (ServerPlayerList)tabs.SelectedIndex;
            var name = username.Text?.Trim() ?? "";
            if (list == ServerPlayerList.Operators)
            {
                var confirm = new ContentActionDialog(TextCatalog.Get("ServerPlayersOperators"), TextCatalog.Get("ServerPlayerAdd"),
                    TextCatalog.Get("ServerPlayerGrantOperator"), name, server.Name);
                if (!(await confirm.ShowDialog<bool?>(this)).HasValue) return;
            }
            await Change(async () =>
            {
                await manager.AddPlayerAsync(server, list, name, card.Session);
                username.Text = "";
            });
        };
        remove.Click += async (_, _) =>
        {
            if (busy || tabs.SelectedIndex < 0 || lists[tabs.SelectedIndex].SelectedItem is not PlayerRow selected) return;
            var list = (ServerPlayerList)tabs.SelectedIndex;
            await Change(() => manager.RemovePlayerAsync(server, list, selected.Player.Uuid, card.Session));
        };
        whitelist.Click += async (_, _) =>
        {
            var enabled = whitelist.IsChecked == true;
            whitelist.IsChecked = current.Whitelist;
            if (!busy) await Change(() => manager.SetWhitelistAsync(server, enabled, card.Session));
        };

        async Task Change(Func<Task> operation)
        {
            busy = true;
            panel.IsEnabled = false;
            try
            {
                await operation();
                current = await manager.ReadPlayerAccessAsync(server, card.Session);
                whitelist.IsChecked = current.Whitelist;
                Load(current.Players);
                status.Text = "";
            }
            catch (Exception error) { status.Text = TextCatalog.Get(error.Message); }
            finally { busy = false; panel.IsEnabled = true; }
        }
    }

    private sealed record PlayerRow(ServerPlayer Player)
    {
        public override string ToString() => $"{Player.Name} · {Player.Uuid:D}";
    }

    private void Load(ServerPlayerLists players)
    {
        IReadOnlyList<ServerPlayer>[] entries = [players.Allowed, players.Blocked, players.Operators];
        for (var i = 0; i < lists.Length; i++)
            lists[i].ItemsSource = entries[i].OrderBy(player => player.Name, StringComparer.OrdinalIgnoreCase).Select(player => new PlayerRow(player)).ToArray();
    }
}
