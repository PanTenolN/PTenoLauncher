namespace PTenoLauncher.Desktop.Localization;
public static partial class TextCatalog
{
    private static void RegisterLauncherUpdaterTexts()
    {
        Texts["UpdaterTitle"] = ("Оновлення PTenoLauncher", "PTenoLauncher updates");
        Texts["UpdaterCheck"] = ("Перевірити оновлення лаунчера", "Check launcher updates");
        Texts["UpdaterAuto"] = ("Перевіряти автоматично при запуску", "Check automatically at startup");
        Texts["UpdaterPreviews"] = ("Дозволити preview-збірки", "Allow preview builds");
        Texts["UpdaterUpdate"] = ("Оновити", "Update");
        Texts["UpdaterLater"] = ("Пізніше", "Later");
        Texts["UpdaterSummary"] = ("Поточна: {0} (збірка {1}) → Нова: {2} (збірка {3}). Завантаження: {4:0.0} MB.", "Current: {0} (build {1}) → New: {2} (build {3}). Download: {4:0.0} MB.");
        Texts["UpdaterProgress"] = ("Завантажено {0:0.0} / {1:0.0} MB", "Downloaded {0:0.0} / {1:0.0} MB");
        Texts["UpdaterPreparing"] = ("Завантаження, перевірка файлів і підготовка резервної копії…", "Downloading, verifying files and preparing backup…");
        Texts["UpdaterRestarting"] = ("Helper готовий. Лаунчер закривається для безпечного оновлення.", "Helper ready. Closing launcher for a safe update.");
        Texts["UpdaterRecover"] = ("Відновити попередню версію", "Restore previous version");
        Texts["UpdaterRecoveryHint"] = ("Попереднє оновлення не підтверджено. Відновлення закриє лаунчер і поверне перевірену резервну версію. Дані користувача зберігаються.", "The previous update is unconfirmed. Recovery closes the launcher and restores the verified backup version. User data is preserved.");
        Texts["UpdaterDeferred"] = ("Пропозицію відкладено до наступного запуску.", "Offer deferred until the next launch.");
        Texts["UpdaterTrust"] = ("Автооновлення недоступне: довірений production-ключ ще не налаштований.", "Updates unavailable: the trusted production key is not configured.");
        Texts["UpdaterSignature"] = ("Підпис оновлення недійсний. Пакет не встановлено.", "Update signature is invalid. Package not installed.");
        Texts["UpdaterInvalid"] = ("Метадані оновлення пошкоджені або не підтримуються.", "Update metadata is corrupt or unsupported.");
        Texts["UpdaterBuild"] = ("Ця збірка або формат даних не підтримує безпечне автооновлення.", "This build or data format does not support safe automatic updating.");
        Texts["UpdaterPath"] = ("Небезпечні шляхи або колізії файлів. Оновлення заблоковано.", "Unsafe paths or file collisions. Update blocked.");
        Texts["UpdaterOrigin"] = ("Джерело пакета не належить офіційному каналу PTenoLauncher.", "Package source is outside the official PTenoLauncher channel.");
        Texts["UpdaterReplay"] = ("Отримано застарілий каталог оновлень. Його відхилено.", "An older update catalog was received and rejected.");
        Texts["UpdaterCurrent"] = ("Новішої версії у перевіреному каналі не знайдено.", "No newer version found in the verified channel.");
        Texts["UpdaterAvailable"] = ("Доступна перевірена новіша збірка.", "A verified newer build is available.");
        Texts["UpdaterOffline"] = ("Офлайн: перевірку оновлень не виконано.", "Offline: no update check was made.");
        Texts["UpdaterUnavailable"] = ("Сервіс оновлень недоступний. Лаунчер можна використовувати далі.", "Update service unavailable. You can keep using the launcher.");
        Texts["UpdaterRecovery"] = ("Потрібне відновлення оновлення. Інші операції тимчасово заблоковано.", "Update recovery required. Other operations are temporarily blocked.");
        Texts["UpdaterIntegrity"] = ("Файли не пройшли перевірку цілісності. Заміну зупинено.", "File integrity verification failed. Replacement stopped.");
        Texts["UpdaterInstalledBlocked"] = ("Не підтверджено належність Installed-установки або її безпечний installer workflow. Заміна заблокована.", "Installed ownership or safe installer workflow is unconfirmed. Replacement is blocked.");
        Texts["UpdaterSpace"] = ("Недостатньо місця для пакета, staging і перевіреного backup.", "Insufficient space for package, staging and verified backup.");
        Texts["UpdaterConflict"] = ("Файли змінилися після перевірки. Новіші дані не перезаписано.", "Files changed after verification. Newer data was not overwritten.");
        Texts["UpdaterConsent"] = ("Завантаження й встановлення потребують натискання «Оновити».", "Downloading and installing require clicking Update.");
        Texts["UpdaterParent"] = ("Не підтверджено завершення потрібного процесу лаунчера. Файли не замінено.", "The exact launcher process has not confirmed exit. Files were not replaced.");
        Texts["UpdaterBusy"] = ("Завершіть гру, сервер або поточну операцію перед оновленням. Вони не зупиняються автоматично.", "Finish the game, server or current operation before updating. They are not stopped automatically.");
        Texts["UpdaterHelper"] = ("Helper не завершив операцію. Backup збережено; перегляньте recovery.", "Helper did not complete the operation. Backup retained; review recovery.");
        Texts["UpdaterHealthTimeout"] = ("Нова версія не підтвердила готовність вчасно. Запущені файли не замінюються; потрібне recovery.", "The new version did not confirm readiness in time. Running files are not replaced; recovery is required.");
        Texts["UpdaterHealthFailed"] = ("Нова версія не пройшла startup-перевірку. Попередню версію збережено для відновлення.", "The new version failed startup verification. Previous version retained for recovery.");
        Texts["UpdaterInstallerFailed"] = ("Інсталятор не завершив оновлення успішно. Backup збережено; потрібне recovery.", "Installer did not complete successfully. Backup retained; recovery is required.");
    }
}
