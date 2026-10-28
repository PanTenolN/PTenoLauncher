using PTenoLauncher.Core;
namespace PTenoLauncher.Desktop.ViewModels;
public sealed class OptionalDependencyChoice : ObservableObject
{
    public OptionalModDependency Option {get;}
    public bool CanSelect => Option.CanSelect;
    public string Title => (Option.Dependency.ProjectId ?? Option.Dependency.VersionId ?? Option.Dependency.FileName ?? "Невідомий компонент") + " · для " + Option.Parent;
    public string Note => !CanSelect ? "Немає підтвердженого ID; автоматичний вибір недоступний." : Option.Dependency.VersionId is string version ? "Modrinth · точна версія " + version : "Modrinth · сумісну версію буде підібрано під час застосування вибору.";
    private readonly Action<OptionalDependencyChoice> changed;
    private bool selected;
    public bool IsSelected {get=>selected;set{if(CanSelect && Set(ref selected,value))changed(this);}}
    public OptionalDependencyChoice(OptionalModDependency option,bool selected,Action<OptionalDependencyChoice> changed)
    {Option=option;this.selected=selected && option.CanSelect;this.changed=changed;}
}
