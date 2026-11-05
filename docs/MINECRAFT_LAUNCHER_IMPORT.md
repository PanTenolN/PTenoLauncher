# Minecraft Launcher — локальний імпорт вмісту

Adapter MODPACKS-03 — окремий від Prism/MultiMC. Створено й розробляється за допомогою ШІ (OpenAI Codex). Імпортує лише дозволений локальний вміст **нового незалежного профілю**, не встановлену гру або акаунти.

## Користування

1. На «Модпаки» натисніть **«Імпорт Minecraft Launcher: обрати файл профілів…»**.
2. Явно оберіть `launcher_profiles.json` або `launcher_profiles_microsoft_store.json` своєї копії установки. Автоматичного пошуку `.minecraft` по дисках немає.
3. Оберіть конкретний запис профілю й перерахуйте preview. Лаунчер показує точний `lastVersionId`; symbolic latest не перетворюється на поточну версію мовчки.
4. **Явно оберіть теку вмісту цього профілю.** Якщо `gameDir` відсутній — це тека з вибраним JSON. Якщо заданий — потрібно явно вибрати саме ту абсолютну локальну теку; інший шлях не підставляється. Зовнішній шлях із JSON не відкривається/сканується автоматично. UNC/network roots і links не підтримуються.
5. Перегляньте name/target/files/sizes/exclusions/static findings/blockers, за бажанням перейменуйте новий профіль або закріпіть локальні JAR. Перерахуйте preview після змін і надайте згоду.
6. Імпортуйте. Далі **окремо** встановіть Minecraft/Java/точний loader і перевірте readiness. Жодного installation receipt або готовності до гри цей імпорт не створює.

Офлайн працює без HTTP, AI, Microsoft approval або інтерактивного входу. Авторизований gameplay — окрема перевірка. Оригінальні source files не змінюються.

## Підтримуваний контракт

- JSON object з `profiles`; максимум 256 записів, metadata до 4 MiB, bounded JSON depth/node/duplicate guards. Для використаного профілю підтримано `name`, точний `lastVersionId`, необов’язковий `gameDir` і відомі візуальні/settings поля, які не копіюються. Невідомі profile fields/hooks — blocker.
- `lastVersionId` — **посилання**, а не джерело версії loader за назвою. Оpaque IDs без цифр підтримуються, якщо version JSON є та проходить контракт. Заборонені небезпечні сегменти, вихід за межі `versions`, reserved latest references.
- Читаються лише необхідні `versions/<id>/<id>.json` та, для підтримуваного loader, одна base metadata. `id` повинен збігатися; точна Minecraft version береться з підтвердженого `id`/`inheritsFrom`, loader version — з єдиної відповідної Maven coordinate.
- Сучасний Vanilla: `net.minecraft.client.main.Main`, bounded local version metadata з client download descriptor офіційного HTTPS Mojang origin та SHA-1 правильної форми. Це структурні локальні дані, **не криптографічне підтвердження походження**. Сторонні libraries/assets/client JAR не копіюються і не запускаються.
- Обмежений Fabric/Quilt: відомий client Knot mainClass, одна Vanilla base metadata, єдина точна `net.fabricmc:fabric-loader` або `org.quiltmc:quilt-loader`, підтримані стандартні library coordinates; якщо є intermediary — він має збігатися з Minecraft target. Custom extra libraries/arguments/version fields або багаторівневе inheritance не підтримуються. Empty game/JVM argument arrays дозволені; довільні команди не виконуються.
- Forge/NeoForge, OptiFine, historical/custom bootstrap/version patches та інші профілі **залишаються заблокованими** цим adapter. Вони не підміняються Vanilla, latest або Fabric. Це обмежений контракт, а не обіцянка імпорту кожного source profile.
- Source `javaArgs`, RAM/Java paths, resolution/log settings не переносяться. Новий профіль отримує 4096 MB та Java selection PTenoLauncher; policy видно в preview. Нічого не записується у вихідний launcher.
- Копіюються лише дозволені `mods/config/resourcepacks/shaderpacks`, з існуючими eligibility/path/link/collision/budget guards. Saves/worlds, accounts/tokens/sessions, screenshots/logs, launcher/updater settings, game binaries/libraries/assets/runtime та foreign receipts виключені. Directory recursion не йде у виключені worlds/account/runtime trees.
- Вибраний profiles JSON може історично містити account fields. Вони не відображаються, не передаються AI, не використовуються для входу, не записуються в provenance/logs чи цільовий профіль. Парситься bounded явно вибраний файл; його hash зберігається, raw JSON — ні.

## Транзакція й цілісність

Preview/consent fingerprint прив’язаний до точного profiles file, selected profile key, exact content directory, sanitized requirements, hashes profiles/version/base metadata і inventory дозволених файлів. Після зміни source/profile/target/selection потрібний новий preview. Під час копіювання перевіряються розміри й SHA-256; перед Ready source перевіряється знову. Це change detection, не OS snapshot або sandbox довільного коду.

Повторно використано staging, leases, durable journal, точний file-set verification, insert-only SQLite publication, cancellation/retry/recovery з `.mrpack`/ZIP/Prism pipeline. Після Ready recovery може завершити перевірений snapshot без джерела; Preparing не публікується. Чужі/новіші bytes у цілі блокують recovery. Source, worlds та інші профілі не видаляються.

Receipt зберігає окремий `local-minecraft-launcher` source kind і hash-verified `minecraft-launcher-target.json`; raw profile file, credentials або absolute source gameDir туди не копіюються. Локальні JAR не отримують вигаданого Modrinth identity. Static dependencies/nested review не стають доказом gameplay чи відсутності загроз.

## Primary references / scope of evidence

Публічний Minecraft Help описує installations/configuration workflow, але не публікує повну стабільну schema launcher JSON:

- https://help.minecraft.net/hc/en-us/articles/23431114561037-Troubleshooting-Launching-Minecraft-from-the-Minecraft-Launcher
- https://help.minecraft.net/hc/pt-br/articles/360034754852-Change-Game-Versions-for-Minecraft-Java-Edition

Конкретні поля й Windows variants перевірено за **офіційним FabricMC installer** — producer цього самого launcher profile contract:

- https://github.com/FabricMC/fabric-installer/blob/master/src/main/java/net/fabricmc/installer/client/ProfileInstaller.java — blob `65d9721651e1608dc3cad52a653fae00af25eb36`: `profiles`, `name`, `type`, `lastVersionId`, WIN32/Microsoft Store filenames.
- https://github.com/FabricMC/fabric-installer/blob/master/src/main/java/net/fabricmc/installer/client/ClientInstaller.java — blob `2996e5171bab98308694753ab2de0991afb34b97`: version directory, exact profile JSON, inheritance supplied by official metadata, separate libraries.

Це independently implemented bounded reader; сторонній код не скопійовано. Fixtures/synthetic native UI відокремлені від **справжніх source profiles**, які не надавались і не перевірялись. Ліцензійний gameplay, повна ширина third-party formats, Modrinth/CurseForge App imports, license-aware export та friend comparison — окремі наступні блоки. Sinytra/Kilt/Cross-Loader залишаються DEFERRED.
