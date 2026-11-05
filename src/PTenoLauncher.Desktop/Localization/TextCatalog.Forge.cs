namespace PTenoLauncher.Desktop.Localization;

public static partial class TextCatalog
{
    private static void RegisterForgeTexts()
    {
        Texts["ForgeComponents"] = ("Компоненти Forge / NeoForge", "Forge / NeoForge components");
        Texts["ForgeVersions"] = ("Офіційні версії", "Official versions");
        Texts["ForgePrepare"] = ("Завантажити й перевірити компоненти", "Download and verify components");
        Texts["ForgeHint"] = ("Підготовка перевіряє компоненти. «Встановити / відновити» виконує лише підтримувані client processors із явними SHA-1 outputs. Інші формати відхиляються; gameplay ще не перевірено.", "Preparation verifies components. Install / repair runs only supported client processors with explicit SHA-1 outputs. Other formats are rejected; gameplay is unverified.");
        Texts["ForgeUnknownMinecraft"] = ("Сумісність NeoForge з Minecraft перевіряється в інсталяторі. Повне встановлення підтримує лише явні SHA-1 outputs. Невідомі формати відхиляються; gameplay ще не перевірено.", "The installer must confirm NeoForge/Minecraft compatibility. Full installation supports explicit SHA-1 outputs only. Unknown formats are rejected; gameplay is unverified.");
        Texts["ForgeSelect"] = ("Оберіть профіль Forge/NeoForge та офіційну версію завантажувача.", "Select a Forge/NeoForge profile and an official loader version.");
        Texts["ForgeCount"] = ("Версій в офіційному каталозі: {0}.", "Versions in the official catalog: {0}.");
        Texts["ForgePrepared"] = ("Файлів перевірено: {0}. Обробників клієнта очікує виконання: {1}; бібліотек очікує створення: {2}. Артефакт FML: {3}. Встановлення гри ще не завершено.", "Verified files: {0}. Client processors pending: {1}; libraries pending generation: {2}. FML artifact: {3}. Game installation is not complete.");
    }
}
