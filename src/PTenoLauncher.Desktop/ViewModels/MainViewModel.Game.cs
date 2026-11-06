using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Storage;
using PTenoLauncher.Infrastructure.Diagnostics;

namespace PTenoLauncher.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private const int ConsoleBufferSize = 4000;
    private const int ConsoleCharacterLimit = 4 * 1024 * 1024;
    private const int ConsolePageSize = 300;

    private sealed class ProfileRun(Guid launchId)
    {
        public PTenoLauncher.Infrastructure.Mods.WorkingConfiguration? WorkingCandidate { get; set; }
        public Guid LaunchId { get; } = launchId;
        public Queue<ConsoleLine> Lines { get; } = [];
        public ConsoleLineClassifier Classifier { get; } = new();
        public string State { get; set; } = "WorkspaceStarting";
        public int? ExitCode { get; set; }
        public string? LogFile { get; set; }
        public int Characters { get; set; }
        public bool LimitReached { get; set; }
    }
    private readonly Dictionary<Guid, ProfileRun> profileRuns = [];
    private readonly Queue<Guid> recentConsoleProfiles = [];
    private ProfileRun? SelectedRun => SelectedProfile is { } p && profileRuns.TryGetValue(p.Id, out var run) ? run : null;
    private ProfileRun? historyRun;
    private IReadOnlyList<ConsoleLine> historyLines = [];
    private int historyPage;
    private bool historyHasNext;
    public bool HasConsoleLog => SelectedRun?.LogFile is not null;
    public bool ConsoleShowingHistory => historyRun is not null && ReferenceEquals(historyRun, SelectedRun);
    public bool ConsoleHasPreviousPage => ConsoleShowingHistory && historyPage > 0;
    public bool ConsoleHasNextPage => ConsoleShowingHistory && historyHasNext;
    public string ConsolePageLabel => ConsoleShowingHistory ? TextCatalog.Format("ConsolePage", historyPage + 1) : "";
    public IReadOnlyList<string> ConsoleLines => SelectedRun?.Lines.Select(line => line.Text).ToArray() ?? [];
    public ObservableCollection<ConsoleLine> VisibleConsoleRows { get; } = [];
    private int visibleConsoleCharacters;
    public string ConsoleLimitStatus => SelectedRun?.LimitReached == true ? TextCatalog.Get("ConsoleLimitReached") : "";
    private string? consoleQuery;
    public string? ConsoleQuery
    {
        get => consoleQuery;
        set { if (Set(ref consoleQuery, value)) RefilterConsole(); }
    }
    public IReadOnlyList<string> VisibleConsoleLines => VisibleConsoleRows.Select(line => line.Text).ToArray();
    private bool consoleColored = true;
    private int consoleFilterIndex;
    public bool ConsoleColored { get => consoleColored; set => Set(ref consoleColored, value); }
    public int ConsoleFilterIndex { get => consoleFilterIndex; set { if (Set(ref consoleFilterIndex, value)) RefilterConsole(); } }
    private bool MatchesConsole(ConsoleLine line) =>
        (ConsoleFilterIndex switch { 1 => line.Warning || line.Error, 2 => line.Error, _ => true }) &&
        (string.IsNullOrWhiteSpace(ConsoleQuery) || line.Text.Contains(ConsoleQuery, StringComparison.OrdinalIgnoreCase));
    private void RefilterConsole()
    {
        VisibleConsoleRows.Clear();
        visibleConsoleCharacters = 0;
        IEnumerable<ConsoleLine> lines = ConsoleShowingHistory ? historyLines : (IEnumerable<ConsoleLine>?)SelectedRun?.Lines ?? [];
        foreach (var line in lines.Where(MatchesConsole))
        {
            visibleConsoleCharacters += line.Text.Length;
            VisibleConsoleRows.Add(line);
        }
        ProfileConsoleUpdated?.Invoke();
    }
    public string ProfileConsoleStatus => SelectedRun is { ExitCode: int code } ? TextCatalog.Format("GameExit", code)
        : TextCatalog.Get(SelectedRun?.State ?? "WorkspaceConsoleEmpty");
    public bool IsSelectedProfileRunning => GameLaunchId is { } launch && SelectedRun?.LaunchId == launch;
    private bool consoleWrap = true, consoleAutoScroll = true;
    public bool ConsoleWrap { get => consoleWrap; set { if (Set(ref consoleWrap, value)) { Changed(nameof(ConsoleWrapMode)); Changed(nameof(ConsoleHorizontalScroll)); } } }
    public bool ConsoleAutoScroll
    {
        get => consoleAutoScroll;
        set { if (Set(ref consoleAutoScroll, value) && value && !ConsoleShowingHistory) RefilterConsole(); }
    }
    public TextWrapping ConsoleWrapMode => ConsoleWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
    public ScrollBarVisibility ConsoleHorizontalScroll => ConsoleWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    public event Action? ProfileConsoleUpdated;
    public void ClearProfileConsole()
    {
        if (SelectedRun is { } run) { run.Lines.Clear(); run.Characters = 0; }
        ShowLiveConsole();
    }

    private void NotifyConsoleHistory()
    {
        Changed(nameof(HasConsoleLog)); Changed(nameof(ConsoleShowingHistory)); Changed(nameof(ConsoleHasPreviousPage));
        Changed(nameof(ConsoleHasNextPage)); Changed(nameof(ConsolePageLabel));
    }

    public void ShowLiveConsole()
    {
        historyRun = null;
        historyLines = [];
        NotifyConsoleHistory();
        RefilterConsole();
    }

    public async Task ShowConsoleHistoryAsync(int direction)
    {
        if (SelectedRun is not { LogFile: { } logFile } run) return;
        var page = direction == 0 || !ConsoleShowingHistory ? 0 : Math.Max(0, historyPage + direction);
        var offset = checked(page * ConsolePageSize);
        var classifier = new ConsoleLineClassifier();
        var rows = new List<ConsoleLine>(ConsolePageSize);
        using var reader = OpenConsoleLog(logFile);
        var more = false;
        for (var index = 0; await reader.ReadLineAsync() is { } text; index++)
        {
            if (index == offset + ConsolePageSize) { more = true; break; }
            var row = classifier.Parse(text);
            if (index >= offset) rows.Add(row);
        }
        if (!ReferenceEquals(run, SelectedRun) || (direction != 0 && !ConsoleShowingHistory)) return;
        historyRun = run;
        historyPage = page;
        historyHasNext = more;
        historyLines = rows;
        NotifyConsoleHistory();
        RefilterConsole();
    }

    public async Task<string?> ReadFullConsoleAsync()
    {
        if (SelectedRun is not { LogFile: { } logFile } run) return null;
        using var reader = OpenConsoleLog(logFile);
        var text = await reader.ReadToEndAsync();
        return ReferenceEquals(run, SelectedRun) ? text : null;
    }

    private static StreamReader OpenConsoleLog(string path)
    {
        SafePath.RejectLinks(path);
        return new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8192,
            FileOptions.Asynchronous | FileOptions.SequentialScan));
    }
    internal void RelocateConsoleLog(ProfileFolderMove move)
    {
        if (profileRuns.TryGetValue(move.ProfileId, out var run) && run.LogFile is { } file)
        {
            var relative = Path.GetRelativePath(move.Source, file).Replace('\\', '/');
            if (relative.StartsWith("launcher-logs/", StringComparison.Ordinal))
                run.LogFile = SafePath.Join(move.Target, relative);
        }
        RefreshWorkspaceView();
    }

    private void RefreshProfileConsole()
    {
        Changed(nameof(ConsoleLines)); Changed(nameof(ProfileConsoleStatus)); Changed(nameof(IsSelectedProfileRunning)); Changed(nameof(ConsoleLimitStatus));
        if (historyRun is not null && !ConsoleShowingHistory) ShowLiveConsole();
        else { NotifyConsoleHistory(); RefilterConsole(); }
    }

    internal async Task<int> RunProfileConsoleAsync(Guid profileId,
        Func<IProgress<string>, Action, Action<string>, CancellationToken, Task<int>> runProcess, CancellationToken ct)
    {
        var run = new ProfileRun(Guid.NewGuid());
        if (!profileRuns.ContainsKey(profileId))
        {
            if (recentConsoleProfiles.Count == 16) profileRuns.Remove(recentConsoleProfiles.Dequeue());
            recentConsoleProfiles.Enqueue(profileId);
        }
        profileRuns[profileId] = run;
        SetGameLaunch(run.LaunchId);
        Status = TextCatalog.Get(run.State);
        RefreshProfileConsole();
        var output = new Progress<string>(batch =>
        {
            if (!profileRuns.TryGetValue(profileId, out var current) || !ReferenceEquals(current, run)) return;
            var selected = SelectedProfile?.Id == profileId;
            var visible = selected && !ConsoleShowingHistory;
            foreach (var text in batch.Split('\n'))
            {
                var line = run.Classifier.Parse(text.TrimEnd('\r'));
                if (text == LiveProcessRunner.TranscriptLimitNotice)
                {
                    run.LimitReached = true;
                    if (selected) Changed(nameof(ConsoleLimitStatus));
                }
                run.Lines.Enqueue(line);
                run.Characters += line.Text.Length;
                while (run.Lines.Count > ConsoleBufferSize || run.Characters > ConsoleCharacterLimit)
                {
                    var oldest = run.Lines.Dequeue();
                    run.Characters -= oldest.Text.Length;
                    if (visible && ConsoleAutoScroll && VisibleConsoleRows.Count > 0 && ReferenceEquals(VisibleConsoleRows[0], oldest))
                    {
                        visibleConsoleCharacters -= oldest.Text.Length;
                        VisibleConsoleRows.RemoveAt(0);
                    }
                }
                if (visible && VisibleConsoleRows.Count < ConsoleBufferSize &&
                    visibleConsoleCharacters + line.Text.Length <= ConsoleCharacterLimit && MatchesConsole(line))
                {
                    VisibleConsoleRows.Add(line);
                    visibleConsoleCharacters += line.Text.Length;
                }
            }
            if (visible) ProfileConsoleUpdated?.Invoke();
        });
        var started = new Progress<bool>(_ =>
        {
            if (GameLaunchId != run.LaunchId || run.State != "WorkspaceStarting") return;
            run.State = "WorkspaceRunning";
            GameProcessStarted = true;
            Changed(nameof(GameProcessStarted));
            Status = TextCatalog.Get(run.State);
            if (SelectedProfile?.Id == profileId) Changed(nameof(ProfileConsoleStatus));
        });
        var logOpened = new Progress<string>(path =>
        {
            run.LogFile = path;
            if (ReferenceEquals(run, SelectedRun)) NotifyConsoleHistory();
        });
        try
        {
            var code = await runProcess(output, () => ((IProgress<bool>)started).Report(true),
                path => ((IProgress<string>)logOpened).Report(path), ct);
            run.ExitCode = code;
            run.State = "WorkspaceFinished";
            Status = TextCatalog.Format("GameExit", code);
            return code;
        }
        catch (OperationCanceledException) { run.State = "Message020"; throw; }
        catch { run.State = "WorkspaceLaunchFailed"; throw; }
        finally { SetGameLaunch(null); Changed(nameof(ProfileConsoleStatus)); Changed(nameof(IsSelectedProfileRunning)); }
    }
    public Guid? GameLaunchId { get; private set; }
    public bool GameProcessStarted { get; private set; }
    public bool IsGameLaunchActive => GameLaunchId is not null;
    public bool CanCancelOperation => IsBusy && !IsGameLaunchActive;

    private void SetGameLaunch(Guid? id)
    {
        GameLaunchId = id;
        GameProcessStarted = false;
        Changed(nameof(GameProcessStarted));
        Changed(nameof(GameLaunchId)); Changed(nameof(IsGameLaunchActive)); Changed(nameof(CanLeaveWorkspace)); Changed(nameof(CanCancelOperation)); RefreshHomeAction();
        CancelCommand.Refresh();
    }

    public async Task<bool> StopGameAsync(Guid launchId, bool confirmed)
    {
        // A confirmation for an old launch must never stop a newer operation.
        if (!confirmed || GameLaunchId != launchId || operation is null) return false;
        await operation.CancelAsync();
        return true;
    }
}
