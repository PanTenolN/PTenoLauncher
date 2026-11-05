namespace PTenoLauncher.Desktop.Localization;
public static partial class TextCatalog
{
    private static void RegisterInstanceTexts()
    {
        Texts["InstanceChooseDirectory"] = ("Імпорт Prism / MultiMC: тека…", "Import Prism / MultiMC directory…");
        Texts["InstanceChooseZip"] = ("Імпорт Prism / MultiMC: ZIP…", "Import Prism / MultiMC ZIP…");
        Texts["InstancePolicy"] = ("Спільний формат OneSix v1. Джерело лише читається; створюється новий профіль. Це імпорт вмісту, а не встановлення гри. Невідомі компоненти, зміни компонентів і команди блокуються.", "Common OneSix v1 format. The source is read only; a new profile is created. Content import does not install the game. Unknown components, patches and commands are blocked.");
        Texts["InstanceLayoutLabel"] = ("Prism / MultiMC · корінь: {0} · вміст: {1:0.0} MB · завантаження: 0", "Prism / MultiMC · root: {0} · content: {1:0.0} MB · download: 0");
        Texts["InstanceLayout"] = ("Оберіть одну теку instance або ZIP з одним instance без неоднозначних тек minecraft / .minecraft.", "Choose one instance directory or a single-instance ZIP without ambiguous minecraft / .minecraft roots.");
        Texts["InstanceMetadata"] = ("Метадані instance відсутні, пошкоджені або виходять за підтримуваний формат OneSix v1.", "Instance metadata is missing, malformed or outside the supported OneSix v1 subset.");
        Texts["InstanceComponent"] = ("Компонент або його точна версія не підтримується. Cached version не замінює запитану версію.", "A component or its exact version is unsupported. A cached version does not replace the requested version.");
        Texts["InstanceCustom"] = ("Власні patches, hooks, команди або зміни компонентів не підтримуються й не виконуються.", "Custom patches, hooks, commands or component overrides are unsupported and are never executed.");
        Texts["InstanceExternal"] = ("Зовнішня або пов’язана тека гри не підтримується. Оберіть незалежну копію instance без посилань.", "External or linked game directories are unsupported. Select an independent instance copy without links.");
        Texts["InstanceExclusions"] = ("Імпортуються лише mods, config, resourcepacks і shaderpacks. Світи, акаунти, сесії, runtime, гра та налаштування стороннього лаунчера виключені.", "Only mods, config, resourcepacks and shaderpacks are imported. Worlds, accounts, sessions, runtime, game binaries and other launcher settings are excluded.");
        Texts["InstanceDefaults"] = ("Новий профіль використовує 4 GB RAM і власний вибір Java. Пам’ять, абсолютні шляхи та JVM-параметри джерела не переносяться.", "The new profile uses 4 GB RAM and its own Java selection. Source memory settings, absolute paths and JVM arguments are not copied.");
        Texts["InstanceDisabled"] = ("Вимкнений компонент виключено", "Disabled component excluded");
    }
}
