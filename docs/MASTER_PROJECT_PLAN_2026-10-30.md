<!-- AI-UX-CURRENT -->
Current-reading note — 2026-11-02: this is the **dated master-plan snapshot**, not the newest source report. AI UX was subsequently locally verified; the bounded Prism/MultiMC import is now implemented and covered by the fresh 891-total / 889-passed / two-explicit-NOT-RUN regression. Read [PRISM_MULTIMC_IMPORT_CHECKPOINT.md](PRISM_MULTIMC_IMPORT_CHECKPOINT.md), [AI_USABILITY_CHECKPOINT.md](AI_USABILITY_CHECKPOINT.md) and the root SESSION_CHECKPOINT.md for current evidence/limits. The old paused 34/36 and unbuilt-Prism statements below are historical, not instructions to stop or restore an older source. All 189 requirements / 44 modules / five DEFERRED rows remain; no overall 100% completion claim.

# PTenoLauncher — повний план, вимоги та стан виконання

**Зведення станом на 30 жовтня 2026 року.** Створено та розробляється за допомогою ШІ (OpenAI Codex); напрям і вимоги задає власник.

Це єдиний читабельний план із **184 початковими + 5 доданими = 189 вимогами**, 44 модулями, останніми уточненнями користувача й критеріями завершення. Він не означає, що розробку відновлено або будь-які нові функції реалізовано під час підготовки документа. Код не змінювався; нові build/tests/live API/gameplay не запускалися.

План відновлено з доступних файлів проєкту та уточнень у цій розмові. Не заявляється доступ до всіх неімпортованих старих вкладень/повідомлень. Ранні цифри тестів, commits і релізи є історією; вони не перевіряють автоматично новіший source.

## Навігація

1. [Мета й правила](#purpose)
2. [Поточний стан і докази](#current)
3. [Уточнення власника](#owner)
4. [Повна карта розробки](#areas)
5. [Черговість і залежності](#sequence)
6. [Умови завершення та релізу](#acceptance)
7. [Усі 189 вимог — детальний реєстр](#inventory)
8. [44 модулі й історичні оцінки](#modules)
9. [Повний план нового AI UX](#ai-ux)
10. [Джерела й підтримання актуальності](#sources)

<a id="purpose"></a>
## 1. Мета й незмінні правила

Повноцінний Minecraft Java Edition Launcher для Windows 11 x64, придатний до регулярного використання, а не лише демонстрація UI. C#/.NET 10/Avalonia і SQLite, offline-first, independent profiles та безпечні файлові операції зберігаються. Початкові шари не переписуються; поточне дерево має вісім .csproj з helper/E2E/tools.

- Офіційний Microsoft browser OAuth, власний Client ID, entitlement/profile/refresh. Немає password forms, чужих Client ID, fake tokens або обходу ліцензії.
- Core, mods, deterministic dependencies, backups та локальні редактори працюють без ШІ. ШІ default off; local models/Ollama не додаються.
- Довгі операції async/cancellable; профільні leases/world/process locks не обходяться. Preview та згода перед зміною даних; невідомі файли не видаляються мовчки.
- Власні worlds/accounts/secrets не використовуються в ризикованих тестах. Код/оновлення/плагіни не називаються ізольованими лише через staging directory чи окремий процес.
- На відміну від попередніх загальних доручень, останні запити цього чату дозволяють план/документацію; реалізацію продовжувати окремим запитом. Поточний Prism блок збережений і paused.
- Без окремого запиту немає push/release/production signing. Старі published previews не включають автоматично нові локальні зміни. Не створювати новий проєкт/репозиторій.
- Sinytra, Kilt і всі сторонні cross-loader bridges — **DEFERRED**, а не скасовані. Native nested/Jar-in-Jar не є bridge.

<a id="current"></a>
## 2. Поточний стан і спосіб читання статусів

**Критично:** останній успішний ZIP baseline має 787 сценаріїв, 785 PASS, 2 explicit NOT RUN і Release 0 errors/0 warnings. Після нього з’явилися незавершені Prism/MultiMC source edits без нової збірки/тестів. Тому поточний working source не називається повністю перевіреним.

«Реалізовано» не означає «перевірено у справжній грі». Підготовка файлів ≠ виконання official processors ≠ вікно гри ≠ світ/save/reopen ≠ стабільний реліз. Хеш ≠ антивірусний висновок, а кількість PASS ≠ кількість завершених вимог.

Нижче наведені **статуси вихідної таблиці**, зі збереженням її аудиторських приміток/розбіжностей. Вони не є новою сертифікацією кожного рядка й не дають математичного відсотка готовності.
| Статус вихідного реєстру | Кількість |
|---|---:|
| Реалізовано й перевірено в зазначеному обсязі | 81 |
| Код є; повна перевірка не завершена | 14 |
| Частково реалізовано | 45 |
| Не реалізовано як завершена функція | 37 |
| Зовнішній блокер; реалізацію див. окремо | 7 |
| Відкладено власником — не реалізовувати зараз | 5 |

### Докази з датами

| Блок | Дата | Результат | Файл | Межі |
|---|---|---|---|---|
| Managed Java | 2026-10-29 | 8/17/21/25 Windows runner install/version/offline reuse; Java 21 repair | [JAVA_RUNTIME_CHECKPOINT.md](../docs/JAVA_RUNTIME_CHECKPOINT.md) | Runtime ≠ Minecraft gameplay; локальна мережева невдача лишається невдачею. |
| Updater executable E2E | 2026-10-29 | 3/3 PASSED: update-health; failed-startup-rollback; live-health-timeout-recovery | [result.json](../.local/updater-e2e-1793301080-5a2ca0/result.json) | Ізольовані framework-dependent test builds; не final Installed/distribution. |
| Updater full regression | 2026-10-29 | 688 total / 686 passed / 2 not run | [updater-context-safe-full.trx](../artifacts/0.2.0/tests/updater-context-safe-full.trx) | Старі failed/aborted журнали збережені. |
| MRPACK | 2026-10-30 | 739 total / 737 passed / 2 not run | [mrpack-complete-full.trx](../artifacts/0.2.0/tests/mrpack-complete-full.trx) | Live official catalog FAILED socket 10051 до завантаження пакета. |
| ZIP baseline | 2026-10-30 | 787 total / 785 passed / 2 not run; Release 0/0 | [zip-final-full.trx](../artifacts/0.2.0/tests/zip-final-full.trx) | Останній успішний baseline до незавершених Prism edits. |
| Owned ZIP process | 2026-10-30 | PASSED_LOCAL_CONTENT_IMPORT, 0 HTTP | [zip-import-result.json](../.local/zip-content-process-20261030/zip-import-result.json) | Власний приклад із двома content files, не сторонній модпак. |
| Prism/MultiMC edits | 2026-10-30 | STARTED; NOT BUILT / NOT TESTED | [PRISM_MULTIMC_IMPORT_CHECKPOINT.md](../docs/PRISM_MULTIMC_IMPORT_CHECKPOINT.md) | Є незавершені зміни поверх ZIP baseline; не переносити на них старі PASS. |
| AI UX уточнення | 2026-10-30 | PLANNED; documentation only | [AI_USABILITY_PLAN.md](../docs/AI_USABILITY_PLAN.md) | Реалізація source selectors/chat/library/auto format ще відсутня. |

<a id="owner"></a>
## 3. Реєстр уточнень власника

Це критерії наявних вимог, а не 189 нових задач. Окрема implementation approval, ключ, сертифікат або EULA не вважаються наданими самим існуванням плану.

| Уточнення | Що має бути в результаті | Поточний стан |
|---|---|---|
| Продовжувати наявний проєкт | Зберегти newer source, дані, архітектуру й історію вимог | Зберігати для кожної сесії |
| Працювати без очікування Microsoft | Завершувати незалежні задачі, auth/gameplay позначати окремо | Уже виконувалося; зовнішні blockers не скасовані |
| Відкладені bridges | Sinytra, Kilt, Cross-Loader і всі напрями взаємного запуску залишити DEFERRED | 5 рядків збережені |
| Перед новим промптом перевіряти зроблене | Читати checkpoints, код і результати, не дублювати готові блоки | Обов’язковий робочий порядок |
| Розрізняти PASS і невиконане | Дати, середовище, failed/blocked/not-run, fixtures vs real | Вимога до звітності |
| Перший вибір мови | До нормального першого сеансу 8 native names, preview/confirm/SQLite | Реалізовано в автоматично перевіреному обсязі |
| Вісім мов та graphite/themes | uk/en/pl/de/es/fr/pt-BR/ru; 5 themes; live switch/persistence | Є; technical diagnostics/DPI/linguistic QA часткові |
| Mods лише для modded profile | При Vanilla/відсутньому профілі приховати Mods, не видаляти файли | Є |
| Кнопка mods folder | Відкривати тільки mods вибраного modded профілю | Код/тести є, native Explorer check pending |
| Уточнення updater | При запуску нове вікно з версією/змінами й «Оновити / Пізніше»; один явний клік | UI/helper/restart/recovery є; production ще заблокований |
| Власний API-ключ | Видиме зрозуміле поле, якщо ключ потрібен кожному | Direct client потребує per-user key; поле є, нова connection card planned |
| Зрозумілий ШІ-асистент | Підписане input + Send, розмова, окремі connection settings | Новий UX planned, поточна форма незручна |
| Source → target мови | Два окремі селектори з людськими назвами й фактичним source locale | Planned; зараз source жорстко en_us |
| Мої попередні переклади | Бібліотека, прогрес, search/filter, table editor, offline доступ | Drafts/JSON є; нова бібліотека planned |
| Приховати pack_format та іншу техніку | Визначити за точною MC version; проста генерація й інструкція | Planned; зараз numeric input |
| Зберегти ручні правки | Safe incremental update і migration старих drafts без втрат | Старий scope перевірено; новий source/target scope ще planned |
| Поширення друзям | Preview/export без secrets/game binaries і з правами поширення | Export/friend sync ще не реалізовані |
| Не втрачати worlds/backups | Ownership, locks, confirmation, preserve by default | Частина ядра перевірена; нові менеджери ще потрібні |
| Окрема документація й повний план | Єдиний master із всіма ID, evidence та next actions | Цей документ; не реалізація коду |

<a id="areas"></a>
## 4. Повна карта функціональності

Нижче — очікувана поведінка по всіх напрямках; точні IDs і докази наведені в реєстрі 189 пунктів.

### 4.1. Основа, дані й інтерфейс

**Є:** .NET/Avalonia/SQLite, settings/profiles/history, atomic helpers/log redaction, graphite, Dark/Black/Light/System/Custom, live dictionaries, language onboarding, профільна навігація.

**Залишилося:** наскрізна cancellation/помилки кожного workflow, responsive/DPI/keyboard/contrast/manual QA, повні low-level переклади й dynamic refresh, custom/system theme review. Основні поля/кнопки мають бути підписані й зрозумілі; placeholder сторінка не є реалізацією.

**Готово коли:** settings/theme/language перезапускаються без втрати, UI не блокується, довгі рядки й 100–200% DPI не ховають основні дії, помилки не містять secrets.

### 4.2. Профілі, видалення й backups

**Є:** independent CRUD/archive, RAM/Java settings, deletion preview/count/size/world warnings, exact-name confirm, owned-path guards, shared cache/Java preservation, journals/recovery, snapshots і restore-as-new.

**Залишилося:** вибір довільної backup directory, selected tracked export ZIP UI, інтеграція з новими content managers, повний облік диска/unused cleanup, додаткові translation-specific restore/delete перевірки. Невідомі/cloud/remote ZIP не шукати й не видаляти.

**Готово коли:** жодний delete/restore не зачіпає інший профіль чи shared content, backup за замовчуванням зберігається, interruption не приховує неповний стан.

### 4.3. Microsoft акаунти й офіційна гра

**Є:** browser OAuth код, Xbox/XSTS/Minecraft service flow, protected DPAPI accounts, active selection/targeted/logout-all, skin cache код, redacted stage diagnostics. Історично browser completion був; downstream 403 не є встановленим діагнозом approval.

**Залишилося:** підтвердити approval/allowed API access, entitlement, name/UUID/skin, refresh після restart, multiple real accounts, офіційно підготовлену offline session. Не вимагати нового app registration без причини й не retry нескінченно.

**Готово коли:** дозволений ліцензійний акаунт проходить увесь flow і refresh; ліцензійна online/offline гра перевірена. Це не блокує інші локальні модулі.

### 4.4. Minecraft, snapshots, assets і process lifecycle

**Є:** official manifest/version cache/releases/snapshots/history, Vanilla files/libraries/natives/logging, modern і legacy arguments, virtual/mapped asset plan/journals/repair, readiness, live bounded redacted console, exit/history/process stop confirmation.

**Залишилося:** real historical installation, ширша аргументна/version coverage, custom JVM settings, real game shutdown, світи/save/reopen, snapshot gameplay та relocation. Немає підтвердженого повного Minecraft session.

**Готово коли:** точна версія стартує з потрібною Java, відкриває isolated test world, зберігає/закриває/повторно відкриває; missing/corrupt preparation не оголошується Ready.

### 4.5. Java Manager

**Є:** discovery/major metadata, managed Temurin x64 install/hash/inventory/link guards, cache/offline repair, профільна Java, historical Windows 8/17/21/25 acceptance.

**Залишилося:** UI inventory/update/remove, usage/profile/process guards, retain-old-until-new-verified, реальна інтеграція із грою. Probe environment isolation не прирівнювати до перевірки gameplay.

### 4.6. Native loaders

**Fabric/Quilt:** catalogs/install/cache/repair і частина live components перевірені; native metadata/nested subset є. **Forge/NeoForge:** official catalogs/components, strict client processors, generated hashes, transactional receipts/UI/readiness є, real official artifacts/execution мережево не підтверджені.

**Залишилося:** version-specific unsupported contracts, mappings/missing-output checksum adapters без послаблення guards; реальні installs і online/offline gameplay для кожного loader. Quilt QSL/compatibility не вгадувати за Fabric. Forge і NeoForge не взаємозамінні. Broad support не заявляти для всіх версій.

### 4.7. Моди, провайдери й залежності

**Є:** local JAR inspect/hash, drag/drop wiring, Modrinth search/version/identity/download/cache, deterministic transitive dependencies, required/optional cart, preview/consent, journals, supported Fabric/Quilt/Forge/NeoForge ranges/nested selection, persistent integrity baselines.

**Залишилося:** details/categories/filters, complex unsupported rules/aliases/selector versions, permitted CurseForge provider/credentials/cache, native DnD й real mod sets. Unknown не стає Compatible; JAR name/Mod ID не стає provider identity; plain Jar-in-Jar library не стає mod.

### 4.8. Оновлення модів

**Є:** Modrinth hash identity/publication policy, direct/dependency change preview, exact persistent pins, staged apply, before-state ZIP, journal/recovery, rollback/epochs/stale guards і actual Mods UI.

**Залишилося:** реальна provider update/rollback, manual review, broader scope. Немає automatic orphan delete; user changes не перезаписуються. Backup конфігурації не означає «остання ігрова працездатна версія» без гри.

### 4.9. Модпаки й інші лаунчери

**Є:** .mrpack client import та self-contained ZIP root/one-wrapper, explicit targets/optionals, both hashes і permitted downloads, exclusions, offline cache, local pins/provenance, staging/insert-only SQLite/recovery, UI. Owned ZIP content process passed; real third-party .mrpack check мережево заблокований.

**Розпочато, не перевірено:** Prism/MultiMC InstanceSource + shared eligibility refactor. **Далі:** exact metadata/UI/transactions і regression; Minecraft Launcher; Modrinth App; CurseForge App. Accounts/hooks/runtime не переносяться; sources read-only, source mutations invalidate preview.

**Ще немає:** license-aware export із manifest/exact files/configs, own PTeno format contract, friend comparison/sync, whole-pack lock/rollback/last verified configuration. Дозволені provider downloads замість незаконного перепакування; no Minecraft binaries/secrets. End-to-end ціль — передати й відновити підтримувану збірку на іншому Windows PC.

### 4.10. World / Resource Pack / Shader Managers

**World:** list/metadata/version warnings, backup/restore, import/export, rename/copy між профілями, safe delete; не відкривати original world у несумісній грі для тесту.

**Resource packs:** ZIP import/list/remove/compatibility/явна activation, інтеграція generated translations. **Shaders:** import/list/remove, потрібні native shader components і missing-dependency warnings. Наявні saves/backups/generated ZIP — лише база, не завершені менеджери.

### 4.11. Downloads, cache і storage

**Є:** trusted HTTPS, hash/size, atomic part commit, bounded parallel assets, progress/cancel, cache reuse/repair. **Ще потрібно:** загальна queue/pause/retry/resume, speed/bandwidth, consistent Downloads page, ownership-aware storage usage/cleanup/dedup library.

Не видаляти cache/runtime/operation backups, поки вони потрібні профілям/rollback. Не послаблювати guards заради звільнення місця. Resume має перевіряти server/range support і фінальний файл.

### 4.12. Security, diagnostics і performance

**Є:** path/archive/link/ZIP-bomb limits, static JAR metadata, hash baselines, logs/redaction, readiness findings, least privilege, no hidden telemetry. **Ще потрібно:** trusted provenance registry, дозволений fresh threat feed зі статусами, supported Defender scan adapter, свіжий dependency audit, local crash classification, process RAM/CPU/start metrics.

**Conflict Finder:** isolated profile copies без original worlds, dependency-aware delta debugging, consented trial launches, evidence vs hypotheses. Static blockers не є автоматичним пошуком конфлікту; запуск неперевірених модів не автоматизується мовчки. No antivirus bypass/exclusions або absolute-security/SSD erase claims.

### 4.13. Онлайн-ШІ й переклади

**Є:** Responses direct client, per-user protected key/model, AI toggle/default off/cancel, explicit selected-data consent/redaction, question/log input, modern outer-JAR en_us batches, placeholders, manual/incremental drafts, offline editor/generated ZIP.

**Власник уточнив:** connection card, clear chat, two real language selectors, «Мої переклади» library/table/search, auto pack metadata за exact MC version; technical controls у «Додатково». Повний acceptance у розділі 9.

**Ще потрібно:** реальні короткі owner-consented API checks/quality, wider legacy/nested/component language resources, source/target migration without data loss, verified game application та optional confirmed activation. Не вимагати API key для локальних drafts/editor/export; не включати developer key в дистрибутив; shared paid backend не входить до поточного scope. Немає доказу live API лише з показаної settings форми.

### 4.14. Server Manager

Потрібні Vanilla/Paper/Purpur/Fabric/Forge/NeoForge/Quilt окремо за реально підтримуваними версіями: versions/Java/RAM/downloads, explicit EULA, server.properties, whitelist/OP list, start/stop/restart, console, players/metrics, backups, server-mod filtering/dependencies і підготовка з профілю. Online-mode/security defaults зберігаються. Paper не універсальний native Forge/Fabric сервер. Наразі повного manager немає.

### 4.15. Share My World, Porthole та Aternos

Потрібні LAN/port detection/manual fallback, explicit firewall/router boundaries; встановлений Porthole, version/interface detection і лише підтверджений CLI/API/session/lobby/invite або ручний workflow. Не вигадувати private endpoints.

Aternos: server mod names/versions/dependencies, recommended software, copy/search/filter/progress checklist, TXT/CSV/JSON export, official panel/catalog links та чесний ping/status. Без website automation, обходу правил, exaroton чи інших paid-hosting integrations.

### 4.16. Плагіни самого лаунчера

Потрібні SDK/API/manifest/version compatibility, install/enable/disable/remove/update, declared minimal permissions, verified origin і документація. Недовірені DLL не вантажаться довільно in-process. Реальний process isolation і межі доступу мають бути перевірені; не заявляти sandbox лише за наявністю окремого процесу.

### 4.17. Auto Updater

**Є:** startup/manual eight-language offer, Update/Later/settings/progress, signed ECDSA metadata, numeric sequence/stable-preview/schema, exact parent/helper/process guards, Portable staged replacement/backup/restart/SQLite+window health, recovery; distinct guarded Inno adapter і packaging tools; 3/3 actual executable test scenarios.

**Потрібно для публічного використання:** production public key + protected signing/sequence/feed pipeline, правильні managed inventories і rollout для legacy packages, actual self-contained two-build delivery, isolated real Inno install/upgrade/uninstall/recovery. Registry access guard не обходити. Private signing key не в source/package; Authenticode — окрема вимога. Running game/server не зупиняється updater автоматично. Incompatible migrations/key rotation — окремі непідтримувані випадки до реалізації.

### 4.18. Installer / Portable / реліз

**Є історично:** self-contained Windows payload/Setup/Portable/Source/checksums, Inno recipe, CI packaging/source exclusions/CRC/startup. **Ще потрібно на поточному source:** fresh verified packages, clean-system install/upgrade/reinstall/uninstall, UA/EN wizard/shortcuts/Installed Apps, preserve data by default + explicit own-data cleanup, relocated installed-game Portable, без SDK, актуальні notices/docs/audit/signing.

No Stable до реальних P0 checks. Немає автоматичного дозволу публікувати новий package. Source mirror без .git не означає відсутність локальних змін і не дозволяє скидати їх.

### 4.19. DEFERRED — не втрачати й не реалізовувати зараз

Sinytra Connector із version-specific prerequisites (Forgified Fabric API/Launchpad лише коли потрібні); version-specific compatibility/cart/server integration; Kilt; Cross-Loader Manager і Fabric→Forge, Forge→Fabric, Forge↔NeoForge та інші bridges. П’ять ID залишаються в деталізованому реєстрі. Це відкладення на майбутні версії, не скасування й не універсальна сумісність.

<a id="sequence"></a>
## 5. Практична черговість від поточного стану

Це план залежностей, не оцінка календарних строків і не команда почати код у цьому документаційному запиті. Нові уточнення власника щодо AI UX мають пріоритет над старим автоматичним переходом від імпорту до export; P0 залишається release gate.

| Етап | Робота | Результат переходу далі | Потрібен Microsoft-вхід? |
|---|---|---|---|
| A0 | Зберегти/review unfinished Prism edits, відокремити baseline | Зрозумілий стан, жодного overwrite; перед code continuation — build/regression поточного дерева | Ні |
| A1 | AI key setup і clear assistant layout | Людина розуміє поле питання/Send/answer/connection states; fixtures/native review | Ні; live request — власний ключ і явна згода |
| A2 | Source→target, migration, saved translation library/table | Старі ручні правки збережені; реальний non-en_us import, offline browse/edit | Ні |
| A3 | Auto pack metadata/simple generation | Known/unknown MC versions оброблені чесно; no required numeric field | Для export ні; game activation окремо |
| B1 | Завершити Prism/MultiMC, потім Minecraft Launcher | Read-only safe source import, exact targets, actual UI/recovery/regression | Ні |
| B2 | Modrinth App/CurseForge App local imports | Підтримувані adapters без credentials і fake readiness | Ні; online provider access окремо |
| B3 | Export/license workflow, friend diff, pack lock/restore | Дозволений передаваний pack і repeatable restore; no fake last-working claim | Ні для файлового workflow |
| C1 | Native loader/provider gaps і real artifacts | Exact supported contracts, no Unknown→Ready, real components у дозволеній мережі | Для install ні, gameplay так |
| C2 | World/resource/shader/backups | Isolated-profile lifecycle, metadata/version warnings і rollback | Для файлів ні |
| C3 | Downloads/Java/storage managers | Full queue/runtime update/remove/owned cleanup | Ні |
| D1 | Security/diagnostics/performance/conflict infrastructure | Fresh audit, local analyzer, isolated test plans | Для core ні; trial gameplay так |
| D2 | Server Manager/Porthole/Aternos | Окремі підтверджені native workflows, EULA користувача, no invented integration | Розробка ні; реальні сесії окремо |
| D3 | Plugins SDK/manager/isolation | Перевірені permissions/version lifecycle | Ні |
| R1 | Production updater/Installed/self-contained QA | Trusted feed і підпис/послідовність, actual Windows lifecycle | Ні; ключ/сертифікат/середовище окремо |
| R2 | Licensed client acceptance для всіх підтримуваних loaders | Online/offline world/save/reopen/stop, real mods і translations | Так |
| R3 | Final localization/accessibility/security/docs/Stable gate | Немає невідомих критичних P0 gaps; конкретна release evidence | Частина перевірок так |

Не блокувати всі незалежні задачі через один external blocker. Не намагатися одночасно реалізувати всі підсистеми. Перед новим дорученням перевіряти актуальні files/checkpoints, а не повторювати цей snapshot без звірки.

<a id="acceptance"></a>
## 6. Критерії завершення та release gate

### 6.1. Для кожної вимоги

1. Є працюючий user flow, не лише class/button.
2. Визначено supported scope, errors/Unknown і залежності.
3. Є відповідні змістовні автоматичні перевірки; risky file changes перевірені ізольовано, cancellation/recovery не приховують неповний стан.
4. Необхідні real components/native/gameplay checks виконані або чесно лишилися NOT RUN/BLOCKED.
5. Дати, source/build і середовище конкретні; старі результати не переносяться на новий payload.
6. Дані збережено, docs/checkpoint актуальні, required translations наявні.

### 6.2. Шкала модулів

0% — немає завершеної реалізації; 25% — початок/архітектура; 50% — часткова працездатність; 75% — суттєва реалізація/автоматичні докази з інтеграційними прогалинами; 100% — завершено та перевірено у визначеному scope. Це не відсотки сукупного продукту; середнє арифметичне не рахується.

### 6.3. Stable має бути заблокований до

- Full official auth/entitlement/profile/refresh і real licensed client matrix (Vanilla/Fabric/Quilt та заявлені Forge/NeoForge versions), включно з offline після підготовки, modded test world/save/reopen/stop.
- Current-source Windows build/regression, real artifact preparation/repair, installer/upgrade/reinstall/uninstall/data preservation і Portable relocation без SDK.
- Fresh vulnerability/dependency review, безпечний release source/package exclusions/checksums/notices; підпис і його фактичний стан зазначені чесно.
- Trusted updater deployment перед обіцянкою one-button public updates; відсутність production trust не маскується fixture-підписом.
- Native localization/DPI/keyboard review критичних workflows і зрозумілий список remaining supported/unsupported features.

Paid live AI quality не вважати автоматично включеною до CI, ключ не вимагати для core. Servers/plugins/DEFERRED scope не оголошувати завершеним лише для релізної назви.

### 6.4. Відомі розбіжності обліку

- OFFLINE-06: запис Not Implemented застарів щодо bounded .mrpack/ZIP offline workflows. Нижче він збережений дослівно з аудиторським уточненням.
- SECURITY-07: historical audit у таблиці не є новим поточним audit.
- DISTRIBUTION-11 та деякі module evidence/next cells досі посилаються на старі test counts/checkpoints; authoritative source — newest dated checkpoint + actual result file.
- MODPACKS-03: initial code уже розпочато, але офіційний рядок Not Implemented справедливо не означає completed import. Current code unbuilt/untested.
- Рядок architecture у старому module summary ще каже «п’ять», language keys — «370»; поточний baseline має 8 projects і 384 keys за ZIP checkpoint. Counts не є readiness.

Не виправляти ці розбіжності шляхом тихого видалення історії. Зведення відокремлює recorded status від актуального scoped interpretation; первинні статуси не підвищено цим документом.

<a id="inventory"></a>
## 7. Повний реєстр — усі 189 вимог

Кожний ID наведений один раз як картка. Текст вимоги й 8 полів узяті з поточної traceability; аудиторські уточнення відокремлені. «Перевірено» стосується лише зазначеного scope/дати. Generic старі next/evidence не скасовують нові checkpoint примітки.

- [Архітектура, сховище та базові гарантії — 5](#group-foundation)
- [Інтерфейс, навігація та мови — 11](#group-ui)
- [Теми й оформлення — 8](#group-themes)
- [Профілі, видалення, backups і контент — 20](#group-profiles)
- [Необов’язковий ШІ й переклади — 11](#group-ai)
- [Minecraft, версії, assets і loaders — 15](#group-minecraft)
- [Microsoft / Minecraft авторизація — 10](#group-auth)
- [Java Runtime Manager — 8](#group-java)
- [Offline-first та готовність — 8](#group-offline)
- [Завантаження — 7](#group-downloads)
- [Моди, джерела, залежності та оновлення — 21](#group-mods)
- [Модпаки, імпорт, експорт і синхронізація — 8](#group-modpacks)
- [Безпека, діагностика й продуктивність — 16](#group-security)
- [Сервери, спільна гра та Aternos — 17](#group-multiplayer)
- [Плагіни, updater і підпис — 7](#group-extensions)
- [Windows-пакети, документація та release QA — 12](#group-distribution)
- [Відкладені compatibility bridges — 5](#group-compat)

<a id="group-foundation"></a>
### Архітектура, сховище та базові гарантії — 5 вимог

<a id="req-foundation-01"></a>
#### FOUNDATION-01 — C#/.NET 10/Avalonia solution, вісім проєктів (початкові шари збережено)

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Архітектура та сховище.
- **Реалізація / місце в коді:** PTenoLauncher.sln; Directory.Build.props.
- **Наявні перевірки:** StorageTests; DesktopTests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.
- **Уточнення цього плану:** У поточному дереві вісім .csproj: початкові шари збережені, додано UpdateHelper, executable E2E і UpdateTools. Це не дозвіл відтворювати стару п’ятипроєктну структуру. Поточні Prism edits не проходили нову збірку.

<a id="req-foundation-02"></a>
#### FOUNDATION-02 — SQLite settings/profiles/history, WAL і транзакції

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Архітектура та сховище.
- **Реалізація / місце в коді:** src/PTenoLauncher.Infrastructure/Storage/SqliteLauncherStore.cs.
- **Наявні перевірки:** StorageTests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-foundation-03"></a>
#### FOUNDATION-03 — Offline-first запуск без прихованих мережевих probe

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Архітектура та сховище.
- **Реалізація / місце в коді:** src/PTenoLauncher.Desktop/ViewModels/MainViewModel.cs; NetworkPolicy.cs.
- **Наявні перевірки:** VersionAndReadinessTests; native smoke.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.
- **Уточнення цього плану:** Offline-first не означає відсутності будь-яких opt-in online-функцій: updater перевіряє мережу лише за відповідними налаштуваннями, Offline Mode блокує запити. Нові функції мають зберігати цю гарантію.

<a id="req-foundation-04"></a>
#### FOUNDATION-04 — Async UI та скасування операцій

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Архітектура та сховище.
- **Реалізація / місце в коді:** MainViewModel.cs; MainViewModel.Game.cs; LiveProcessRunner.cs.
- **Наявні перевірки:** ProcessLifecycleTests; GameStopUiTests; existing DesktopTests/NetworkTests.
- **Що підтверджує запис:** 330-case suite passes; owned process/child cancellation verified.
- **Залишок / межі:** Actual game shutdown and all operation types not fully verified.
- **Наступна дія за записом:** Finish listed requirement and verify.

<a id="req-foundation-05"></a>
#### FOUNDATION-05 — Обробка помилок, локальні логи, приховування секретів

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Архітектура та сховище.
- **Реалізація / місце в коді:** Diagnostics/LauncherLog.cs.
- **Наявні перевірки:** SecurityAndModsTests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="group-ui"></a>
### Інтерфейс, навігація та мови — 11 вимог

<a id="req-ui-01"></a>
#### UI-01 — Графітова палітра, компактна навігація, typography

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Сучасний інтерфейс.
- **Реалізація / місце в коді:** Desktop/MainWindow.axaml; App.axaml; Themes/Graphite.axaml.
- **Наявні перевірки:** DesktopTests; native render.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-ui-02"></a>
#### UI-02 — Головна: активний профіль, іконка, версія/loader, кількість JAR

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Сучасний інтерфейс.
- **Реалізація / місце в коді:** MainViewModel.Foundation.cs; MainWindow.axaml.
- **Наявні перевірки:** DesktopTests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-ui-03"></a>
#### UI-03 — Головна: помітна кнопка Виконати вхід, auth status, Грати, налаштування, readiness

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Сучасний інтерфейс.
- **Реалізація / місце в коді:** MainWindow.axaml; MainViewModel.cs; MainViewModel.Foundation.cs.
- **Наявні перевірки:** DesktopTests.VisibleSignInButtonsUseTheApplicationClientAndTrackAccountAndLogout; native smoke.
- **Що підтверджує запис:** Header/Account buttons directly start official sign-in, hide for account nickname and return after logout; default public app ID avoids player setup.
- **Залишок / межі:** Real post-fix sign-in and game e2e still pending.
- **Наступна дія за записом:** User licensed game/authentication retest.

<a id="req-ui-04"></a>
#### UI-04 — Порожній стан і окремий діалог створення профілю

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Сучасний інтерфейс.
- **Реалізація / місце в коді:** Dialogs/ProfileEditor.axaml.
- **Наявні перевірки:** DesktopTests.ModalEditorLoadsRealXamlAndSavesThroughBindings.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-ui-05"></a>
#### UI-05 — Навігація: Головна/Профілі/Моди/Модпаки/Завантаження/Сервери/Налаштування

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Сучасний інтерфейс.
- **Реалізація / місце в коді:** MainWindow.axaml.
- **Наявні перевірки:** Actual .mrpack page/control workflow verified; Servers/Downloads remain partial.
- **Що підтверджує запис:** Subset implemented; see limitations.
- **Залишок / межі:** Unverified/unsupported paths remain.
- **Наступна дія за записом:** Finish listed requirement and verify.

<a id="req-ui-06"></a>
#### UI-06 — Responsive layout і Windows DPI scaling

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Код є; повна перевірка не завершена (`Implemented, Not Verified`).
- **Призначення:** Сучасний інтерфейс.
- **Реалізація / місце в коді:** MainWindow.axaml.
- **Наявні перевірки:** Native render; multiple DPI not tested.
- **Що підтверджує запис:** Code/config exists; end-to-end evidence pending.
- **Залишок / межі:** Unverified/unsupported paths remain.
- **Наступна дія за записом:** Finish listed requirement and verify.
- **Уточнення цього плану:** Останні скриншоти власника підтверджують незручні AI поля/прокручування. Нові UX-01–06 є планом; потрібні actual-control і native/DPI перевірки.

<a id="req-ui-07"></a>
#### UI-07 — Українська локалізація інтерфейсу

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Сучасний інтерфейс.
- **Реалізація / місце в коді:** Localization/TextCatalog.cs.
- **Наявні перевірки:** Main UI localized; some diagnostics remain bilingual.
- **Що підтверджує запис:** Subset implemented; see limitations.
- **Залишок / межі:** Unverified/unsupported paths remain.
- **Наступна дія за записом:** Finish listed requirement and verify.
- **Уточнення цього плану:** ZIP-блок звітує 384 matching keys у восьми мовах. Це не повна локалізація low-level diagnostics і не мовна/DPI перевірка всього продукту.

<a id="req-ui-08"></a>
#### UI-08 — Додаткова англійська локалізація

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Сучасний інтерфейс.
- **Реалізація / місце в коді:** Localization/TextCatalog.cs.
- **Наявні перевірки:** Primary labels switch live; legacy diagnostics remain Ukrainian.
- **Що підтверджує запис:** Subset implemented; see limitations.
- **Залишок / межі:** Unverified/unsupported paths remain.
- **Наступна дія за записом:** Finish listed requirement and verify.
- **Уточнення цього плану:** Англійська є однією з восьми мов; нові AI UX зміни ще не перекладені, бо ще не реалізовані.

<a id="req-ui-09"></a>
#### UI-09 — Ненав'язливі анімації та вимкнення анімацій

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Код є; повна перевірка не завершена (`Implemented, Not Verified`).
- **Призначення:** Сучасний інтерфейс.
- **Реалізація / місце в коді:** App.axaml; Themes/AppearanceManager.cs.
- **Наявні перевірки:** Theme/preferences tests; motion not visually measured.
- **Що підтверджує запис:** Code/config exists; end-to-end evidence pending.
- **Залишок / межі:** Unverified/unsupported paths remain.
- **Наступна дія за записом:** Finish listed requirement and verify.

<a id="req-ui-modsvis"></a>
#### UI-MODSVIS — Hide Mods for Vanilla/no profile and return Home on switch

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Profile-specific navigation.
- **Реалізація / місце в коді:** MainViewModel.Foundation.cs; MainWindow.axaml.
- **Наявні перевірки:** DesktopTests.ModsNavigationIsHiddenForVanillaAndReturnsHomeWhenSwitching.
- **Що підтверджує запис:** Vanilla hidden; all four modded loader kinds visible; programmatic navigation guarded.
- **Залишок / межі:** Does not remove any mod files on profile change.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-ui-firstlang"></a>
#### UI-FIRSTLANG — First normal startup language menu with eight native choices, preview and SQLite persistence

- **Походження:** додана й збережена вимога.
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Choose a language without finding Settings or signing in.
- **Реалізація / місце в коді:** Dialogs/LanguageSelectionDialog.axaml; MainViewModel.Onboarding.cs; App.axaml.cs.
- **Наявні перевірки:** LanguageOnboardingTests: 14 real-XAML/SQLite/offline/restart cases in Windows CI.
- **Що підтверджує запис:** CI 40827117958: 344 passed; saved preferences and synthetic worlds preserved.
- **Залишок / межі:** Verified automated functional scope; native interactive/DPI QA and overall localization remain partial.
- **Наступна дія за записом:** Retain regressions and complete native UI review.

<a id="group-themes"></a>
### Теми й оформлення — 8 вимог

<a id="req-themes-01"></a>
#### THEMES-01 — Graphite Dark за замовчуванням

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Теми і налаштування.
- **Реалізація / місце в коді:** Themes/AppearanceManager.cs.
- **Наявні перевірки:** Foundation020Tests; DesktopTests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-themes-02"></a>
#### THEMES-02 — Midnight Black

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Теми і налаштування.
- **Реалізація / місце в коді:** Themes/AppearanceManager.cs.
- **Наявні перевірки:** DesktopTests.ThemeSwitchesAtRuntimePersistsAndAiDefaultsOff.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-themes-03"></a>
#### THEMES-03 — Light

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Теми і налаштування.
- **Реалізація / місце в коді:** Themes/AppearanceManager.cs.
- **Наявні перевірки:** DesktopTests.ThemeSwitchesAtRuntimePersistsAndAiDefaultsOff.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-themes-04"></a>
#### THEMES-04 — System: стеження за системною темою

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Код є; повна перевірка не завершена (`Implemented, Not Verified`).
- **Призначення:** Теми і налаштування.
- **Реалізація / місце в коді:** Themes/AppearanceManager.cs.
- **Наявні перевірки:** OS preference transition not manually tested.
- **Що підтверджує запис:** Code/config exists; end-to-end evidence pending.
- **Залишок / межі:** Unverified/unsupported paths remain.
- **Наступна дія за записом:** Finish listed requirement and verify.

<a id="req-themes-05"></a>
#### THEMES-05 — Custom: Background/Sidebar/Surface/Accent/Text

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Теми і налаштування.
- **Реалізація / місце в коді:** Core/Preferences.cs; AppearanceManager.cs; MainViewModel.Foundation.cs.
- **Наявні перевірки:** Preferences round-trip; manual color QA pending.
- **Що підтверджує запис:** Subset implemented; see limitations.
- **Залишок / межі:** Unverified/unsupported paths remain.
- **Наступна дія за записом:** Finish listed requirement and verify.

<a id="req-themes-06"></a>
#### THEMES-06 — Custom: radius та animations

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Теми і налаштування.
- **Реалізація / місце в коді:** Core/Preferences.cs; AppearanceManager.cs.
- **Наявні перевірки:** Foundation020Tests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-themes-07"></a>
#### THEMES-07 — Live theme switching через ResourceDictionary/DynamicResource

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Теми і налаштування.
- **Реалізація / місце в коді:** Themes/Graphite.axaml; App.axaml; AppearanceManager.cs.
- **Наявні перевірки:** DesktopTests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-themes-08"></a>
#### THEMES-08 — Збереження theme/language у SQLite

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Теми і налаштування.
- **Реалізація / місце в коді:** Settings/PreferencesService.cs; MainViewModel.Foundation.cs.
- **Наявні перевірки:** Foundation020Tests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="group-profiles"></a>
### Профілі, видалення, backups і контент — 20 вимог

<a id="req-profiles-01"></a>
#### PROFILES-01 — Створення/редагування незалежних профілів

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Керування профілями та даними.
- **Реалізація / місце в коді:** Core/ProfileRules.cs; SqliteLauncherStore.cs; ProfileEditor.axaml.
- **Наявні перевірки:** StorageTests; DesktopTests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-profiles-02"></a>
#### PROFILES-02 — Індивідуальні RAM та Java settings

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Керування профілями та даними.
- **Реалізація / місце в коді:** Models.cs; ProfileRules.cs; ProfileEditor.axaml.
- **Наявні перевірки:** StorageTests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-profiles-03"></a>
#### PROFILES-03 — Архівування і повернення з архіву без втрати файлів

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Керування профілями та даними.
- **Реалізація / місце в коді:** MainViewModel.cs; MainViewModel.Foundation.cs.
- **Наявні перевірки:** StorageTests; compiled UI.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-profiles-04"></a>
#### PROFILES-04 — Permanent delete профілю зі збереженням backups

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Керування профілями та даними.
- **Реалізація / місце в коді:** Storage/ProfileDeletionService.cs.
- **Наявні перевірки:** Foundation020Tests.DeleteKeepsBackupsAndNeverTouchesOtherProfileSharedCacheOrJava.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-profiles-05"></a>
#### PROFILES-05 — Permanent delete із related backups і selected tracked ZIP

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Керування профілями та даними.
- **Реалізація / місце в коді:** ProfileDeletionService.cs; SqliteLauncherStore.Lifecycle.cs.
- **Наявні перевірки:** Core selection tested; export-selection UI pending.
- **Що підтверджує запис:** Subset implemented; see limitations.
- **Залишок / межі:** Unverified/unsupported paths remain.
- **Наступна дія за записом:** Finish listed requirement and verify.

<a id="req-profiles-06"></a>
#### PROFILES-06 — Deletion preview: файли, розмір, попередження про worlds

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Керування профілями та даними.
- **Реалізація / місце в коді:** ProfileDeletionService.cs; Dialogs/DeleteProfileDialog.axaml.
- **Наявні перевірки:** Foundation020Tests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-profiles-07"></a>
#### PROFILES-07 — Deletion exact-name confirmation

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Керування профілями та даними.
- **Реалізація / місце в коді:** ProfileDeletionService.cs; DeleteProfileDialog.axaml.cs.
- **Наявні перевірки:** Foundation020Tests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-profiles-08"></a>
#### PROFILES-08 — Блокування зайнятих профілів і світу/process lease

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Керування профілями та даними.
- **Реалізація / місце в коді:** Storage/ProfileUsage.cs; ActiveGameRecord.cs.
- **Наявні перевірки:** Foundation020Tests; Core020Tests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-profiles-09"></a>
#### PROFILES-09 — Перевірка належності всіх deletion paths

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Керування профілями та даними.
- **Реалізація / місце в коді:** ProfileDeletionService.cs; SafePath.cs.
- **Наявні перевірки:** Foundation020Tests.UnsafeArtifactCannotMakeDeletionRemoveSharedFiles.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-profiles-10"></a>
#### PROFILES-10 — Збереження shared cache та Java при profile delete

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Керування профілями та даними.
- **Реалізація / місце в коді:** ProfileDeletionService.cs.
- **Наявні перевірки:** Foundation020Tests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-profiles-11"></a>
#### PROFILES-11 — Deletion журнал, rollback/recovery після збою

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Керування профілями та даними.
- **Реалізація / місце в коді:** ProfileDeletionService.cs.
- **Наявні перевірки:** Foundation020Tests.InterruptedDeleteBeforeDatabaseCommitIsRecovered.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-profiles-12"></a>
#### PROFILES-12 — Не шукати чужі ZIP/не видаляти cloud/remote copies

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Керування профілями та даними.
- **Реалізація / місце в коді:** ProfileDeletionService.cs.
- **Наявні перевірки:** Owned-path tests; no cloud integration.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-profiles-13"></a>
#### PROFILES-13 — Backup snapshots, перевірка цілісності, restore-as-new

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Керування профілями та даними.
- **Реалізація / місце в коді:** Storage/BackupManager.cs.
- **Наявні перевірки:** BackupAndRulesTests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-profiles-14"></a>
#### PROFILES-14 — Вибір довільної backup-директорії

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Керування профілями та даними.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-profiles-15"></a>
#### PROFILES-15 — World Manager: список, rename, перенос між профілями, compatibility

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Керування профілями та даними.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.
- **Уточнення цього плану:** Backup saves не є World Manager; повний список/metadata/copy/import/export/version checks і safe delete залишаються.

<a id="req-profiles-16"></a>
#### PROFILES-16 — Resource Pack Manager

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Керування профілями та даними.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.
- **Уточнення цього плану:** Generated translation ZIP не є загальним Resource Pack Manager. Автоматичний pack format та бібліотека перекладів заплановані окремо.

<a id="req-profiles-17"></a>
#### PROFILES-17 — Shader Manager і required components

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Керування профілями та даними.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-profiles-18"></a>
#### PROFILES-18 — Shared Content Library та deduplication

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Керування профілями та даними.
- **Реалізація / місце в коді:** VerifiedDownloader.cs; VanillaInstaller.cs; LocalModManager.cs.
- **Наявні перевірки:** NetworkTests; live installation.
- **Що підтверджує запис:** Shared checksum download cache exists; full content-library UI/lifecycle is pending.
- **Залишок / межі:** No full Shared Content Library manager.
- **Наступна дія за записом:** Implement library ownership/deduplication management.

<a id="req-profiles-19"></a>
#### PROFILES-19 — Storage usage, unused files, safe cache cleanup

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Керування профілями та даними.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-profiles-20"></a>
#### PROFILES-20 — Профільні translations/metadata/history cleanup

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Керування профілями та даними.
- **Реалізація / місце в коді:** ProfileDeletionService.cs; SqliteLauncherStore.Lifecycle.cs; AI/TranslationWorkspace.cs.
- **Наявні перевірки:** Foundation020Tests and OnlineAiTests; reviewed profile-owned draft/pack paths.
- **Що підтверджує запис:** Translation subsystem exists; local drafts/generated packs are profile-owned and packs registered for deletion preview.
- **Залишок / межі:** Exhaustive translation-specific delete/restore scenarios not separately verified.
- **Наступна дія за записом:** Add targeted translation cleanup and restore checks.

<a id="group-ai"></a>
### Необов’язковий ШІ й переклади — 11 вимог

<a id="req-ai-01"></a>
#### AI-01 — Глобальний AI Toggle, default off, SQLite persistence

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Необов'язковий онлайн-ШІ.
- **Реалізація / місце в коді:** AI/AiPolicy.cs; Settings/PreferencesService.cs; MainViewModel.Foundation.cs.
- **Наявні перевірки:** Foundation020Tests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-ai-02"></a>
#### AI-02 — Disable AI: cancel operations і gate HTTP/provider calls

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Необов'язковий онлайн-ШІ.
- **Реалізація / місце в коді:** AI/AiPolicy.cs; AI/OpenAiClient.cs.
- **Наявні перевірки:** Foundation020Tests; OnlineAiTests cancellation/offline/consent.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-ai-03"></a>
#### AI-03 — Core/mods/dependencies/offline незалежні від AI

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Необов'язковий онлайн-ШІ.
- **Реалізація / місце в коді:** LocalModManager.cs; VersionConstraints.cs; NetworkPolicy.cs.
- **Наявні перевірки:** Core tests run with default AI off.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-ai-04"></a>
#### AI-04 — Не додавати local models/Ollama

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Необов'язковий онлайн-ШІ.
- **Реалізація / місце в коді:** Solution dependency/code audit.
- **Наявні перевірки:** No local model packages/processes.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-ai-05"></a>
#### AI-05 — Online AI Assistant and protected provider credentials

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Код є; повна перевірка не завершена (`Implemented, Not Verified`).
- **Призначення:** Optional online AI.
- **Реалізація / місце в коді:** AI/OpenAiClient.cs; MainViewModel.Ai.cs; SecretVault.cs.
- **Наявні перевірки:** OnlineAiTests; actual XAML/DPAPI test.
- **Що підтверджує запис:** Fixed endpoint, selected data/consent, protected key, no tools/retries; synthetic contracts pass.
- **Залишок / межі:** No live paid API/model-quality test.
- **Наступна дія за записом:** Owner key/model test using docs/ONLINE_AI.md.
- **Уточнення цього плану:** UI/API client/захищений локальний ключ є. За нинішньої архітектури кожен користувач налаштовує власний ключ. Новий зрозумілий connection/chat UX і live якість не перевірені. Скриншот налаштувань не доводить успішний paid API request.

<a id="req-ai-06"></a>
#### AI-06 — Official single-mod and bulk online translation

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Optional online AI.
- **Реалізація / місце в коді:** AI/TranslationWorkspace.cs; AI/OpenAiClient.cs; MainViewModel.Ai.cs.
- **Наявні перевірки:** OnlineAiTests selected namespace, batching and recovery.
- **Що підтверджує запис:** Modern outer-JAR en_us JSON; explicit selection/bulk scope; up to 32 strings/request.
- **Залишок / межі:** Legacy/nested/component formats and real API quality unverified.
- **Наступна дія за записом:** Live account validation and broader formats.
- **Уточнення цього плану:** Джерело поточного TranslationWorkspace жорстко en_us. Дві реальні source/target вибиралки, нова provenance і безпечна міграція старих drafts — нове уточнення власника, ще не реалізоване.

<a id="req-ai-07"></a>
#### AI-07 — Profile-isolated translation resource packs

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Код є; повна перевірка не завершена (`Implemented, Not Verified`).
- **Призначення:** Optional online AI.
- **Реалізація / місце в коді:** AI/TranslationWorkspace.cs; SqliteLauncherStore.Lifecycle.cs; ProfileDeletionService.cs.
- **Наявні перевірки:** OnlineAiTests ZIP contents, user-file refusal and deletion preview.
- **Що підтверджує запис:** Offline generated ZIP, guarded ownership, previous pack backup, missing/review counts.
- **Залишок / межі:** Explicit numeric pack_format required; game activation unverified.
- **Наступна дія за записом:** Test against licensed game and automate verified format selection.
- **Уточнення цього плану:** Поточний export потребує ручного numeric pack_format. Новий план прибирає його з обов’язкового сценарію, вимагає перевірених version-specific metadata та зрозумілого результату.

<a id="req-ai-08"></a>
#### AI-08 — Translation editor and manual corrections

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Optional online AI.
- **Реалізація / місце в коді:** MainViewModel.Ai.cs; MainWindow.axaml; AI/TranslationWorkspace.cs.
- **Наявні перевірки:** OnlineAiTests manual preservation and placeholder validation.
- **Що підтверджує запис:** JSON editor saves manual changes; changed sources require review.
- **Залишок / межі:** Grid/editor UX and broad manual workflow review pending.
- **Наступна дія за записом:** Improve editor UX and actual user verification.
- **Уточнення цього плану:** Зараз drafts можна завантажити й відкрити в JSON editor. Нова «Мої переклади» бібліотека, table editor, search і filters — PLANNED, а не вже готовий UI.

<a id="req-ai-09"></a>
#### AI-09 — Incremental translation after mod updates

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Optional online AI.
- **Реалізація / місце в коді:** AI/TranslationWorkspace.cs.
- **Наявні перевірки:** OnlineAiTests.IncrementalTranslationPreservesManualEditsAndExportsOfflineWithoutChangingJarOrWorlds.
- **Що підтверджує запис:** Reimport compares English source; only pending strings sent; manual edits retained with review flags.
- **Залишок / межі:** Bounded modern JSON scope; removed values recoverable from previous draft.
- **Наступна дія за записом:** Broader formats and real mod/API check.
- **Уточнення цього плану:** Incremental/manual preservation перевірені в існуючому en_us→target обсязі; після source/target migration ці гарантії треба перевірити знову.

<a id="req-ai-10"></a>
#### AI-10 — Saved translations usable offline with AI off

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Optional online AI.
- **Реалізація / місце в коді:** AI/TranslationWorkspace.cs; MainViewModel.Ai.cs.
- **Наявні перевірки:** OnlineAiTests offline editor/export and no extra HTTP.
- **Що підтверджує запис:** Local drafts/manual edits/export do not enter AI provider gate.
- **Залишок / межі:** Game application requires separately verified pack format and activation.
- **Наступна дія за записом:** Licensed in-game verification.
- **Уточнення цього плану:** Offline local drafts/manual edits/export працюють. Це не доказ, що нова бібліотека перекладів або застосування generated pack у грі вже перевірені.

<a id="req-ai-11"></a>
#### AI-11 — Consent-only AI analysis of selected redacted logs

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Код є; повна перевірка не завершена (`Implemented, Not Verified`).
- **Призначення:** Optional online AI.
- **Реалізація / місце в коді:** AI/OpenAiClient.cs; MainViewModel.Ai.cs.
- **Наявні перевірки:** OnlineAiTests.AssistantUsesFixedEndpointNoToolsNoStoreAndRedactedSelectedInput.
- **Що підтверджує запис:** Only pasted selected text is redacted/previewed and explicitly sent; no automatic log uploads.
- **Залишок / межі:** Best-effort redaction; no live diagnostic-quality evidence.
- **Наступна дія за записом:** Review preview and perform consented user test.

<a id="group-minecraft"></a>
### Minecraft, версії, assets і loaders — 15 вимог

<a id="req-minecraft-01"></a>
#### MINECRAFT-01 — Офіційний Version Manifest і cached Version Manager

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Minecraft Core.
- **Реалізація / місце в коді:** Minecraft/VersionManager.cs.
- **Наявні перевірки:** VersionAndReadinessTests; live-metadata log.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-minecraft-02"></a>
#### MINECRAFT-02 — Vanilla client/libraries/assets/native installation

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Minecraft Core.
- **Реалізація / місце в коді:** Minecraft/VanillaInstaller.cs.
- **Наявні перевірки:** 0.1 live installation 1.21.1; 3960 files.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-minecraft-03"></a>
#### MINECRAFT-03 — Modern launch arguments та classpath

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Minecraft Core.
- **Реалізація / місце в коді:** GameLauncher.cs; LaunchArgumentExpansion.cs; MinecraftRules.cs; LegacyArguments.cs.
- **Наявні перевірки:** HistoricalAssetTests controlled real argument process; full 424 passed / 2 explicit not run.
- **Що підтверджує запис:** Classpath/native/game paths, empty/quoted/literal arguments and version_type verified without authentication.
- **Залишок / межі:** Real Minecraft and licensed loader gameplay NOT RUN.
- **Наступна дія за записом:** Licensed Vanilla/Fabric/Quilt tests when official prerequisites are available.
- **Уточнення цього плану:** Historical argument expansion і assets підготовка реалізовані; справжній Minecraft session лишається непідтвердженим.

<a id="req-minecraft-04"></a>
#### MINECRAFT-04 — Historical minecraftArguments parser

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Minecraft Core.
- **Реалізація / місце в коді:** Minecraft/LegacyArguments.cs; VanillaInstaller.cs.
- **Наявні перевірки:** Core020Tests.LegacyArgumentsPreserveTokensAndQuotedValues.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-minecraft-05"></a>
#### MINECRAFT-05 — Historical full install/launch, virtual/map-to-resources assets

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Minecraft Core.
- **Реалізація / місце в коді:** AssetIndexPlan.cs; AssetManager.cs; VanillaInstaller.cs; ReceiptValidator.cs; OfflineReadiness.cs.
- **Наявні перевірки:** HistoricalAssetTests: 40 cases, 38 pass / 2 explicit; retained in current 463-case suite.
- **Що підтверджує запис:** Virtual/mapped preparation, cache hashes, ownership, offline repair and restart fixtures pass.
- **Залишок / межі:** Native install blocked by socket 10013; prior owner junction pass retained; current two-case repeat pending; gameplay NOT RUN.
- **Наступна дія за записом:** Run official historical component check in an allowed environment, then licensed historical gameplay.
- **Уточнення цього плану:** Загальне відхилення virtual/map_to_resources усунене раніше. Реальний official install був network-blocked; mapped-index fixtures не є gameplay.

<a id="req-minecraft-06"></a>
#### MINECRAFT-06 — Official logging configuration download

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Minecraft Core.
- **Реалізація / місце в коді:** Minecraft/VanillaInstaller.cs.
- **Наявні перевірки:** Real 1.21.1 installation within live-fabric.log.
- **Що підтверджує запис:** Official logging configuration downloaded/verified and retained in launch arguments.
- **Залишок / межі:** Historical version coverage remains limited.
- **Наступна дія за записом:** Add historical install cases.

<a id="req-minecraft-07"></a>
#### MINECRAFT-07 — Запуск процесу Minecraft

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Зовнішній блокер; реалізацію див. окремо (`Blocked by External Requirements`).
- **Призначення:** Minecraft Core.
- **Реалізація / місце в коді:** GameLauncher.cs.
- **Наявні перевірки:** Own permitted Client ID and official user login required.
- **Що підтверджує запис:** External requirement unresolved.
- **Залишок / межі:** Own approval/key/login/certificate or unimplemented dependency required.
- **Наступна дія за записом:** Resolve prerequisites; continue independent work.

<a id="req-minecraft-08"></a>
#### MINECRAFT-08 — Live Console, redacted bounded logs, exit codes

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Minecraft Core.
- **Реалізація / місце в коді:** Diagnostics/LiveProcessRunner.cs; GameLauncher.cs.
- **Наявні перевірки:** Core020Tests; ProcessLifecycleTests; real Java 21 probe.
- **Що підтверджує запис:** Redacted real stdout/stderr, exit 7, cancellation/cleanup; Java exits 0/1.
- **Залишок / межі:** Minecraft session not run; no hostile-descendant containment guarantee.
- **Наступна дія за записом:** Finish listed requirement and verify.

<a id="req-minecraft-09"></a>
#### MINECRAFT-09 — Діагностика запуску і launch history

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Minecraft Core.
- **Реалізація / місце в коді:** GameLauncher.cs; SqliteLauncherStore.cs.
- **Наявні перевірки:** Storage/rules tests; real game pending.
- **Що підтверджує запис:** Subset implemented; see limitations.
- **Залишок / межі:** Unverified/unsupported paths remain.
- **Наступна дія за записом:** Finish listed requirement and verify.

<a id="req-minecraft-10"></a>
#### MINECRAFT-10 — Fabric Loader version manager/install/launch

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Minecraft Core.
- **Реалізація / місце в коді:** Minecraft/FabricInstaller.cs; MainViewModel.Fabric.cs; ReceiptValidator.cs.
- **Наявні перевірки:** FabricInstallerTests; native-fabric-smoke.log; live-fabric.log.
- **Що підтверджує запис:** Catalog/pinning/verified inherited preparation and offline repair passed: 3969 files, 79 classpath entries.
- **Залишок / межі:** Actual licensed Fabric game launch NOT RUN; historical combinations not all supported.
- **Наступна дія за записом:** Manual licensed game test; compatibility/readiness coverage.

<a id="req-minecraft-11"></a>
#### MINECRAFT-11 — Forge Loader version manager/install/launch

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Minecraft Core.
- **Реалізація / місце в коді:** ForgeCatalog.cs; ForgeInstallerMetadata.cs; ForgeComponentPreparer.cs; ForgeProcessorPlan.cs; ForgeProcessorHost.cs; ForgeInstaller.cs; ReceiptValidator.cs; MainViewModel.Forge.cs.
- **Наявні перевірки:** ForgeComponentTests + ForgeExecutionTests (30 new); full 522 passed / 2 explicit not run.
- **Що підтверджує запис:** Strict spec-1 execution/explicit SHA-1 outputs, staged generated artifacts, managed Java host, durable ownership/recovery and previous receipt preservation; synthetic public installer/UI/readiness verified.
- **Залишок / межі:** Real catalogs failed with socket 10051 (prior 10013 retained); real official processors/gameplay NOT VERIFIED; unsupported profiles/tasks/outputs fail closed.
- **Наступна дія за записом:** Verify official exact versions in permitted environment; add explicit adapters for missing outputs/Mojang mappings; retain licensed gameplay gate.
- **Уточнення цього плану:** Forge має strict spec-1 execution/generated-output/transaction subset. Широка official installer сумісність, mappings/contracts та справжній запуск не підтверджені.

<a id="req-minecraft-12"></a>
#### MINECRAFT-12 — NeoForge Loader version manager/install/launch

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Minecraft Core.
- **Реалізація / місце в коді:** ForgeCatalog.cs; ForgeInstallerMetadata.cs; ForgeComponentPreparer.cs; ForgeProcessorPlan.cs; ForgeProcessorHost.cs; ForgeInstaller.cs; ReceiptValidator.cs; MainViewModel.Forge.cs.
- **Наявні перевірки:** ForgeComponentTests + ForgeExecutionTests (30 new); full 522 passed / 2 explicit not run.
- **Що підтверджує запис:** Strict spec-1 execution/explicit SHA-1 outputs, staged generated artifacts, managed Java host, durable ownership/recovery and previous receipt preservation; synthetic public installer/UI/readiness verified.
- **Залишок / межі:** Real catalogs failed with socket 10051 (prior 10013 retained); real official processors/gameplay NOT VERIFIED; unsupported profiles/tasks/outputs fail closed.
- **Наступна дія за записом:** Verify official exact versions in permitted environment; add explicit adapters for missing outputs/Mojang mappings; retain licensed gameplay gate.
- **Уточнення цього плану:** NeoForge обробляється окремо; не переносити Forge/FML identity за здогадкою. Live official execution і gameplay не підтверджені.

<a id="req-minecraft-13"></a>
#### MINECRAFT-13 — Quilt Loader version manager/install/launch

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Minecraft Core.
- **Реалізація / місце в коді:** Minecraft/QuiltInstaller.cs; MainViewModel.Quilt.cs; ReceiptValidator.cs.
- **Наявні перевірки:** QuiltInstallerTests (13 cases); native-quilt-smoke.log; live-quilt-offline.log; quilt-native-metadata.log; quilt-native-repair.log.
- **Що підтверджує запис:** Real 1.21.1/0.30.1 cached install/repair: 3972 files, 82 classpath; fresh C# catalog and actual loader redownload/hash verification passed.
- **Залишок / межі:** Full clean online exec test failed DNS; actual gameplay NOT RUN; root Quilt metadata implemented, nested/conditional cases incomplete.
- **Наступна дія за записом:** Licensed game test; complete nested/conditional Quilt metadata; full clean native-online installation.
- **Уточнення цього плану:** Bounded native Quilt conditional/nested selection уже додано окремим блоком; старе формулювання «complete nested» не означає повну відсутність реалізації. Unsupported rules/overrides/ambiguity залишаються Unknown.

<a id="req-minecraft-14"></a>
#### MINECRAFT-14 — Повноцінні JVM argument settings

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Minecraft Core.
- **Реалізація / місце в коді:** GameProfile; GameLauncher.cs.
- **Наявні перевірки:** RAM arguments implemented; user custom JVM editor pending.
- **Що підтверджує запис:** Subset implemented; see limitations.
- **Залишок / межі:** Unverified/unsupported paths remain.
- **Наступна дія за записом:** Finish listed requirement and verify.

<a id="req-minecraft-snap"></a>
#### MINECRAFT-SNAP — Latest releases/snapshots/historical filters і refresh-on-open

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Код є; повна перевірка не завершена (`Implemented, Not Verified`).
- **Призначення:** Офіційний актуальний каталог без зміни профілю.
- **Реалізація / місце в коді:** MainViewModel.Versions.cs; ProfileEditor.axaml; VersionManager.cs.
- **Наявні перевірки:** Snapshot020Tests and final 81/81 tests passed.
- **Що підтверджує запис:** Cache filters/settings/selection verified; online catalog refresh e2e not rerun.
- **Залишок / межі:** Refresh requires online access; individual snapshot launches NOT RUN.
- **Наступна дія за записом:** Live official catalog refresh and licensed snapshot launch test.

<a id="group-auth"></a>
### Microsoft / Minecraft авторизація — 10 вимог

<a id="req-auth-01"></a>
#### AUTH-01 — MSAL system-browser OAuth/PKCE public client

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Офіційна авторизація.
- **Реалізація / місце в коді:** Authentication/MicrosoftAuthentication.cs; LauncherMicrosoftApplication.cs.
- **Наявні перевірки:** User browser completion; AuthenticationHttpTests; DesktopTests.
- **Що підтверджує запис:** Own personal-account registration/localhost configured; browser step observed PASS; registered public default ID supplied.
- **Залишок / межі:** Full downstream sign-in/refresh e2e still pending.
- **Наступна дія за записом:** User retest without repeating registration.

<a id="req-auth-02"></a>
#### AUTH-02 — Xbox/XSTS/Minecraft Services and Java entitlement

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Official authentication.
- **Реалізація / місце в коді:** MicrosoftAuthentication.cs; AuthenticationServiceException.cs.
- **Наявні перевірки:** AuthenticationHttpTests; user post-fix HTTP 403 screenshot.
- **Що підтверджує запис:** Xbox request casing/header fixed; browser OAuth PASS; current payload reaches Minecraft Services HTTP 403.
- **Залишок / межі:** Exact endpoint/reason not known; application API approval is possible but unconfirmed; no full sign-in.
- **Наступна дія за записом:** Verify owned AppID access and retest official entitlement/profile chain.

<a id="req-auth-03"></a>
#### AUTH-03 — Minecraft profile name/UUID retrieval

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Зовнішній блокер; реалізацію див. окремо (`Blocked by External Requirements`).
- **Призначення:** Офіційна авторизація.
- **Реалізація / місце в коді:** MicrosoftAuthentication.cs.
- **Наявні перевірки:** Live authorized API test pending.
- **Що підтверджує запис:** External requirement unresolved.
- **Залишок / межі:** Own approval/key/login/certificate or unimplemented dependency required.
- **Наступна дія за записом:** Resolve prerequisites; continue independent work.

<a id="req-auth-04"></a>
#### AUTH-04 — Кілька DPAPI accounts, active selection і targeted logout

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Офіційна авторизація.
- **Реалізація / місце в коді:** MicrosoftAuthentication.Accounts.cs.
- **Наявні перевірки:** Core020Tests.MultiAccountSelectionAndTargetedLogoutPreserveOtherAccount.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-auth-05"></a>
#### AUTH-05 — Multi-account official sign-in/silent refresh e2e

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Зовнішній блокер; реалізацію див. окремо (`Blocked by External Requirements`).
- **Призначення:** Офіційна авторизація.
- **Реалізація / місце в коді:** MicrosoftAuthentication.cs; Accounts.cs.
- **Наявні перевірки:** Own approved Client ID/user login required.
- **Що підтверджує запис:** External requirement unresolved.
- **Залишок / межі:** Own approval/key/login/certificate or unimplemented dependency required.
- **Наступна дія за записом:** Resolve prerequisites; continue independent work.

<a id="req-auth-06"></a>
#### AUTH-06 — Skin retrieval, bounded official PNG cache

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Код є; повна перевірка не завершена (`Implemented, Not Verified`).
- **Призначення:** Офіційна авторизація.
- **Реалізація / місце в коді:** Authentication/SkinCache.cs.
- **Наявні перевірки:** Compiled; live licensed account/skin test pending.
- **Що підтверджує запис:** Code/config exists; end-to-end evidence pending.
- **Залишок / межі:** Unverified/unsupported paths remain.
- **Наступна дія за записом:** Finish listed requirement and verify.

<a id="req-auth-07"></a>
#### AUTH-07 — DPAPI CurrentUser token storage і logout-all

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Офіційна авторизація.
- **Реалізація / місце в коді:** Security/SecretVault.cs; Storage/AtomicFile.cs; MicrosoftAuthentication.Accounts.cs.
- **Наявні перевірки:** SecurityAndModsTests; Core020Tests; UI header/account test.
- **Що підтверджує запис:** Protected atomic storage retained; sync-over-async UI deadlock fixed and synthetic UI-context writes verified.
- **Залишок / межі:** No claim of real token refresh e2e; local test credentials only.
- **Наступна дія за записом:** Keep security regression coverage and perform licensed real sign-in.

<a id="req-auth-08"></a>
#### AUTH-08 — Попередньо авторизований offline launch

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Зовнішній блокер; реалізацію див. окремо (`Blocked by External Requirements`).
- **Призначення:** Офіційна авторизація.
- **Реалізація / місце в коді:** OfflineReadiness.cs; GameLauncher.cs.
- **Наявні перевірки:** Actual online/offline game test requires official login.
- **Що підтверджує запис:** External requirement unresolved.
- **Залишок / межі:** Own approval/key/login/certificate or unimplemented dependency required.
- **Наступна дія за записом:** Resolve prerequisites; continue independent work.

<a id="req-auth-09"></a>
#### AUTH-09 — Заборона чужих Client ID, password forms і auth bypass

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Офіційна авторизація.
- **Реалізація / місце в коді:** MicrosoftAuthentication.cs; UI.
- **Наявні перевірки:** Missing-ID tests; no username-only offline accounts.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-auth-10"></a>
#### AUTH-10 — MICROSOFT_AUTH_SETUP.md із офіційними інструкціями

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Офіційна авторизація.
- **Реалізація / місце в коді:** MICROSOFT_AUTH_SETUP.md.
- **Наявні перевірки:** See setup checklist; permissions external.
- **Що підтверджує запис:** Subset implemented; see limitations.
- **Залишок / межі:** Unverified/unsupported paths remain.
- **Наступна дія за записом:** Finish listed requirement and verify.

<a id="group-java"></a>
### Java Runtime Manager — 8 вимог

<a id="req-java-01"></a>
#### JAVA-01 — Пошук встановленої Java

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Автоматична Java.
- **Реалізація / місце в коді:** Minecraft/JavaManager.cs.
- **Наявні перевірки:** Live Java inspection; Core020Tests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-java-02"></a>
#### JAVA-02 — Визначення Java major із Minecraft metadata

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Автоматична Java.
- **Реалізація / місце в коді:** JavaRuntimeManager.cs.
- **Наявні перевірки:** LiveJavaMatrix; JavaRuntimeIntegrityTests; Windows CI 40833002559.
- **Що підтверджує запис:** Managed Java 8/17/21/25 installs/x64/version/offline reuse pass; Java 21 cached repair passes.
- **Залишок / межі:** Runtime verification only; repair fault injection performed on Java 21; game launch remains unverified.
- **Наступна дія за записом:** Retain CI matrix; continue runtime UI and real-game acceptance.

<a id="req-java-03"></a>
#### JAVA-03 — Автоматичний Temurin JRE x64 ZIP install

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Автоматична Java.
- **Реалізація / місце в коді:** JavaRuntimeManager.cs; TrustedSources.cs.
- **Наявні перевірки:** LiveJavaMatrix; JavaRuntimeIntegrityTests; Windows CI 40833002559.
- **Що підтверджує запис:** Managed Java 8/17/21/25 installs/x64/version/offline reuse pass; Java 21 cached repair passes.
- **Залишок / межі:** Runtime verification only; repair fault injection performed on Java 21; game launch remains unverified.
- **Наступна дія за записом:** Retain CI matrix; continue runtime UI and real-game acceptance.

<a id="req-java-04"></a>
#### JAVA-04 — SHA256, PE architecture, runtime file receipt

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Автоматична Java.
- **Реалізація / місце в коді:** JavaRuntimeManager.cs; ManagedJavaValidation.cs; TemurinPackage.cs; JavaManager.cs; OfflineReadiness.cs.
- **Наявні перевірки:** JavaRuntimeIntegrityTests: 42 new cases; full 386-test suite.
- **Що підтверджує запис:** Complete inventory/exact path/origin/architecture/corruption/no-execution regressions pass.
- **Залишок / межі:** Hash manifests are not antivirus or a trust boundary against local receipt replacement.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-java-05"></a>
#### JAVA-05 — Кеш Java metadata/archives, offline reuse/repair code

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Автоматична Java.
- **Реалізація / місце в коді:** JavaRuntimeManager.cs.
- **Наявні перевірки:** LiveJavaMatrix; JavaRuntimeIntegrityTests; Windows CI 40833002559.
- **Що підтверджує запис:** Managed Java 8/17/21/25 installs/x64/version/offline reuse pass; Java 21 cached repair passes.
- **Залишок / межі:** Runtime verification only; repair fault injection performed on Java 21; game launch remains unverified.
- **Наступна дія за записом:** Retain CI matrix; continue runtime UI and real-game acceptance.

<a id="req-java-06"></a>
#### JAVA-06 — Runtime corruption detection

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Автоматична Java.
- **Реалізація / місце в коді:** JavaRuntimeManager.cs; ManagedJavaValidation.cs; TemurinPackage.cs; JavaManager.cs; OfflineReadiness.cs.
- **Наявні перевірки:** JavaRuntimeIntegrityTests: 42 new cases; full 386-test suite.
- **Що підтверджує запис:** Complete inventory/exact path/origin/architecture/corruption/no-execution regressions pass.
- **Залишок / межі:** Hash manifests are not antivirus or a trust boundary against local receipt replacement.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-java-07"></a>
#### JAVA-07 — Java 8/17/25 actual installs

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Автоматична Java.
- **Реалізація / місце в коді:** JavaRuntimeManager.cs.
- **Наявні перевірки:** LiveJavaMatrix; JavaRuntimeIntegrityTests; Windows CI 40833002559.
- **Що підтверджує запис:** Managed Java 8/17/21/25 installs/x64/version/offline reuse pass; Java 21 cached repair passes.
- **Залишок / межі:** Runtime verification only; repair fault injection performed on Java 21; game launch remains unverified.
- **Наступна дія за записом:** Retain CI matrix; continue runtime UI and real-game acceptance.

<a id="req-java-08"></a>
#### JAVA-08 — Profile-specific runtime without replacing other profiles

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Автоматична Java.
- **Реалізація / місце в коді:** MainViewModel.Foundation.cs; GameProfile.
- **Наявні перевірки:** Storage tests; only selected profile updated.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="group-offline"></a>
### Offline-first та готовність — 8 вимог

<a id="req-offline-01"></a>
#### OFFLINE-01 — Offline local profiles/settings/mods/backups/diagnostics

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Offline readiness.
- **Реалізація / місце в коді:** NetworkPolicy.cs; storage/mod services.
- **Наявні перевірки:** Offline/local tests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-offline-02"></a>
#### OFFLINE-02 — Ready/NeedsPreparation/Error/Unknown distinction

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Offline readiness.
- **Реалізація / місце в коді:** Core/Models.cs; Preferences.cs; OfflineReadiness.cs.
- **Наявні перевірки:** VersionAndReadinessTests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-offline-03"></a>
#### OFFLINE-03 — Game/library/asset hashes і corruption repair

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Offline readiness.
- **Реалізація / місце в коді:** VerifiedDownloader.cs; OfflineReadiness.cs.
- **Наявні перевірки:** NetworkTests; live-repair-offline 0.1.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-offline-04"></a>
#### OFFLINE-04 — Java integrity та prior account check

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Offline readiness.
- **Реалізація / місце в коді:** OfflineReadiness.cs.
- **Наявні перевірки:** Java/account tests; actual game e2e blocked.
- **Що підтверджує запис:** Subset implemented; see limitations.
- **Залишок / межі:** Unverified/unsupported paths remain.
- **Наступна дія за записом:** Finish listed requirement and verify.

<a id="req-offline-05"></a>
#### OFFLINE-05 — Installed mod integrity and dependency readiness

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Offline readiness.
- **Реалізація / місце в коді:** InstalledModReadiness.cs; QuiltCompatibility.cs; ForgeCompatibility.cs; InstalledModInventory.cs; LocalModManager.Baseline.cs.
- **Наявні перевірки:** InstalledModReadinessTests; QuiltModMetadataTests; ForgeModMetadataTests; DesktopTests.
- **Що підтверджує запис:** Fabric and native Quilt/Forge/NeoForge root metadata integrated with read-only hashes/dependencies; existing baseline and recovery retained.
- **Залишок / межі:** NeoForge FML and unsupported nested/qualifier cases stay Unknown; actual gameplay pending.
- **Наступна дія за записом:** Complete loader/runtime metadata and licensed gameplay.

<a id="req-offline-06"></a>
#### OFFLINE-06 — Offline local modpack imports

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Offline readiness.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.
- **Уточнення цього плану:** Розбіжність у таблиці: запис Not Implemented застарів для підтримуваних локальних .mrpack/ZIP. MRPACK_IMPORT_CHECKPOINT і ZIP_IMPORT_CHECKPOINT та їхні тести підтверджують bounded offline workflows; інші формати ще не готові. Оцінка цього зведення: частково реалізовано; вихідний статус збережено для аудиту, не рахується як нове завершення.

<a id="req-offline-07"></a>
#### OFFLINE-07 — Offline local server components

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Offline readiness.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-offline-08"></a>
#### OFFLINE-08 — Offline готові translation packs

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Offline readiness.
- **Реалізація / місце в коді:** AI/TranslationWorkspace.cs; MainViewModel.Ai.cs.
- **Наявні перевірки:** OnlineAiTests.IncrementalTranslationPreservesManualEditsAndExportsOfflineWithoutChangingJarOrWorlds.
- **Що підтверджує запис:** Local drafts, manual edits and generated resource-pack ZIP export work offline with AI off and no HTTP.
- **Залишок / межі:** Explicit numeric pack_format; actual in-game activation NOT RUN.
- **Наступна дія за записом:** Broader language formats, verified pack format and licensed in-game test.

<a id="group-downloads"></a>
### Завантаження — 7 вимог

<a id="req-downloads-01"></a>
#### DOWNLOADS-01 — HTTPS source policies, hash/size, atomic .part commit

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Завантаження.
- **Реалізація / місце в коді:** Network/VerifiedDownloader.cs; TrustedSources.cs.
- **Наявні перевірки:** NetworkTests; live Java.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-downloads-02"></a>
#### DOWNLOADS-02 — Bounded parallel asset downloads, progress, cancellation

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Завантаження.
- **Реалізація / місце в коді:** VanillaInstaller.cs; VerifiedDownloader.cs.
- **Наявні перевірки:** NetworkTests; actual 3960-file install.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-downloads-03"></a>
#### DOWNLOADS-03 — Controlled Temurin redirects без credentials forwarding

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Завантаження.
- **Реалізація / місце в коді:** VerifiedDownloader.cs; TrustedSources.cs.
- **Наявні перевірки:** Live Java download.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-downloads-04"></a>
#### DOWNLOADS-04 — Загальна черга / pause / retry / resume

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Завантаження.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-downloads-05"></a>
#### DOWNLOADS-05 — Speed display і bandwidth limit

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Завантаження.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-downloads-06"></a>
#### DOWNLOADS-06 — Після втрати мережі: повторне використання verified cache

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Завантаження.
- **Реалізація / місце в коді:** VerifiedDownloader.cs.
- **Наявні перевірки:** NetworkTests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-downloads-07"></a>
#### DOWNLOADS-07 — Повна Downloads Manager сторінка

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Завантаження.
- **Реалізація / місце в коді:** MainWindow.axaml; MainViewModel.cs.
- **Наявні перевірки:** Current operation/progress only.
- **Що підтверджує запис:** Subset implemented; see limitations.
- **Залишок / межі:** Unverified/unsupported paths remain.
- **Наступна дія за записом:** Finish listed requirement and verify.

<a id="group-mods"></a>
### Моди, джерела, залежності та оновлення — 21 вимог

<a id="req-mods-01"></a>
#### MODS-01 — Local JAR multi-select і Drag & Drop

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Моди і детерміновані залежності.
- **Реалізація / місце в коді:** MainWindow.axaml.cs; LocalModManager.cs.
- **Наявні перевірки:** Import tested; drag events compiled, manual DnD pending.
- **Що підтверджує запис:** Subset implemented; see limitations.
- **Залишок / межі:** Unverified/unsupported paths remain.
- **Наступна дія за записом:** Finish listed requirement and verify.

<a id="req-mods-02"></a>
#### MODS-02 — JAR static metadata/structure/SHA256 without execution

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Static local mod inspection.
- **Реалізація / місце в коді:** LocalModManager.cs; LocalModManager.Embedded.cs; LocalModManager.Quilt.cs; LocalModManager.Forge.cs; LocalModManager.NativeNested.cs; SafeArchive.cs.
- **Наявні перевірки:** SecurityAndModsTests; EmbeddedFabricTests; QuiltModMetadataTests; ForgeModMetadataTests; NativeNestedModTests.
- **Що підтверджує запис:** Hash/static archive reading and bounded Fabric/native Quilt/Jar-in-Jar metadata verified without execution; unsafe inputs rejected and unsupported metadata explicit.
- **Залишок / межі:** Only documented static format subsets; no antivirus or gameplay guarantee; real native artifact checks network-blocked.
- **Наступна дія за записом:** Keep regression/bounds coverage and verify representative real native artifacts.

<a id="req-mods-03"></a>
#### MODS-03 — Fabric required/optional/incompatible constraints

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Моди і детерміновані залежності.
- **Реалізація / місце в коді:** LocalModManager.cs; LocalModManager.Embedded.cs; FabricVersionConstraints.cs; VersionConstraints.cs.
- **Наявні перевірки:** ModResolver020Tests; EmbeddedFabricTests; FabricInstallerTests; FabricVersionConstraintTests; InstalledModReadinessTests.
- **Що підтверджує запис:** Fabric root/nested constraints and installed loader version are available to deterministic checks.
- **Залишок / межі:** Unsupported expressions remain explicitly Unknown.
- **Наступна дія за записом:** Extend other-loader metadata.

<a id="req-mods-04"></a>
#### MODS-04 — Loader-specific constraints, numeric Maven ranges and supported qualifiers

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Deterministic compatibility.
- **Реалізація / місце в коді:** VersionConstraints.cs; FabricVersionConstraints.cs; QuiltMetadata.cs; QuiltSemanticOrder.cs; MavenVersionConstraints.cs.
- **Наявні перевірки:** MavenQualifierTests; QuiltModMetadataTests; NativeNestedModTests; full 574 passed / 2 explicit not run.
- **Що підтверджує запис:** Supported Maven qualifiers/ranges and separate native Quilt semantic ordering/range grammar verified; leading-zero prerelease tokens follow Quilt source.
- **Залишок / межі:** Noncanonical/unsupported/generic forms remain Unknown; no universal version-parser claim.
- **Наступна дія за записом:** Extend only with authoritative parser/selector fixtures.

<a id="req-mods-05"></a>
#### MODS-05 — Unsupported ranges залишаються Unknown

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Моди і детерміновані залежності.
- **Реалізація / місце в коді:** VersionConstraints.cs; LocalModManager.cs.
- **Наявні перевірки:** ModResolver020Tests; NativeNestedModTests; full 574 passed / 2 explicit not run.
- **Що підтверджує запис:** Unsupported native selection/ranges/aliases/overrides and exceeded limits stay Unknown with explicit reasons.
- **Залишок / межі:** Static Unknown is not proof of runtime compatibility.
- **Наступна дія за записом:** Retain fail-closed unknown handling and extend documented contracts.

<a id="req-mods-06"></a>
#### MODS-06 — Quilt root and nested dependency metadata

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Deterministic Quilt mod compatibility.
- **Реалізація / місце в коді:** QuiltMetadata.cs; QuiltSemanticOrder.cs; LocalModManager.Quilt.cs; LocalModManager.NativeNested.cs; NativeNestedResolver.Quilt.cs; InstalledModReadiness.cs.
- **Наявні перевірки:** QuiltModMetadataTests; NativeNestedModTests (52 new across native subset); full 574 passed / 2 explicit not run.
- **Що підтверджує запис:** Bounded native candidate discovery/weighted conditional selection, identity/provides/groups/environment/optional/unless/conflicts and profile overrides guard; offline combined cart/readiness verified.
- **Залишок / межі:** Real Modrinth artifact requests blocked 10051; custom plugins/rules/breaks-unless/complex alias/generic ordering remain Unknown; no gameplay.
- **Наступна дія за записом:** Verify real artifacts/default loader integration; extend only with authoritative version-specific contracts.

<a id="req-mods-07"></a>
#### MODS-07 — Forge/NeoForge TOML metadata and dependency ranges

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Native mod compatibility.
- **Реалізація / місце в коді:** ForgeMetadata.cs; MavenVersionConstraints.cs; LocalModManager.Forge.cs; LocalModManager.NativeNested.cs; NativeNestedResolver.cs; ForgeCompatibility.cs.
- **Наявні перевірки:** ForgeModMetadataTests; NativeNestedModTests (52 new across native subset); full 574 passed / 2 explicit not run.
- **Що підтверджує запис:** Bounded recursive Jar-in-Jar descriptors, separate Maven/Mod ID/module identities, restrictive-range selection/root precedence and independent FML checks; offline cart/readiness verified.
- **Залишок / межі:** Multiple eligible alternatives/version-dependent selector behavior, obfuscation/unknown module identity remain Unknown; real artifact requests blocked 10051; gameplay unverified.
- **Наступна дія за записом:** Real artifact checks and version-specific selector adapters; MODS-19 update workflow next.

<a id="req-mods-08"></a>
#### MODS-08 — Recursive provider-backed dependency resolution без AI

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Моди і детерміновані залежності.
- **Реалізація / місце в коді:** Mods/RemoteDependencyResolver.cs; RemoteModInstaller.LocalFiles.cs; ModCatalog.cs.
- **Наявні перевірки:** RemoteDependencyTests; LocalDependencyTests; live-modrinth.log.
- **Що підтверджує запис:** Exact hash-known local JARs and catalog roots share deterministic transitive resolution without AI; secondary published files stay pinned.
- **Залишок / межі:** Unidentified-JAR auto-discovery and other providers/loaders incomplete.
- **Наступна дія за записом:** Optional selection; permitted CurseForge and native loader metadata.

<a id="req-mods-09"></a>
#### MODS-09 — Автоматичне додавання missing required dependencies у cart

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Моди і детерміновані залежності.
- **Реалізація / місце в коді:** RemoteDependencyResolver.cs; RemoteModInstaller.LocalFiles.cs; MainViewModel.Modrinth.cs.
- **Наявні перевірки:** LocalDependencyTests; ModrinthIntegrationTests; real local-JAR cached test.
- **Що підтверджує запис:** Known local Modrinth file identities seed required dependencies into the combined confirmed cart with exact pins.
- **Залишок / межі:** Unknown identities are not guessed from Mod IDs; final actual-JAR review needed after downloads.
- **Наступна дія за записом:** Improve typed pre-download findings and unidentified-JAR discovery.

<a id="req-mods-10"></a>
#### MODS-10 — Installed-mod duplicate IDs/hashes/loader checks

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Моди і детерміновані залежності.
- **Реалізація / місце в коді:** LocalModManager.cs; InstalledModReadiness.cs.
- **Наявні перевірки:** SecurityAndModsTests; ModResolver020Tests; InstalledModReadinessTests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-mods-11"></a>
#### MODS-11 — Optional dependencies окремими попередженнями та явним вибором

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Моди і детерміновані залежності.
- **Реалізація / місце в коді:** RemoteDependencyResolver.Optional.cs; MainViewModel.Modrinth.cs; OptionalDependencyChoice.cs; MainWindow.axaml.
- **Наявні перевірки:** OptionalDependencyTests; LocalDependencyTests; DesktopTests; live-optional-offline.log.
- **Що підтверджує запис:** Modrinth opt-in checkboxes, exact parent pins, transitive required edges, conflicts, orphan pruning and cache handling verified.
- **Залишок / межі:** Other provider/loader metadata and actual optional-mod gameplay pending.
- **Наступна дія за записом:** Extend supported providers and verify licensed gameplay.

<a id="req-mods-12"></a>
#### MODS-12 — Cart preview перед установленням

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Моди і детерміновані залежності.
- **Реалізація / місце в коді:** MainViewModel.Modrinth.cs; MainWindow.axaml; RemoteModInstaller.LocalFiles.cs.
- **Наявні перевірки:** LocalDependencyTests; DesktopTests.UnifiedLocalCartRequiresConfirmationAndInvalidatesAfterEditsOffline.
- **Що підтверджує запис:** One local/catalog cart, explicit confirmation and stale-plan refusal work; unknown files need an extra internal-warning acknowledgment.
- **Залишок / межі:** Static review does not prove in-game compatibility.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-mods-13"></a>
#### MODS-13 — Cart source/reason/version/size/compatibility columns

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Моди і детерміновані залежності.
- **Реалізація / місце в коді:** Core/ModCatalog.cs; MainViewModel.Modrinth.cs; MainWindow.axaml.
- **Наявні перевірки:** LocalDependencyTests; DesktopTests; native Mods render.
- **Що підтверджує запис:** Combined local/catalog cart shows identity/source, version/MC/loader, required reasons, real download sizes and preliminary/final-review warnings.
- **Залишок / межі:** Definite local compatibility failures now block before download; per-row diagnostics and other-provider optional handling remain incomplete.
- **Наступна дія за записом:** Improve typed diagnostics and optional selection.

<a id="req-mods-14"></a>
#### MODS-14 — Durable local mod-install journal і crash recovery

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Моди і детерміновані залежності.
- **Реалізація / місце в коді:** Mods/LocalModManager.Journal.cs; InstalledModInventory.cs.
- **Наявні перевірки:** ModResolver020Tests; InstalledModReadinessTests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-mods-15"></a>
#### MODS-15 — Не видаляти змінений користувачем target при recovery

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Моди і детерміновані залежності.
- **Реалізація / місце в коді:** LocalModManager.Journal.cs.
- **Наявні перевірки:** RecoveryNeverDeletesAChangedTarget.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-mods-16"></a>
#### MODS-16 — Offline dependency metadata/cache

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Моди і детерміновані залежності.
- **Реалізація / місце в коді:** ModrinthClient.cs; LocalModManager.cs; LocalModManager.Embedded.cs; RemoteModInstaller.LocalFiles.cs.
- **Наявні перевірки:** LocalDependencyTests; ModrinthIntegrationTests; live-local-deps-offline.log.
- **Що підтверджує запис:** Local Fabric metadata and verified Modrinth metadata/files cache work offline; unknown cold-cache files with complete local metadata can install with explicit confirmation; unresolved mandatory dependencies block without HTTP.
- **Залишок / межі:** Other provider/loader caches pending.
- **Наступна дія за записом:** Extend with supported metadata providers.

<a id="req-mods-17"></a>
#### MODS-17 — Modrinth search/version/download/provider integration

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Моди і детерміновані залежності.
- **Реалізація / місце в коді:** Mods/ModrinthClient.cs; RemoteModInstaller.cs; Desktop/ViewModels/MainViewModel.Modrinth.cs.
- **Наявні перевірки:** ModrinthIntegrationTests; artifacts/0.2.0/live-modrinth.log.
- **Що підтверджує запис:** Real filtered search, compatible versions, recursive selection, SHA512 download, identity lookup and offline installation passed.
- **Залишок / межі:** Mod loader installation and real game launch are separate and pending.
- **Наступна дія за записом:** Loader installation and further mod-set compatibility tests.

<a id="req-mods-18"></a>
#### MODS-18 — CurseForge API provider

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Зовнішній блокер; реалізацію див. окремо (`Blocked by External Requirements`).
- **Призначення:** Моди і детерміновані залежності.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Permitted API key/access not provided; provider not implemented.
- **Що підтверджує запис:** External requirement unresolved.
- **Залишок / межі:** Own approval/key/login/certificate or unimplemented dependency required.
- **Наступна дія за записом:** Resolve prerequisites; continue independent work.

<a id="req-mods-19"></a>
#### MODS-19 — Automatic updates і version locking mods

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Моди і детерміновані залежності.
- **Реалізація / місце в коді:** ModUpdateManager*.cs; ModUpdateModels.cs; MainViewModel.ModUpdates.cs; MainWindow.axaml.
- **Наявні перевірки:** ModUpdateTests (40); ModUpdateUiTests (2); full 616 passed / 2 explicit not run.
- **Що підтверджує запис:** Hash identity, actual-JAR/native preview, exact persistent pins, verified backup/journal, confirmed apply/rollback/offline recovery and UI verified in isolated fixtures.
- **Залишок / межі:** Real provider check FAILED socket 10051; real gameplay and power-loss/manual UX unverified; Modrinth/bounded static subset.
- **Наступна дія за записом:** Verify live provider updates and authorized gameplay; next EXTENSIONS-05 before modpacks.

<a id="req-mods-20"></a>
#### MODS-20 — Compatibility Engine for full multi-loader modpacks

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Моди і детерміновані залежності.
- **Реалізація / місце в коді:** VersionConstraints.cs; LocalModManager.cs.
- **Наявні перевірки:** Local Fabric subset only.
- **Що підтверджує запис:** Subset implemented; see limitations.
- **Залишок / межі:** Unverified/unsupported paths remain.
- **Наступна дія за записом:** Finish listed requirement and verify.

<a id="req-mods-folder"></a>
#### MODS-FOLDER — Перейти до теки mods вибраного профілю

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Код є; повна перевірка не завершена (`Implemented, Not Verified`).
- **Призначення:** Навігація локальних модів.
- **Реалізація / місце в коді:** MainViewModel.ModFolders.cs; MainWindow.axaml.
- **Наявні перевірки:** DesktopTests.ModsFolderButtonTargetsOnlyTheSelectedModdedProfile.
- **Що підтверджує запис:** Button wiring, owned path and profile/Vanilla guards tested.
- **Залишок / межі:** Interactive Explorer opening NOT RUN.
- **Наступна дія за записом:** Complete HOME_TEST_CHECKLIST.md folder case.

<a id="group-modpacks"></a>
### Модпаки, імпорт, експорт і синхронізація — 8 вимог

<a id="req-modpacks-01"></a>
#### MODPACKS-01 — .mrpack import

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Modpacks.
- **Реалізація / місце в коді:** Modpacks/Mrpack*.cs; SqliteLauncherStore.Imports.cs; MainViewModel.Modpacks.cs; MainWindow.axaml; TextCatalog.Mrpack.cs.
- **Наявні перевірки:** MrpackTests (48); MrpackUiTests (3); current regression 785 passed / 2 explicit not run; original native evidence retained.
- **Що підтверджує запис:** Supported client preview/consent/download/hash/cache/override/new-profile/transaction/recovery/exact-target/static-JAR/local-pin/UI workflow verified in isolated fixtures.
- **Залишок / межі:** Live official catalog FAILED socket 10051 before archive; ZIP64/multi-disk/encrypted/unknown origins/combinations and limits blocked; actual loader/gameplay unverified.
- **Наступна дія за записом:** Verify a real pack in allowed network; preserve existing local workflow; source-launcher adapters next.

<a id="req-modpacks-02"></a>
#### MODPACKS-02 — Compatible ZIP modpack imports

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Local client content import.
- **Реалізація / місце в коді:** Modpacks/ZipPackReader.cs; MrpackImporter.cs; MrpackRequirements.cs; SqliteLauncherStore.Imports.cs; MainViewModel.Modpacks.cs; TextCatalog.Zip.cs.
- **Наявні перевірки:** ZipPackTests (45); ZipPackUiTests (3); full 787 total / 785 passed / 2 explicit not run; local process + native startup.
- **Що підтверджує запис:** Supported root/wrapper/explicit-target/preview/exclusions/offline/transaction/recovery/UI subset verified; owned local ZIP imported into independent profile.
- **Залишок / межі:** Not universal ZIP; foreign manifests, ZIP64/encrypted/deep/oversize layouts blocked; unknown metadata remains Unknown; no third-party pack/gameplay or manual picker/DPI proof.
- **Наступна дія за записом:** Source-launcher adapters MODPACKS-03/04 and license-aware export remain separate.

<a id="req-modpacks-03"></a>
#### MODPACKS-03 — Prism/MultiMC/Minecraft Launcher profile imports

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Модпаки і сумісність збірок.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.
- **Уточнення цього плану:** Prism/MultiMC: початкова InstanceSource і shared eligibility refactor вже є, але parsing/integration/UI не завершені, збірка й тести НЕ ВИКОНАНІ. Minecraft Launcher adapter не реалізовано. Не заявляти завершений імпорт.

<a id="req-modpacks-04"></a>
#### MODPACKS-04 — Modrinth App/CurseForge App profile imports

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Модпаки і сумісність збірок.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.
- **Уточнення цього плану:** Local Modrinth App/CurseForge App source adapters — окремо від online CurseForge API provider; завершеної реалізації не підтверджено.

<a id="req-modpacks-05"></a>
#### MODPACKS-05 — Shareable modpack ZIP export із redistribution licenses

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Модпаки і сумісність збірок.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-modpacks-06"></a>
#### MODPACKS-06 — Зіставлення/синхронізація збірок друзів

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Модпаки і сумісність збірок.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-modpacks-07"></a>
#### MODPACKS-07 — Version locking/last working configuration

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Модпаки і сумісність збірок.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.
- **Уточнення цього плану:** Per-mod persistent pins у MODS-19 не дорівнюють modpack-wide last-known-working game configuration. Потрібні whole-set manifest/restore і окремий доказ gameplay.

<a id="req-modpacks-08"></a>
#### MODPACKS-08 — Backup ZIP import/restore

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Модпаки і сумісність збірок.
- **Реалізація / місце в коді:** BackupManager.cs.
- **Наявні перевірки:** BackupAndRulesTests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="group-security"></a>
### Безпека, діагностика й продуктивність — 16 вимог

<a id="req-security-01"></a>
#### SECURITY-01 — Least privilege/current Windows user/asInvoker

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Діагностика, приватність та безпека.
- **Реалізація / місце в коді:** Desktop/app.manifest; installer/PTenoLauncher.iss.
- **Наявні перевірки:** Native smoke; manifest inspection.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-security-02"></a>
#### SECURITY-02 — No hidden PTenoLauncher telemetry / AI transmissions

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Діагностика, приватність та безпека.
- **Реалізація / місце в коді:** AI/AiPolicy.cs; project refs; build scripts.
- **Наявні перевірки:** AI gating tests; BuildServices excluded.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-security-03"></a>
#### SECURITY-03 — Безпечне зберігання API secrets

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Діагностика, приватність та безпека.
- **Реалізація / місце в коді:** SecretVault.cs; MainViewModel.Ai.cs; MainWindow.axaml.
- **Наявні перевірки:** OnlineAiTests.ActualAiTabIsHiddenByDefaultAndKeyIsProtectedLocallyWithoutNetwork.
- **Що підтверджує запис:** OpenAI key UI exists: masked input, DPAPI CurrentUser save/delete, local synthetic tests pass.
- **Залишок / межі:** CurseForge credentials UI/provider and paid live OpenAI use are not verified.
- **Наступна дія за записом:** Permitted CurseForge credentials flow and optional owner-authorized provider test.

<a id="req-security-04"></a>
#### SECURITY-04 — Path Traversal/ADS/device-name/reparse guards

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Діагностика, приватність та безпека.
- **Реалізація / місце в коді:** Storage/SafePath.cs; Minecraft/AssetIndexPlan.cs.
- **Наявні перевірки:** StorageTests and asset path/collision cases pass; prior owner junction 1/1 recorded.
- **Що підтверджує запис:** Named ordinary path guards verified; entry attributes now inspect dangling reparse points.
- **Залишок / межі:** Current explicit existing/dangling junction pair awaits separate owner rerun; no absolute filesystem-security guarantee.
- **Наступна дія за записом:** Record the two-case ordinary Windows result without bypassing Codex restrictions.

<a id="req-security-05"></a>
#### SECURITY-05 — Zip Slip/Zip Bomb/entry-size/symlink guards

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Діагностика, приватність та безпека.
- **Реалізація / місце в коді:** Security/SafeArchive.cs.
- **Наявні перевірки:** SecurityAndModsTests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-security-06"></a>
#### SECURITY-06 — Atomic configs, SQLite transactions, recovery

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Діагностика, приватність та безпека.
- **Реалізація / місце в коді:** AtomicFile.cs; SqliteLauncherStore.cs; journal services.
- **Наявні перевірки:** StorageTests; recovery tests.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-security-07"></a>
#### SECURITY-07 — Dependency vulnerability audit

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Діагностика, приватність та безпека.
- **Реалізація / місце в коді:** artifacts/dependency-audit.json.
- **Наявні перевірки:** 0.1 audit; 0.2 refresh at release.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.
- **Уточнення цього плану:** Застарілий dependency audit не є поточним чистим аудитом. Останній новий audit не виконувався через недоступний сервіс; committed NuGetAudit=true збережено. Потрібна нова перевірка актуальних залежностей.

<a id="req-security-08"></a>
#### SECURITY-08 — Mod security source provenance і trusted hashes

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Діагностика, приватність та безпека.
- **Реалізація / місце в коді:** LocalModManager.cs; InstalledModInventory.cs; ModBaselineDialog.axaml.
- **Наявні перевірки:** Persistent SHA256/size baselines and explicit review verified; no publisher provenance registry.
- **Що підтверджує запис:** Subset implemented; see limitations.
- **Залишок / межі:** Unverified/unsupported paths remain.
- **Наступна дія за записом:** Finish listed requirement and verify.

<a id="req-security-09"></a>
#### SECURITY-09 — Known threat feed, freshness date, scanner statuses

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Діагностика, приватність та безпека.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-security-10"></a>
#### SECURITY-10 — Windows Defender supported scan adapter

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Діагностика, приватність та безпека.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-security-11"></a>
#### SECURITY-11 — Лише внутрішній Unknown Source override; no AV bypass/exclusions

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Діагностика, приватність та безпека.
- **Реалізація / місце в коді:** LocalModManager.cs; UI checkbox.
- **Наявні перевірки:** Unknown confirmation tests; no Defender mutations.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-security-12"></a>
#### SECURITY-12 — Crash Reports analyzer/local diagnostics patterns

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Діагностика, приватність та безпека.
- **Реалізація / місце в коді:** LauncherLog.cs; OfflineReadiness.cs.
- **Наявні перевірки:** Integrity/Java checks; full crash-pattern analyzer pending.
- **Що підтверджує запис:** Subset implemented; see limitations.
- **Залишок / межі:** Unverified/unsupported paths remain.
- **Наступна дія за записом:** Finish listed requirement and verify.

<a id="req-security-13"></a>
#### SECURITY-13 — Performance RAM/CPU/start-time monitoring

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Діагностика, приватність та безпека.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-security-14"></a>
#### SECURITY-14 — Automatic Mod Conflict Finder delta debugging

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Діагностика, приватність та безпека.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-security-15"></a>
#### SECURITY-15 — Isolated dependency-aware conflict test profiles без original worlds

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Діагностика, приватність та безпека.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-security-16"></a>
#### SECURITY-16 — Known risks і no absolute-security/SSD secure-erase claims

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Діагностика, приватність та безпека.
- **Реалізація / місце в коді:** docs/SECURITY.md; REQUIREMENTS_TRACEABILITY.md.
- **Наявні перевірки:** Documentation/code audit.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="group-multiplayer"></a>
### Сервери, спільна гра та Aternos — 17 вимог

<a id="req-multiplayer-01"></a>
#### MULTIPLAYER-01 — Local dedicated server із профілю

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Безкоштовні сервери.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-multiplayer-02"></a>
#### MULTIPLAYER-02 — Server/client/universal mod filtering і dependencies

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Безкоштовні сервери.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-multiplayer-03"></a>
#### MULTIPLAYER-03 — Server EULA explicit acceptance

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Безкоштовні сервери.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-multiplayer-04"></a>
#### MULTIPLAYER-04 — Dashboard start/stop/restart/players/RAM/CPU/console/whitelist/backups

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Безкоштовні сервери.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-multiplayer-05"></a>
#### MULTIPLAYER-05 — Online-mode/security defaults

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Безкоштовні сервери.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-multiplayer-06"></a>
#### MULTIPLAYER-06 — Share My World/LAN port detection/manual fallback

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Безкоштовні сервери.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-multiplayer-07"></a>
#### MULTIPLAYER-07 — Porthole detection/version/supported CLI/lobby/invite/port/session

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Безкоштовні сервери.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-multiplayer-08"></a>
#### MULTIPLAYER-08 — Porthole UI fallback, no undocumented API

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Безкоштовні сервери.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-multiplayer-09"></a>
#### MULTIPLAYER-09 — Explicit port and firewall/router permission boundaries

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Безкоштовні сервери.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-multiplayer-10"></a>
#### MULTIPLAYER-10 — Aternos server-mod list/name/version/dependencies/copy/checklist

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Безкоштовні сервери.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-multiplayer-11"></a>
#### MULTIPLAYER-11 — Aternos TXT/CSV/JSON export/progress/search/filter

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Безкоштовні сервери.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-multiplayer-12"></a>
#### MULTIPLAYER-12 — Aternos official manual panel/ready catalog modpack

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Безкоштовні сервери.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-multiplayer-13"></a>
#### MULTIPLAYER-13 — Aternos address/server ping із невигаданими статусами

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Безкоштовні сервери.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-multiplayer-14"></a>
#### MULTIPLAYER-14 — No Aternos website automation/private endpoints

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Безкоштовні сервери.
- **Реалізація / місце в коді:** docs/ROADMAP.md.
- **Наявні перевірки:** No automation/provider implementation.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-multiplayer-15"></a>
#### MULTIPLAYER-15 — Exclude exaroton/paid hosting integrations

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Безкоштовні сервери.
- **Реалізація / місце в коді:** docs/ROADMAP.md.
- **Наявні перевірки:** No paid hosting code.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-multiplayer-paper-01"></a>
#### MULTIPLAYER-PAPER-01 — Paper local server support

- **Походження:** додана й збережена вимога.
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Supported server versions, EULA and lifecycle.
- **Реалізація / місце в коді:** docs/ROADMAP.md.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** New explicit requirement retained.
- **Залишок / межі:** No server implementation.
- **Наступна дія за записом:** Implement after critical client/core work.

<a id="req-multiplayer-purpur-01"></a>
#### MULTIPLAYER-PURPUR-01 — Purpur local server support

- **Походження:** додана й збережена вимога.
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Supported server versions, EULA and lifecycle.
- **Реалізація / місце в коді:** docs/ROADMAP.md.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** New explicit requirement retained.
- **Залишок / межі:** No server implementation.
- **Наступна дія за записом:** Implement after critical client/core work.

<a id="group-extensions"></a>
### Плагіни, updater і підпис — 7 вимог

<a id="req-extensions-01"></a>
#### EXTENSIONS-01 — Plugin SDK: sources/import/export/translation/diagnostics/UI/server extensions

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Плагіни й оновлення.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-extensions-02"></a>
#### EXTENSIONS-02 — Plugin install/enable/disable/remove/update/compatibility

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Плагіни й оновлення.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-extensions-03"></a>
#### EXTENSIONS-03 — Plugin permissions і isolated untrusted processes

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Не реалізовано як завершена функція (`Not Implemented`).
- **Призначення:** Плагіни й оновлення.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** No implementation.
- **Залишок / межі:** Pending implementation.
- **Наступна дія за записом:** Implement in roadmap order.

<a id="req-extensions-04"></a>
#### EXTENSIONS-04 — No arbitrary in-process third-party DLL plugins

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Плагіни й оновлення.
- **Реалізація / місце в коді:** Solution code audit.
- **Наявні перевірки:** No plugin loader exists.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-extensions-05"></a>
#### EXTENSIONS-05 — Updater online check/changelog/download/signature/rollback

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Plugins and updates.
- **Реалізація / місце в коді:** Updates/LauncherUpdate*.cs; InnoUpdateAdapter.cs; MainViewModel.LauncherUpdates.cs; LauncherUpdateDialog; UpdateHelper; UpdateTools.
- **Наявні перевірки:** 70 updater fixture/UI/process cases; full 688 total / 686 passed / 2 explicit not run; executable E2E 3/3.
- **Що підтверджує запис:** Eight-language UI, verified helper/parent/lease guards, real test-build restart/health, startup failure rollback and live-timeout normal-close recovery verified locally.
- **Залишок / межі:** No production key/feed; Installed registry-blocked; self-contained production delivery and migrations unverified.
- **Наступна дія за записом:** Separate production trust setup and isolated Installed lifecycle; next MODPACKS-01.
- **Уточнення цього плану:** UI/helper/health/recovery і 3/3 actual executable scenarios є. Вони перевіряли framework-dependent ізольовані test builds, не production delivery. Production key/feed, managed inventory для старих пакетів і real Installed lifecycle залишаються блокерами.

<a id="req-extensions-06"></a>
#### EXTENSIONS-06 — GitHub Releases/release automation без неявної публікації

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Плагіни й оновлення.
- **Реалізація / місце в коді:** .github/workflows/windows-build.yml.
- **Наявні перевірки:** Windows CI 40759155278 and actual prerelease assets.
- **Що підтверджує запис:** Previous release source aec184651786 verified; new commits need separate CI.
- **Залишок / межі:** Unsigned prereleases; no Stable or updater implied.
- **Наступна дія за записом:** Verify each new source publication.

<a id="req-extensions-07"></a>
#### EXTENSIONS-07 — Installer digital signing із справжнім certificate

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Зовнішній блокер; реалізацію див. окремо (`Blocked by External Requirements`).
- **Призначення:** Плагіни й оновлення.
- **Реалізація / місце в коді:** installer/PTenoLauncher.iss.
- **Наявні перевірки:** No signing certificate supplied.
- **Що підтверджує запис:** External requirement unresolved.
- **Залишок / межі:** Own approval/key/login/certificate or unimplemented dependency required.
- **Наступна дія за записом:** Resolve prerequisites; continue independent work.

<a id="group-distribution"></a>
### Windows-пакети, документація та release QA — 12 вимог

<a id="req-distribution-01"></a>
#### DISTRIBUTION-01 — Windows 11 x64 self-contained EXE

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Windows реліз.
- **Реалізація / місце в коді:** scripts/build.ps1; Desktop.csproj.
- **Наявні перевірки:** Self-contained publish; native UI and installer GUI smoke.
- **Що підтверджує запис:** Actual Windows 11 x64 payload built and started.
- **Залишок / межі:** No clean external VM / signing certificate.
- **Наступна дія за записом:** External clean-system verification.

<a id="req-distribution-02"></a>
#### DISTRIBUTION-02 — PTenoLauncher-Setup-0.2.0-win-x64.exe, real ISCC compilation

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Windows реліз.
- **Реалізація / місце в коді:** installer/PTenoLauncher.iss; scripts/test-installer.py.
- **Наявні перевірки:** Latest ISCC compile; prior Fabric payload 5/5 lifecycle.
- **Що підтверджує запис:** Actual current unsigned Setup EXE compiled; lifecycle retest blocked at registry preflight.
- **Залишок / межі:** WinError 5 registry denial blocks current lifecycle; interactive wizard/shortcut checks pending.
- **Наступна дія за записом:** Signing and manual distribution QA.

<a id="req-distribution-03"></a>
#### DISTRIBUTION-03 — Installer Ukrainian/English/icon/directory/desktop/start shortcuts

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Код є; повна перевірка не завершена (`Implemented, Not Verified`).
- **Призначення:** Windows реліз.
- **Реалізація / місце в коді:** installer/PTenoLauncher.iss.
- **Наявні перевірки:** ISCC compile; silent install and registration.
- **Що підтверджує запис:** Bilingual wizard/config/icon and shortcut definitions compile; app install succeeds.
- **Залишок / межі:** Interactive language and desktop-task branches NOT RUN.
- **Наступна дія за записом:** Manual bilingual wizard/shortcut checks.

<a id="req-distribution-04"></a>
#### DISTRIBUTION-04 — Per-user Installed Apps/uninstaller/offline install

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Код є; повна перевірка не завершена (`Implemented, Not Verified`).
- **Призначення:** Windows реліз.
- **Реалізація / місце в коді:** installer/PTenoLauncher.iss; scripts/test-installer.py.
- **Наявні перевірки:** Prior Fabric-payload lifecycle; latest preflight blocked.
- **Що підтверджує запис:** HKCU Installed Apps registration, per-user install and uninstaller removal passed with bundled local payload.
- **Залишок / межі:** Clean external Windows VM not exercised.
- **Наступна дія за записом:** Current payload registry preflight blocked; external clean-system installation.

<a id="req-distribution-05"></a>
#### DISTRIBUTION-05 — Upgrade/uninstall preserve profiles/worlds/settings by default

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Код є; повна перевірка не завершена (`Implemented, Not Verified`).
- **Призначення:** Windows реліз.
- **Реалізація / місце в коді:** installer/PTenoLauncher.iss; scripts/test-installer.py.
- **Наявні перевірки:** Prior Fabric-payload upgrade/reinstall/uninstall; latest lifecycle blocked.
- **Що підтверджує запис:** Synthetic profile/settings/world sentinel/backup preserved by default.
- **Залишок / межі:** Older Setup generated from retained 0.1 payload with current recipe.
- **Наступна дія за записом:** Re-run current Setup lifecycle in a permitted isolated Windows environment; keep regression coverage for each release.

<a id="req-distribution-06"></a>
#### DISTRIBUTION-06 — Explicit opt-in own user-data cleanup при uninstall

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Код є; повна перевірка не завершена (`Implemented, Not Verified`).
- **Призначення:** Windows реліз.
- **Реалізація / місце в коді:** Security/UserDataCleaner.cs; installer/PTenoLauncher.iss.
- **Наявні перевірки:** Uninstaller020Tests PASS; prior opt-in uninstall PASS; latest lifecycle blocked.
- **Що підтверджує запис:** Owned data removed only on opt-in; unknown root note preserved; locked DB blocks before deletion.
- **Залишок / межі:** Not a physical SSD erasure guarantee.
- **Наступна дія за записом:** Keep isolated safety regressions.

<a id="req-distribution-07"></a>
#### DISTRIBUTION-07 — Portable relative paths без tokens/game data

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Код є; повна перевірка не завершена (`Implemented, Not Verified`).
- **Призначення:** Windows реліз.
- **Реалізація / місце в коді:** LauncherPaths.cs; scripts/package-preview.py.
- **Наявні перевірки:** 0.2 ZIP CRC/exclusions and extracted Portable GUI smoke PASS.
- **Що підтверджує запис:** Portable startup/local data verified; installed-game relocation not tested.
- **Залишок / межі:** Credential roaming is intentionally unsupported; full profile portability e2e pending.
- **Наступна дія за записом:** Test an installed profile after relocation.

<a id="req-distribution-08"></a>
#### DISTRIBUTION-08 — SHA256/release notes/source/docs/test reports

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Windows реліз.
- **Реалізація / місце в коді:** scripts/package-preview.py; scripts/verify-packages.py; TEST_REPORT.md.
- **Наявні перевірки:** Historical Windows CI 40833002559 verified Setup/Portable/Source/checksums.
- **Що підтверджує запис:** Prior actual preview e7b058cf31c3 exists; current historical-assets/Forge components source is local only.
- **Залишок / межі:** Unsigned; no new packages or current installer lifecycle verification in this block.
- **Наступна дія за записом:** Build/publish only if separately requested; retain package source guards.

<a id="req-distribution-09"></a>
#### DISTRIBUTION-09 — Do not redistribute Minecraft files/secrets у public packages

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Windows реліз.
- **Реалізація / місце в коді:** scripts/package-preview.py; installer/PTenoLauncher.iss.
- **Наявні перевірки:** 0.1 archive scan; repeated at 0.2 packaging.
- **Що підтверджує запис:** See named tests / evidence logs.
- **Залишок / межі:** Scope limited to named behavior.
- **Наступна дія за записом:** Keep regression coverage.

<a id="req-distribution-10"></a>
#### DISTRIBUTION-10 — README/User/Developer/Security/Auth/Porthole/Aternos/Server docs

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Частково реалізовано (`Partially Implemented`).
- **Призначення:** Windows реліз.
- **Реалізація / місце в коді:** README.md; docs/.
- **Наявні перевірки:** Implemented subsystem guides; server integration docs pending.
- **Що підтверджує запис:** Subset implemented; see limitations.
- **Залишок / межі:** Unverified/unsupported paths remain.
- **Наступна дія за записом:** Finish listed requirement and verify.

<a id="req-distribution-11"></a>
#### DISTRIBUTION-11 — REQUIREMENTS_TRACEABILITY + DEVELOPMENT_STATUS + TEST_REPORT + ROADMAP

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Реалізовано й перевірено в зазначеному обсязі (`Implemented & Verified`).
- **Призначення:** Windows реліз.
- **Реалізація / місце в коді:** DEVELOPMENT_STATUS.md; TEST_REPORT.md; REQUIREMENTS_TRACEABILITY.md; docs/ROADMAP.md.
- **Наявні перевірки:** New local 463 total / 461 passed / 2 explicit not run; requirements guard.
- **Що підтверджує запис:** 184 original plus five retained additions = 189 IDs; 44 graded modules; all deferred bridges retained.
- **Залишок / межі:** Historical failures/blockers retained; project-wide roadmap incomplete.
- **Наступна дія за записом:** Continue from docs/FORGE_COMPONENTS_CHECKPOINT.md.
- **Уточнення цього плану:** 463/461 у рядку — стара контрольна точка. Остання успішна ZIP baseline: 787/785/2 not-run; незавершені Prism source edits після неї ще не перевірені. Новий актуальний документ — цей master plan; source checkpoints не підміняти старим Forge.

<a id="req-distribution-12"></a>
#### DISTRIBUTION-12 — Real Vanilla/Fabric/Forge/NeoForge/Quilt/offline integration tests

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Зовнішній блокер; реалізацію див. окремо (`Blocked by External Requirements`).
- **Призначення:** Windows реліз.
- **Реалізація / місце в коді:** tests/PTenoLauncher.IntegrationSmoke.
- **Наявні перевірки:** Vanilla/Fabric/Quilt preparation verified; actual game needs official login.
- **Що підтверджує запис:** Fresh Quilt metadata and native library repair passed; cache-only repair preserved synthetic saves.
- **Залишок / межі:** Licensed game tests NOT RUN; Forge/NeoForge installers pending.
- **Наступна дія за записом:** Own approved client/user login, manual game tests and remaining native loaders.
- **Уточнення цього плану:** Блокер змішаний: licensed gameplay потребує офіційного акаунта, а broad Forge/NeoForge support та real artifact checks мають і внутрішні/мережеві прогалини. Не списувати все на Microsoft.

<a id="group-compat"></a>
### Відкладені compatibility bridges — 5 вимог

<a id="req-compat-sinytra-01"></a>
#### COMPAT-SINYTRA-01 — Optional Fabric mods on officially compatible NeoForge via Sinytra Connector

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Відкладено власником — не реалізовувати зараз (`DEFERRED`).
- **Призначення:** Opt-in compatibility bridge; never universal cross-loader support.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** Deferred by owner in 2026-10-28 brief.
- **Залишок / межі:** Official version-specific research and implementation pending.
- **Наступна дія за записом:** Do not implement in current scope.

<a id="req-compat-sinytra-02"></a>
#### COMPAT-SINYTRA-02 — Exact Connector dependencies: Forgified Fabric API and version-dependent Launchpad when required

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Відкладено власником — не реалізовувати зараз (`DEFERRED`).
- **Призначення:** Resolve exact official version dependencies without AI.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** Deferred by owner in 2026-10-28 brief.
- **Залишок / межі:** Official version-specific research and implementation pending.
- **Наступна дія за записом:** Do not implement in current scope.

<a id="req-compat-sinytra-03"></a>
#### COMPAT-SINYTRA-03 — Connector official compatibility results, unknown/conflict labels, isolated test profiles and catalog/cart/server integration

- **Походження:** початкова вимога (184-ID baseline).
- **Стан за реєстром:** Відкладено власником — не реалізовувати зараз (`DEFERRED`).
- **Призначення:** Safe compatibility and server preparation.
- **Реалізація / місце в коді:** —.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** Deferred by owner in 2026-10-28 brief.
- **Залишок / межі:** Official version-specific research and implementation pending.
- **Наступна дія за записом:** Do not implement in current scope.

<a id="req-compat-kilt-01"></a>
#### COMPAT-KILT-01 — Kilt compatibility bridge

- **Походження:** додана й збережена вимога.
- **Стан за реєстром:** Відкладено власником — не реалізовувати зараз (`DEFERRED`).
- **Призначення:** Retain future compatibility scope.
- **Реалізація / місце в коді:** docs/DEVELOPMENT_BRIEF_2026-10-28.md.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** Explicitly deferred by owner.
- **Залишок / межі:** Not implemented.
- **Наступна дія за записом:** No current implementation work.

<a id="req-compat-crossloader-01"></a>
#### COMPAT-CROSSLOADER-01 — Cross-Loader Compatibility Manager and Fabric-to-Forge, Forge-to-Fabric, Forge-to-NeoForge, NeoForge-to-Forge and other third-party bridges

- **Походження:** додана й збережена вимога.
- **Стан за реєстром:** Відкладено власником — не реалізовувати зараз (`DEFERRED`).
- **Призначення:** Retain all listed future bridge directions.
- **Реалізація / місце в коді:** docs/DEVELOPMENT_BRIEF_2026-10-28.md.
- **Наявні перевірки:** Not run.
- **Що підтверджує запис:** Explicitly deferred by owner.
- **Залишок / межі:** Not implemented; no universal compatibility claim.
- **Наступна дія за записом:** No current implementation work.

<a id="modules"></a>
## 8. Усі 44 модулі — recorded readiness

Оцінки перенесені зі summary без нового підвищення. Частина evidence формулювань історична, а новий AI UX — окремий незавершений scope. Для architecture/languages/Prism/offline див. аудиторські уточнення вище. Таблиця не є повторним виконанням тестів.

| Модуль | Пріоритет | Стан | Оцінка | Наявне evidence / межі | Наступна задача |
|---|---|---|---|---|---|
| Перший запуск: вибір мови | P1 | PARTIALLY VERIFIED | 75% | 14 Windows UI/SQLite/offline/restart/error-retry cases pass in the 344-case suite | Native interactive/DPI review |
| Архітектура .NET 10 / Avalonia | Foundation | VERIFIED | 100% | Наявні п'ять проєктів збираються; архітектуру збережено | Зберігати межі модулів |
| SQLite: налаштування, профілі, історія | Foundation | VERIFIED | 100% | Реальні ізольовані SQLite/файлові тести й перезавантаження UI | Міграції для нових функцій |
| Microsoft Authentication | P0 | BLOCKED EXTERNALLY | 50% | Browser OAuth проходив; downstream HTTP 403; контрактні тести й DPAPI працюють; approval не підтверджено | Ліцензійні entitlement/profile/refresh/multi-account тести |
| Minecraft Launch Engine | P0 | PARTIALLY VERIFIED | 50% | Vanilla/Fabric/Quilt підготовка; процес/консоль/exit/cancellation перевірені окремо; справжня гра NOT RUN | Авторизована гра, світ, збереження, повторний запуск |
| Java Manager | P0 | PARTIALLY VERIFIED | 75% | Real Windows managed 8/17/21/25 install/version/offline reuse and Java 21 cached repair; 386 tests pass | Runtime update/removal UI and licensed-game integration |
| Windows Installer | P0 | PARTIALLY VERIFIED | 75% | Реальний unsigned Setup у CI; lifecycle поточного payload не перевірений; попередній registry guard не обходиться | Ізольований Windows install/upgrade/reinstall/uninstall |
| Portable | P0 | PARTIALLY VERIFIED | 75% | Self-contained ZIP, CRC, чисте розпакування й старт перевірені | Перенесення встановленої гри між шляхами |
| Offline-First | P0 | PARTIALLY VERIFIED | 50% | Немає обов'язкової мережі при старті; кеш/локальні моди/налаштування; гра вимагає підтвердженого акаунта | Авторизована offline-гра для кожного loader |
| Minecraft Version Manager | P0 | PARTIALLY VERIFIED | 75% | Mojang manifest/cache plus synthetic historical virtual/mapped preparation and argument checks | Real historical components in an allowed network environment; gameplay |
| Fabric | P1 | PARTIALLY VERIFIED | 75% | Офіційний каталог, pin, реальна установка/repair/cache; гра NOT RUN | Online/offline модифікована гра |
| Quilt | P1 | PARTIALLY VERIFIED | 75% | Каталог/install/repair та root metadata перевірені; nested/conditions часткові | Nested candidates, conditional dependencies, gameplay |
| Forge | P1 | PARTIALLY VERIFIED | 25% | Strict processor adapter/generated SHA-1 outputs/transactional receipt and native UI fixtures pass; official live check failed 10051 | Verify actual official installers/processors; add missing-output/mapping contracts; gameplay remains unverified |
| NeoForge | P1 | PARTIALLY VERIFIED | 25% | Separate catalog/FML identity and strict staged processor/receipt fixtures pass; real catalog blocked 10051 | Real installer/generated outputs and gameplay verification; broader supported profile contracts |
| Modrinth | P1 | PARTIALLY VERIFIED | 75% | Search/version/dependencies/hash/cache і реальна кешована установка | Багатші фільтри, деталі й live-комбінації |
| CurseForge | P1 | BLOCKED EXTERNALLY | 0% | Провайдер не реалізовано; потрібен дозволений API-доступ | Дозволений клієнт, тести й інструкція |
| Unified Dependency Resolver | P1 | PARTIALLY VERIFIED | 50% | Детермінований Modrinth/local/cache, root Fabric/Quilt/Forge metadata; не повний cross-provider resolver | Складні ranges/nested/CurseForge та встановлені набори |
| Mod Installation Cart | P1 | PARTIALLY VERIFIED | 75% | Local/catalog cart, optional choices, hash identity, atomic journal/recovery; AI не потрібний | Складні live-набори й конфлікти |
| Оновлення модів / version lock | P1 | PARTIALLY VERIFIED | 50% | Bounded Modrinth exact pins, full-set preview, verified backup, apply/rollback/recovery and 42 new local/UI cases; live request blocked 10051 | Live provider/gameplay coverage and manual lifecycle review |
| Modpacks / import / export / friend sync | P1 | PARTIALLY VERIFIED | 25% | Supported .mrpack and local client ZIP preview/transaction/recovery/exact-target/UI workflows verified; own offline ZIP process passed; third-party live catalog blocked | Source-launcher imports, license-aware export, lockfile/friend comparison, broader real packs and gameplay |
| Профілі й безпечне видалення | P1 | PARTIALLY VERIFIED | 75% | CRUD/archive, preview/name/size/path ownership/leases і реальні ізольовані видалення перевірені | Розширені профільні налаштування й ручна UI-перевірка |
| Backups / restore | P1 | PARTIALLY VERIFIED | 75% | Реальні ізольовані файли, guard/rollback, synthetic saves | Додаткові місця зберігання й інтеграція з менеджерами |
| World Manager | P1 | PARTIALLY VERIFIED | 25% | Профільні saves/backups є; повного менеджера світів немає | Метадані, import/export/copy/version checks |
| Resource Pack Manager | P1 | PARTIALLY VERIFIED | 25% | Генерація профільних translation ZIP є; загального менеджера немає | Import/list/compatibility/confirmed activation |
| Shader Manager | P1 | NOT IMPLEMENTED | 0% | Немає менеджера й resolver для shader components | Імпорт, сумісність, видалення |
| Shared Content / дисковий простір | P1 | PARTIALLY VERIFIED | 25% | Shared verified cache є; керування простором не завершено | Облік використання й ownership-aware cleanup |
| Security Scanner | P1 | PARTIALLY VERIFIED | 25% | Hashes, ZIP guards, bounded static JAR reading є; це не доказ відсутності загроз | Дозволений threat feed, provenance, Defender |
| Automatic Mod Conflict Finder | P1 | NOT IMPLEMENTED | 0% | Dependency blockers не є контрольованим пошуком конфліктів | Ізольовані копії й дозволені пробні запуски |
| Логи / crash diagnostics | P1 | PARTIALLY VERIFIED | 50% | Redaction, bounded live console, exit codes; повний crash-report аналіз не завершено | Crash classification та зрозумілі локальні підказки |
| Downloads Manager | P1 | PARTIALLY VERIFIED | 25% | Перевірений downloader, progress/cancel є; загальної черги/resume немає | Queue, concurrency, часткові файли, retry |
| Performance Monitoring | P1 | NOT IMPLEMENTED | 0% | Немає завершеного моніторингу | Процесні метрики без телеметрії |
| AI Toggle / незалежність core | P2 | VERIFIED | 100% | Default off, HTTP gates, consent і cancellation; core-тести працюють без AI | Зберігати гарантії для нових модулів |
| Online AI Assistant / selected logs | P2 | IMPLEMENTED BUT UNVERIFIED | 75% | Реальна Responses реалізація, DPAPI й контрактні тести; live API/quality NOT RUN | Добровільний оплачуваний live-тест власного ключа |
| AI Translation / editor / resource pack | P2 | PARTIALLY VERIFIED | 50% | Modern outer-JAR JSON, incremental/manual edits/export/recovery; legacy/nested/auto pack_format відсутні | Формати, зручний редактор, gameplay activation |
| Server Manager: Vanilla/Paper/Purpur/Fabric/Forge/NeoForge/Quilt | P2 | NOT IMPLEMENTED | 0% | Серверна вкладка не є реалізацією; EULA не приймається автоматично | Офіційні ядра, versions, EULA, dashboard |
| Share My World / Porthole | P2 | NOT IMPLEMENTED | 0% | Підтримуваний CLI/API не перевірено | Виявлення інтерфейсу або ручна інструкція |
| Aternos Compatibility Assistant | P2 | NOT IMPLEMENTED | 0% | Немає checklist/copy/export; website automation виключена | Ручний офіційний workflow і server-mod list |
| Plugin SDK / Manager / permissions | P2 | NOT IMPLEMENTED | 0% | Немає стороннього виконуваного коду; sandbox не заявляється | Process isolation та мінімальні дозволи |
| Auto Updater / trusted origin / rollback | P2 | PARTIALLY VERIFIED | 75% | UI/helper/guards/restart/health/Inno adapter; 3/3 real executable test scenarios and 70 updater cases pass | Production key/feed, real Installed lifecycle and self-contained delivery remain blocked/unverified |
| GitHub Release Automation | Release | PARTIALLY VERIFIED | 75% | Реальний успішний Windows CI/prerelease з чотирма assets | Кожен новий commit перевіряти окремо |
| Локалізація восьми мов / accessibility | Release | PARTIALLY VERIFIED | 50% | 370 UI keys кожної мови після mrpack UI; технічні діагностики, theme names, DPI/linguistic QA часткові | Завершувати після стабілізації P0 |
| Theme Manager / graphite UI | Release | PARTIALLY VERIFIED | 75% | SQLite, DynamicResource, п'ять тем; custom/system та DPI review часткові | Контраст, клавіатура, system/custom QA |
| Цифровий підпис | Release | BLOCKED EXTERNALLY | 0% | Потрібні власний сертифікат і процедура підписання | Не називати поточні binaries підписаними |
| Sinytra / Kilt / Cross-Loader bridges | Deferred | DEFERRED | 0% | Відкладено власником; усі напрями збережені у traceability | Не реалізовувати в поточному обсязі |

<a id="ai-ux"></a>
## 9. Повні уточнення AI UX — ще не реалізовано

Наступний текст включено цілком із затвердженого документаційного плану. UX-01–06 — acceptance-підпункти наявних AI/UI ID, а не шість окремих завершених вимог.

### План зрозумілого ШІ-інтерфейсу та перекладів модів

Дата: 2026-10-30. Джерело: прямий запит власника й три скриншоти поточного інтерфейсу.

Статус: **PLANNED / НЕ РЕАЛІЗОВАНО в межах цього запису**. Змінено лише документацію. Це деталізація AI-05/06/07/08/09/10/11, UI-06/07/08 та OFFLINE-08; наявні 189 ID і 44 модулі збережені, оцінки готовності не підвищено. Sinytra/Kilt/Cross-Loader залишаються DEFERRED.

#### Пріоритет і поточний стан

Наступний пріоритетний користувацький блок після безпечного збереження незавершеної роботи — зрозумілий AI UX. Поточні Prism/MultiMC edits збережені окремо в PRISM_MULTIMC_IMPORT_CHECKPOINT.md; їх не відкидати й не оголошувати перевіреними. Цей документ не запускає розробку й не скасовує паузу реалізації.

Зараз AI key/model/input/preview — сусідні великі поля, а кнопка надсилання потребує прокручування. Ключ після збереження очищується без достатньо помітного стану підключення. Переклади мають лише target locale; джерело жорстко en_us. Збережені drafts доступні через кнопку й namespace selector, редактор — сирий JSON. pack_format вводиться числом вручну. Це перевірені обмеження поточного коду, а не бажаний UX.

#### UX-01. Окреме зрозуміле підключення ШІ

Пов'язано: AI-01/02/05, SECURITY-03.

- У нинішній direct-client архітектурі кожен користувач вводить власний OpenAI API-ключ. Ключ розробника не включати до програми/пакетів. Спільний оплачуваний сервіс через backend не входить до цього плану.
- Окрема картка «Підключення ШІ»: коротке пояснення, поле «Вставте ваш API-ключ», приховане значення, «Зберегти ключ», «Замінити», «Видалити».
- Помітні стани: «Ключ не додано», «Ключ збережено», «Підключення перевірено», «Немає інтернету», «ШІ вимкнено», зрозуміла помилка. Збереження ключа не означає успішного live-запиту.
- Короткі посилання «Де отримати ключ?» і пояснення окремої оплати. DPAPI/store=false/endpoint прибрати з основного сценарію в розгорнуті технічні відомості.
- Поле моделі з поясненням; не змушувати вводити незрозумілий ID без підказки. Якщо є вибір моделей, не вигадувати доступність і ціни. Ручний ID — у додаткових налаштуваннях.
- «Перевірити підключення» виконує короткий запит лише після явного підтвердження можливих витрат. Ніякого платного тесту під час збереження ключа.
- Ключ залишається локально захищеним, не потрапляє до логів, export або AI conversation. Відсутність ключа не блокує локальне редагування/перегляд/export перекладів.

#### UX-02. ШІ-асистент як зрозумілий чат

Пов'язано: AI-05/11, UI-06/07/08.

- Головний елемент — підписане поле «Напишіть запитання…» з кнопкою «Надіслати» поруч. Воно та поточний стан не повинні губитися нижче технічних налаштувань.
- Відповіді й запитання показувати як розмову з видимим станом очікування, «Скасувати», «Копіювати відповідь» та «Очистити чат».
- Для першого використання — коротке пояснення й приклади запитань. Налаштування підключення відокремлені від самого чату.
- «Запитати» та «Пояснити помилку/лог» — різні зрозумілі режими. Не створювати враження автоматичного доступу до всіх файлів чи логів.
- Зберегти preview/redaction/consent перед передаванням обраних даних. Сховати повний дубль тексту в «Переглянути дані для надсилання», але не прибирати можливість перевірки.
- Не обіцяти пам'ять розмови лише через chat bubbles: визначити й пояснити, чи передаються попередні повідомлення; очищення має очищувати відповідний контекст. Постійне збереження чатів — лише за окремою явною політикою.
- Причини недоступності кнопки пояснювати поруч. Помилки доступу/ключа/лімітів/мережі показувати зрозуміло, технічні подробиці розгортати.

#### UX-03. Два окремі вибори мов

Пов'язано: AI-06/09.

- Два підписані списки «З якої мови» → «На яку мову», з людськими назвами: «Англійська», «Українська»; locale codes — додаткові відомості.
- Source selector показує лише реально знайдені й підтримувані ресурси вибраного мода. Після вибору кількох модів показати відсутні мови по кожному; не робити непомітний fallback до en_us.
- Явний fallback або зміна вибраних модів потребує нового preview/consent. Вибір однакової source/target пояснювати, не надсилати зайвий платний запит.
- Реально читати обраний source locale; змінити translation instructions і provenance. Додавання лише декоративної другої вибиралки не виконує вимогу.
- Розділити drafts за profile/source/target/namespace; зберегти manual edits, incremental updates, format placeholders і recovery. Колізій між напрямками перекладу бути не повинно.
- Старі drafts трактувати як source=en_us, target=наявна Locale; перед зміною структури зберегти backup, не втратити попередні ручні виправлення.
- Legacy .lang/nested/component-valued JSON не оголошувати підтримуваними лише через новий selector; окремо показати їхні обмеження.

#### UX-04. Бібліотека «Мої переклади» і звичайний редактор

Пов'язано: AI-08/09/10, PROFILES-20, OFFLINE-08.

- Автоматично показувати збережені переклади вибраного профілю без повторного вибору JAR і без API-ключа/мережі.
- Список: назва мода, напрям мов, готово/всього, потребує перевірки, дата зміни. Namespace показувати як fallback, якщо назва невідома.
- Пошук/фільтри: профіль, мод, напрям мов, відсутні рядки, потребують перевірки, ручні зміни.
- Основний редактор — таблиця «Оригінал | Переклад | Стан», пошук рядків, збереження правок і попередження про незбережене. Keys/JSON — додатковий режим, не обов'язковий для звичайного користувача.
- Ручні правки не перезаписувати автоматичним перекладом; зміна source помітно вимагає review. Частково завершені переклади можна відкрити та продовжити.
- Окремі зрозумілі дії: «Відкрити», «Продовжити переклад», «Створити ресурс-пак», «Відкрити теку». Видалення — з явним scope/підтвердженням; воно не повинно видаляти мод/світ або чужі packs.

#### UX-05. Ресурс-пак без ручного pack_format

Пов'язано: AI-07/10, PROFILES-16, OFFLINE-08.

- Основна кнопка — «Створити переклад для цього профілю» / «Створити ресурс-пак» із коротким поясненням результату.
- Автоматично визначати потрібну структуру/формат ресурспака за точною версією Minecraft профілю, використовуючи перевірені version-specific дані. Не обчислювати формат евристикою з номера версії й не вважати одне поле достатнім для всіх майбутніх версій.
- За невідомої версії показати зрозумілий blocker/вибір підтримуваної версії; не генерувати завідомо вгаданий формат. Ручні технічні поля — лише в «Додатково».
- Після генерації показати, скільки рядків включено/пропущено, куди збережено, кнопку «Відкрити теку» й просту інструкцію увімкнення пакета та мови у грі.
- Зберегти ownership, backup попереднього generated pack і захист user files. Автоматичну активацію не заявляти без окремої реалізації/перевірки.

#### UX-06. Зрозумілий layout і критерії приймання

Пов'язано: UI-06/07/08, THEMES-05, AI-05/08.

- Короткі секції та підписи над кожним полем; видимий вибір профілю замість непідписаної назви на кшталт «21». Empty states пояснюють наступний крок.
- Прибрати обов'язкові JSON/namespace/pack_format/DPAPI терміни з основного сценарію; технічні подробиці залишити доступними.
- Перевірити вікно на підтримуваних розмірах, 100/125/150/200% DPI, клавіатуру, контраст і читабельність усіх восьми мов. Основні кнопки не мають обрізатися по горизонталі.
- Meaningful actual-control tests: missing/saved key states без платного request, окремі input/response, зміна source/target і consent, читання не-en_us джерела, старі drafts/manual edits, offline library/editor/export, version-specific pack metadata, unknown version, stale selection і data preservation.
- Перевірки fixtures, native/manual UI, live provider quality та застосування у грі — різні докази. Жодних платних запитів без окремої явної згоди; ключі й акаунти для тестів не читати автоматично.

#### Запропонований порядок

1. Зберегти й перевірити цілісність поточних незавершених Prism/MultiMC edits перед будь-яким переключенням задачі; не переносити на них старий 787/785 результат.
2. Підключення ШІ й зрозумілий assistant layout.
3. Реальні source/target locales та сумісна міграція drafts.
4. Бібліотека перекладів і табличний editor.
5. Автоматичний ресурс-пак, пояснення результату й manual UI review.
6. Відновити загальну roadmap: source-launcher imports, license-aware export/friend sync, content/download/Java managers, diagnostics/server/plugin blocks. Production updater та licensed gameplay перевіряти окремо.

<a id="sources"></a>
## 10. Джерела, повнота й оновлення

Первинне ТЗ збережене без змін: [DEVELOPMENT_BRIEF_2026-10-28.md](DEVELOPMENT_BRIEF_2026-10-28.md). Його старі результати й порядок уточнені новішими checkpoint та прямими вказівками власника.

- [REQUIREMENTS_TRACEABILITY.md](../REQUIREMENTS_TRACEABILITY.md)
- [PROJECT_PROGRESS_SUMMARY.md](../PROJECT_PROGRESS_SUMMARY.md)
- [SESSION_CHECKPOINT.md](../SESSION_CHECKPOINT.md)
- [DEVELOPMENT_STATUS.md](../DEVELOPMENT_STATUS.md)
- [TEST_REPORT.md](../TEST_REPORT.md)
- [docs/ROADMAP.md](ROADMAP.md)
- [docs/requirements-baseline.json](requirements-baseline.json)
- [docs/DEVELOPMENT_BRIEF_2026-10-28.md](DEVELOPMENT_BRIEF_2026-10-28.md)
- [docs/AI_USABILITY_PLAN.md](AI_USABILITY_PLAN.md)
- [docs/JAVA_RUNTIME_CHECKPOINT.md](JAVA_RUNTIME_CHECKPOINT.md)
- [docs/HISTORICAL_ASSETS_CHECKPOINT.md](HISTORICAL_ASSETS_CHECKPOINT.md)
- [docs/FORGE_EXECUTION_CHECKPOINT.md](FORGE_EXECUTION_CHECKPOINT.md)
- [docs/NATIVE_NESTED_CHECKPOINT.md](NATIVE_NESTED_CHECKPOINT.md)
- [docs/MOD_UPDATES_CHECKPOINT.md](MOD_UPDATES_CHECKPOINT.md)
- [docs/LAUNCHER_UPDATER_WORKFLOW_CHECKPOINT.md](LAUNCHER_UPDATER_WORKFLOW_CHECKPOINT.md)
- [docs/MRPACK_IMPORT_CHECKPOINT.md](MRPACK_IMPORT_CHECKPOINT.md)
- [docs/ZIP_IMPORT_CHECKPOINT.md](ZIP_IMPORT_CHECKPOINT.md)
- [docs/PRISM_MULTIMC_IMPORT_CHECKPOINT.md](PRISM_MULTIMC_IMPORT_CHECKPOINT.md)

### Як підтримувати цей план

Після завершеного блока оновити authoritative traceability + newest checkpoint + test evidence, потім цей master/snapshot. Зберегти кожен original ID; нові уточнення додавати як acceptance details або явні нові ID з поясненням, не замінюючи попередні. Розрізняти code/read-only audit, saved test result, fresh test run, official artifacts, UI/manual, gameplay, release.

Числа, hashes і статуси тут — snapshot на дату документа. Вони не перекривають новіші локальні зміни. Для відновлення розробки спочатку знову читати newest checkpoint; цей документ сам по собі не надає дозволу на live paid requests, акаунти, EULA, publish або destructive actions.

### Перевірка повноти під час створення

- 184 original IDs присутні; п’ять доданих ID збережені.
- 189 унікальних requirement cards, без дублікатів; усі 8 полів кожного рядка збережені.
- 44 module rows; усі п’ять explicit bridge rows DEFERRED.
- Шість нових AI UX acceptance blocks включені повністю.
- Нові build/tests/API/gameplay не запускалися для цієї документаційної роботи.

Machine-readable snapshot з усіма полями: [MASTER_PROJECT_PLAN_2026-10-30.json](MASTER_PROJECT_PLAN_2026-10-30.json). Це companion plan data, не програма й не секрети.
