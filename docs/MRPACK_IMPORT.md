# Імпорт .mrpack — MODPACKS-01

Розроблено за допомогою ШІ (OpenAI Codex). Це продовження наявного PTenoLauncher, без заміни архітектури.

## Використання

1. Відкрийте **Модпаки → Обрати .mrpack…**. Preview читає локальний архів і перевіряє кеш, без HTTP-запитів.
2. Перевірте назву нового профілю, Minecraft, точну версію loader, файли, правила клієнта/сервера, overrides та причини блокування.
3. Необов’язкові файли спочатку вимкнені. Після зміни вибору, назви або pins натисніть **Розрахувати preview** й підтвердьте заново.
4. За бажанням увімкніть **Закріпити точні локальні JAR**. Це локальні файлові pins, без вигаданого Modrinth project/version. Обмеження наявної підсистеми pins — 128 JAR.
5. Позначте підтвердження й натисніть **Імпортувати в новий профіль**. Завантаження починаються лише після цього.
6. Після імпорту окремо встановіть Minecraft/Java/loader та перевірте готовність. Запитана версія loader збережена; adapters і readiness не підміняють її іншою. Авторизація й ліцензійні перевірки гри залишаються обов’язковими.

## Формат і політика

Офіційна специфікація: https://support.modrinth.com/en/articles/8802351-modrinth-modpack-format-mrpack . Переглянуто 2026-10-30.

Підтримано `formatVersion=1`, `game=minecraft`, UTF-8 `modrinth.index.json`, Minecraft і один із Fabric/Quilt/Forge/NeoForge. Client-required файли включаються; optional потребують вибору; client-unsupported виключаються незалежно від dedicated-server вимоги. Спочатку накладаються `overrides`, потім `client-overrides`. `server-overrides` перевіряються, але не копіюються до клієнта. Суперечливий override для явно client-unsupported файла блокується; явно server-only JAR з overrides також не публікується до клієнта. Обидва SHA-1/SHA-512 та точний розмір download перевіряються. Спільні payload із однаковими хешами використовують один кешований файл.

Власні обмеження PTenoLauncher: 512 MiB для архіву/одного файла, 1 GiB розпакованого вмісту, 2048 фінальних файлів, 8192 ZIP-записи, 4 MiB index/receipt/journal, JSON depth 32, 16 dependencies, до восьми URL на файл. Центральна директорія ZIP перевіряється до створення entry-об’єктів; ZIP64, multi-disk, encryption, невідомі root-файли й поєднання loaders блокуються. Відносний шлях — до 240 символів/24 компонентів. Case/normalization aliases, file/directory collisions, ADS, device names, traversal і посилання заборонені. Ці ліміти свідомо виключають частину великих/незвичних паків.

Initial HTTPS-джерела: `cdn.modrinth.com`, `github.com`, `raw.githubusercontent.com`, `gitlab.com`. До чотирьох redirects із повторною перевіркою; GitHub asset-hosts `release-assets.githubusercontent.com` та `objects.githubusercontent.com` дозволені тільки як redirects. Приватні адреси, інші hosts, credentials, нестандартні порти й fragments не дозволені. Помилка перевірки або невідоме джерело не вимикають guards. Кеш: `cache/modpacks/files/<sha512>.bin`; reuse потребує обох хешів і розміру. Offline не надсилає HTTP; неповний/пошкоджений кеш дає перелік відсутніх файлів.

## Commit і recovery

Кожна операція має новий випадковий profile ID. `modpack-imports/<id>/journal.json` відстежує `Preparing → Ready → Moved → Committed`; staging розміщений у цій самій операції. Після перевірки всієї staging-структури профіль переміщується в `instances/<id>`, повторно перевіряється, а SQLite публікує його через **insert-only** транзакцію. Звичайний profile upsert не використовується. Перед цим JAR читаються статично наявним inspector/dependency/nested resolver. Відомі blockers зупиняють імпорт; Unknown/warnings зберігаються й не стають підтвердженою сумісністю.

`mrpack-import.json` зберігає локальне походження, назву/versionId, SHA-256 архіву, точні вимоги, початковий перелік файлів/хешів і результати static review. Оригінальний index збережено в `mrpack-index.json` з власним хешем. Це provenance початкового імпорту, а не заборона змінювати звичайні configs або гарантія працездатної гри. Root JAR отримують наявний integrity inventory; pins — лише за явного вибору. Minecraft installation receipt під час імпорту не створюється.

Після переривання натисніть **Відновити перерваний імпорт** із підтвердженням. Незавершений `Preparing` позначається `Aborted`, staging зберігається для діагностики; повторний імпорт може використати перевірений кеш. Для `Ready/Moved` відновлення offline перевіряє всі збережені файли й завершує публікацію. Чужа директорія, новіші файли або змінений SQLite-профіль блокують recovery без перезапису. Непідтверджений commit блокує readiness. Автоматичного видалення старих staging/journals/cache немає; наразі сканування обмежене 128 операціями.

Це журнал із відновленням між файловою системою й SQLite, а не абсолютна гарантія проти фізичної втрати диска, довільного збою сховища або ворожої одночасної модифікації тим самим Windows-користувачем. Hash verification не є антивірусом чи доказом авторства пакета. Імпорт не запускає JAR/скрипти, не приймає EULA й не змінює інші профілі, акаунти, світи чи налаштування.

## Що перевіряється окремо

Автоматичні fixtures/actual Avalonia controls, native startup, мережевий імпорт справжнього пакета, встановлення loader та gameplay — різні докази. Поточні результати й збережені failures див. у `MRPACK_IMPORT_CHECKPOINT.md` та кореневому `TEST_REPORT.md`. ZIP/інші launcher imports, export/license workflow, friend sync і остання підтверджена ігрова конфігурація залишаються наступними окремими блоками. Sinytra/Kilt/Cross-Loader залишаються DEFERRED.
