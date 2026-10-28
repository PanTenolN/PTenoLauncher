using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Core;
namespace PTenoLauncher.Desktop.ViewModels;
public sealed class OptionalDependencyChoice : ObservableObject
{
    public OptionalModDependency Option {get;}
    public bool CanSelect => Option.CanSelect;
    public string Title => (Option.Dependency.ProjectId ?? Option.Dependency.VersionId ?? Option.Dependency.FileName ?? TextCatalog.Get("Message068")) + TextCatalog.Get("Message069") + Option.Parent;
    public string Note => !CanSelect ? TextCatalog.Get("Message070") : Option.Dependency.VersionId is string version ? TextCatalog.Get("Message071") + version : TextCatalog.Get("Message072");
    private readonly Action<OptionalDependencyChoice> changed;
    private bool selected;
    public bool IsSelected {get=>selected;set{if(CanSelect && Set(ref selected,value))changed(this);}}
    public OptionalDependencyChoice(OptionalModDependency option,bool selected,Action<OptionalDependencyChoice> changed)
    {Option=option;this.selected=selected && option.CanSelect;this.changed=changed;}
}
