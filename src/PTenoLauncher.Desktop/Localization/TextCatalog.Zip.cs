namespace PTenoLauncher.Desktop.Localization;
public static partial class TextCatalog
{
    private static void RegisterZipTexts()
    {
        Texts["PackContentTitle"] = ("Імпорт модпака", "Import modpack");
        Texts["ZipChoose"] = ("Обрати ZIP-збірку…", "Choose ZIP pack…");
        Texts["ZipPolicy"] = ("Локальний клієнтський ZIP: лише mods, config, resourcepacks, shaderpacks. Світи, акаунти, налаштування лаунчера, виконувані й інші файли виключено. Вкажіть версії явно; їх не визначають за назвами JAR.", "Local client ZIP: only mods, config, resourcepacks, shaderpacks. Worlds, accounts, launcher settings, executables and other files are excluded. Enter versions explicitly; JAR names are not version evidence.");
        Texts["ZipMinecraft"] = ("Версія Minecraft (обов’язково)", "Minecraft version (required)");
        Texts["ZipLoader"] = ("Завантажувач модів", "Mod loader");
        Texts["ZipLoaderVersion"] = ("Точна версія loader (для Vanilla залиште порожньою)", "Exact loader version (leave empty for Vanilla)");
        Texts["ZipRootLabel"] = ("Корінь вмісту (/ — корінь ZIP)", "Content root (/ means ZIP root)");
        Texts["ZipLayout"] = ("Локальний клієнтський ZIP · корінь: {0} · імпорт: {1:0.0} MB · завантаження: 0", "Local client ZIP · root: {0} · import: {1:0.0} MB · download: 0");
        Texts["ZipExcluded"] = ("Виключено: {0}", "Excluded: {0}");
        Texts["ZipInvalid"] = ("Пошкоджений, небезпечний або непідтримуваний ZIP.", "Corrupt, unsafe or unsupported ZIP.");
        Texts["ZipAdapter"] = ("Цей формат потребує окремого adapter; імпорт як звичайного ZIP заблоковано.", "This format needs a separate adapter; plain ZIP import is blocked.");
        Texts["ZipRoot"] = ("Вміст неоднозначний або корінь не підтримується. Явно оберіть один із доступних коренів.", "Content root is ambiguous or unsupported. Explicitly select an available root.");
        Texts["ZipRequirements"] = ("Вкажіть коректну назву, Minecraft і точну версію loader. Для Vanilla версію loader не задають.", "Enter a valid name, Minecraft and exact loader version. Vanilla has no loader version.");
        Texts["ZipContent"] = ("Архів не містить підтримуваного самодостатнього клієнтського вмісту.", "Archive has no supported self-contained client content.");
    }
}
