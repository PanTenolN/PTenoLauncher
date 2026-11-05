# Локальний імпорт Prism / MultiMC

PTenoLauncher продовжує наявний проєкт за допомогою ШІ (OpenAI Codex). Цей adapter імпортує **вміст нового незалежного профілю**, а не сторонню установку гри або акаунт.

## Як користуватися

1. Відкрийте «Модпаки». Оберіть «Імпорт Prism / MultiMC: тека…» або ZIP.
2. Для теки оберіть саме один instance з `instance.cfg`, `mmc-pack.json` і `minecraft` **або** `.minecraft`. Для ZIP ці файли мають бути в корені або в одній wrapper-теці.
3. Перегляньте точні вимоги Minecraft/loader, перелік файлів, розміри, виключення, статичні findings і blockers. За бажанням перейменуйте новий профіль та ввімкніть локальне закріплення JAR. Перерахуйте preview після змін.
4. Підтвердьте перелік і натисніть імпорт. До цього файли профілю й SQLite не публікуються.
5. Після переривання скористайтеся підтвердженим відновленням на цій самій сторінці. Незавершена підготовка відхиляється; повністю перевірений snapshot може завершити публікацію без джерела. Змінений або чужий цільовий вміст блокує recovery.

Офлайн-імпорт локального самодостатнього джерела працює без HTTP, AI або входу. Після імпорту **окремо** встановіть Minecraft, Java і точну версію loader; перевірте readiness. Авторизований gameplay перевіряється окремо. Імпорт не створює installation receipt і не обіцяє готовності гри.

## Підтримуваний формат і межі

- Спільний `InstanceType=OneSix`, `mmc-pack.json` **formatVersion=1**, UTF-8 INI з необов’язковою секцією `[General]`. Семантику перевірено за офіційним кодом **Prism 9.4** та **MultiMC develop commit a93647e8e9144bd656080919d1e21cfea4cf5d1d**. Це не твердження про перевірку кожного їхнього профілю чи всіх майбутніх версій.
- За цими metadata неможливо довести, який саме launcher створив instance. Provenance чесно називається `local-prism-multimc`.
- Авторитетні поля component — `uid` і точний `version`. `cachedVersion` не використовується як заміна. Підтримуються активний `net.minecraft` та не більш як один із `net.fabricmc.fabric-loader`, `org.quiltmc.quilt-loader`, `net.minecraftforge`, `net.neoforged`.
- Disabled components явно показуються як виключені. Дублікати, невідомі активні компоненти, відсутні точні версії, кілька loaders, custom поля/patches/jarmods і команди блокуються. Непідтримувані ancillary LWJGL/intermediary requests також **блокуються**, навіть з `dependencyOnly=true`; вони не відкидаються мовчки. Через це частина звичайних Prism/MultiMC instances поки потребуватиме розширення adapter. Не видаляйте metadata навмання, щоб обійти blocker.
- Коли присутні обидві теки гри, імпорт блокується: Prism і MultiMC мають різні defaults. External paths і links не використовуються. Глобальні налаштування стороннього launcher не читаються; їхні hooks не успадковуються.
- Копіюються лише дозволені клієнтські `mods`, `config`, `resourcepacks`, `shaderpacks`. Світи/saves, screenshots/logs, accounts/sessions/tokens, runtime, libraries/assets/game binaries, сторонні receipts/settings/updater state виключені. Приватні/виконувані файли за спільною ZIP eligibility policy виключаються. Довільні модові config слід переглянути перед копіюванням: локальний імпорт не є гарантією відсутності приватного тексту в довільному файлі.
- RAM не переноситься: новий профіль отримує стандартні 4096 MB; Java обирається PTenoLauncher. Абсолютні шляхи, JVM arguments і команди не копіюються. Непорожні instance JVM arguments/hooks блокуються, а не виконуються.
- Вихідна тека лише читається. Рекурсія обмежена дозволеними категоріями; saves, accounts і runtime не скануються рекурсивно. ZIP headers перевіряються для всіх записів без читання виключених payloads. Preview може показувати виключену теку без розміру її нечитаних нащадків.

## Цілісність і транзакція

Ліміти: 256 KiB на кожен metadata file, JSON depth 32, максимум 32 components, 8192 source/archive entries, 2048 імпортованих файлів, 128 root JAR, 512 MiB на архів/файл і 1 GiB розгорнутого/перерахованого вмісту. Застосовуються чинні compression, traversal, symlink/reparse, ADS/device, Unicode normalization, case та file/directory collision guards. Непідтримувані/небезпечні форми відхиляються до імпорту.

Для ZIP зберігається SHA-256 архіву; для теки — детермінований fingerprint перевіреного metadata/content scope та layout. Кожен дозволений файл має окремі розмір і SHA-256; зміни до apply або під час копіювання відхиляються, source повторно перевіряється перед Ready journal. Це виявлення змін, не OS filesystem snapshot чи sandbox довільного коду. JAR/скрипти/сторонній launcher не виконуються.

Використовується наявна спільна транзакція `.mrpack`/ZIP: свіжий GUID, lease, staging, durable journal, точна перевірка inventory, move, insert-only SQLite, повторний guarded commit/recovery. Receipt `mrpack-import.json` має окремий SourceKind; sanitized `instance-import-target.json` зберігає точні компоненти і metadata hash без сирих налаштувань/секретів. Старі .mrpack/ZIP receipts залишаються сумісними. Локальні JAR мають лише локальну identity/pins, без вигаданого Modrinth project/version.

Static mod/dependency/native nested review повторно використовує LocalModManager. Unknown залишається Unknown; static review не є перевіркою gameplay, antivirus або універсальної сумісності.

## Докази та наступні adapter

Актуальні результати: [PRISM_MULTIMC_IMPORT_CHECKPOINT.md](PRISM_MULTIMC_IMPORT_CHECKPOINT.md). Тестові fixture й синтетичний native/process run відокремлюються від справжніх source profiles; користувацькі встановлені launcher/accounts/worlds не використовувалися.

MODPACKS-03 також включає Minecraft Launcher: цей окремий adapter залишається наступним. Modrinth App/CurseForge App (MODPACKS-04), license-aware export і порівняння збірок — окремі наступні задачі. Усі bridges Sinytra/Kilt/Cross-Loader залишаються DEFERRED.

Перевірені primary references та blob hashes: [PRISM_MULTIMC_SPEC_REFERENCES.json](PRISM_MULTIMC_SPEC_REFERENCES.json). Це самостійна реалізація читання формату; сторонній GPL/Apache код не скопійовано в PTenoLauncher.
