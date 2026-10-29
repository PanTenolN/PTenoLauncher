namespace PTenoLauncher.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private bool needsLanguageSelection;
    public bool NeedsLanguageSelection
    {
        get => needsLanguageSelection;
        private set => Set(ref needsLanguageSelection, value);
    }
}
