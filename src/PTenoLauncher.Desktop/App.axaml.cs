using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PTenoLauncher.Desktop.ViewModels;

namespace PTenoLauncher.Desktop;

public sealed partial class App : Application
{
    public Themes.AppearanceManager Appearance { get; private set; } = null!;
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Appearance = new(this); Appearance.Initialize(); Localization.TextCatalog.Apply("uk");
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Program.Services is not null)
        {
            var vm = new MainViewModel(Program.Services);
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;
            window.Opened += async (_, _) =>
            {
                await vm.InitializeAsync();
                if (Program.Arguments.Contains("--smoke-test"))
                {
                    if (Program.Arguments.Contains("--smoke-fabric"))
                    {
                        vm.SelectedProfile = vm.Profiles.LastOrDefault(p => p.Loader == PTenoLauncher.Core.ModLoader.Fabric);
                        vm.Offline = true;
                        await vm.LoadFabricVersionsAsync();
                    }
                    if(Program.Arguments.Contains("--smoke-quilt"))
                    {
                        vm.SelectedProfile=vm.Profiles.LastOrDefault(p=>p.Loader==PTenoLauncher.Core.ModLoader.Quilt);
                        vm.Offline=true;await vm.LoadQuiltVersionsAsync();
                    }
                    var versionIndex=Array.IndexOf(Program.Arguments,"--smoke-cart-version");
                    if(versionIndex>=0 && versionIndex+1<Program.Arguments.Length)
                    {
                        vm.Offline=true;vm.SelectedRemoteVersion=await Program.Services.Modrinth.GetVersionAsync(Program.Arguments[versionIndex+1]);
                        await vm.AddRemoteAsync();
                        DispatcherTimer.RunOnce(()=>window.ModsScroll.Offset=new Vector(0,window.ModsScroll.Extent.Height),TimeSpan.FromMilliseconds(700));
                    }
                    var pageIndex = Array.IndexOf(Program.Arguments, "--smoke-page");
                    if (pageIndex >= 0 && pageIndex + 1 < Program.Arguments.Length && Enum.TryParse<LauncherPage>(Program.Arguments[pageIndex + 1], out var page)) vm.Page = page;
                    Dialogs.ModBaselineDialog? reviewDialog = null;
                    if (Program.Arguments.Contains("--smoke-review-mods") && vm.CanManageMods)
                    {
                        reviewDialog = new(vm); await reviewDialog.RefreshAsync(); reviewDialog.Show(window);
                    }
                    var marker = Path.Combine(Program.Services.Paths.Root, "gui-smoke.txt");
                    await File.WriteAllTextAsync(marker, $"Window opened; {vm.Profiles.Count} profiles; page={vm.Page}; mods={vm.CanManageMods}; initialized={vm.IsInitialized}; fabric={vm.SelectedFabricVersion?.Version}; quilt={vm.SelectedQuiltVersion?.Version}; operation={vm.LastOperationSucceeded}; optional={vm.OptionalChoices.Count}; {vm.Status}");
                    DispatcherTimer.RunOnce(() => {
                        if (Program.Arguments.Contains("--capture-ui"))
                        {
                            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)(reviewDialog ?? (Avalonia.Controls.Window)window).Bounds.Width, (int)(reviewDialog ?? (Avalonia.Controls.Window)window).Bounds.Height));
                            bitmap.Render(reviewDialog ?? (Avalonia.Controls.Window)window); bitmap.Save(Path.Combine(Program.Services.Paths.Root, "window-preview.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                        }
                        reviewDialog?.Close(); window.Close();
                    }, TimeSpan.FromSeconds(2));
                }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}