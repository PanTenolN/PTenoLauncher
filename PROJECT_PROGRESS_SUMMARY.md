# PTenoLauncher — стан модулів

Оновлено 2026-10-28. Це контрольна шкала готовності з нового завдання, а не математичний відсоток завершення всього продукту. 0% — немає реалізації; 25% — початковий код; 50% — часткова працездатність; 75% — основна реалізація з автоматичними тестами, але важливими прогалинами інтеграції; 100% — завершено й перевірено у відповідному обсязі. Синтетичний процес не є Minecraft, а успішна збірка не є перевіркою гри.

База: release source `aec1846517865a0953b1037bd6b55f8ce8807efc`, Windows CI `40759155278`, документація `4374db9f44bacb2fac2960b2f01a63d532b7e603`. Поточний блок: кероване завершення процесу й підтвердження зупинки; **330/330 тестів**, Release build **0 errors / 0 warnings**. Результат нової публікації записується окремо в `docs/PROCESS_LIFECYCLE_RELEASE_CHECKPOINT.md`; попередній перевірений реліз — https://github.com/PanTenolN/PTenoLauncher/releases/tag/v0.2.0-preview.aec184651786 .

| Module | Priority | State | Readiness | Evidence / limits | Next task |
|---|---|---|---|---|---|
| Архітектура .NET 10 / Avalonia | Foundation | VERIFIED | 100% | Наявні п'ять проєктів збираються; архітектуру збережено | Зберігати межі модулів |
| SQLite: налаштування, профілі, історія | Foundation | VERIFIED | 100% | Реальні ізольовані SQLite/файлові тести й перезавантаження UI | Міграції для нових функцій |
| Microsoft Authentication | P0 | BLOCKED EXTERNALLY | 50% | Browser OAuth проходив; downstream HTTP 403; контрактні тести й DPAPI працюють; approval не підтверджено | Ліцензійні entitlement/profile/refresh/multi-account тести |
| Minecraft Launch Engine | P0 | PARTIALLY VERIFIED | 50% | Vanilla/Fabric/Quilt підготовка; процес/консоль/exit/cancellation перевірені окремо; справжня гра NOT RUN | Авторизована гра, світ, збереження, повторний запуск |
| Java Manager | P0 | PARTIALLY VERIFIED | 50% | Кероване завантаження й офлайн-повтор Java 21 перевірялися; 8/17/25 потребують окремих live-перевірок | Повна матриця Java та історичних версій |
| Windows Installer | P0 | PARTIALLY VERIFIED | 75% | Реальний unsigned Setup у CI; lifecycle поточного payload не перевірений; попередній registry guard не обходиться | Ізольований Windows install/upgrade/reinstall/uninstall |
| Portable | P0 | PARTIALLY VERIFIED | 75% | Self-contained ZIP, CRC, чисте розпакування й старт перевірені | Перенесення встановленої гри між шляхами |
| Offline-First | P0 | PARTIALLY VERIFIED | 50% | Немає обов'язкової мережі при старті; кеш/локальні моди/налаштування; гра вимагає підтвердженого акаунта | Авторизована offline-гра для кожного loader |
| Minecraft Version Manager | P0 | PARTIALLY VERIFIED | 75% | Mojang manifest, latest/releases/snapshots/history, кеш і SHA-1 перевірки | Старі virtual/mapped assets і gameplay |
| Fabric | P1 | PARTIALLY VERIFIED | 75% | Офіційний каталог, pin, реальна установка/repair/cache; гра NOT RUN | Online/offline модифікована гра |
| Quilt | P1 | PARTIALLY VERIFIED | 75% | Каталог/install/repair та root metadata перевірені; nested/conditions часткові | Nested candidates, conditional dependencies, gameplay |
| Forge | P1 | PARTIALLY VERIFIED | 25% | Root TOML/Maven metadata є; installer/catalog/launch відсутні | Офіційний каталог і перевірений installer |
| NeoForge | P1 | PARTIALLY VERIFIED | 25% | Окремі root TOML metadata; FML не вгадується; installer відсутній | Installer, FML metadata, libraries |
| Modrinth | P1 | PARTIALLY VERIFIED | 75% | Search/version/dependencies/hash/cache і реальна кешована установка | Багатші фільтри, деталі й live-комбінації |
| CurseForge | P1 | BLOCKED EXTERNALLY | 0% | Провайдер не реалізовано; потрібен дозволений API-доступ | Дозволений клієнт, тести й інструкція |
| Unified Dependency Resolver | P1 | PARTIALLY VERIFIED | 50% | Детермінований Modrinth/local/cache, root Fabric/Quilt/Forge metadata; не повний cross-provider resolver | Складні ranges/nested/CurseForge та встановлені набори |
| Mod Installation Cart | P1 | PARTIALLY VERIFIED | 75% | Local/catalog cart, optional choices, hash identity, atomic journal/recovery; AI не потрібний | Складні live-набори й конфлікти |
| Оновлення модів / version lock | P1 | NOT IMPLEMENTED | 0% | Вибір версій і receipts не дорівнюють повноцінному updater/lockfile | Preview змін, backup, update, rollback |
| Modpacks / import / export / friend sync | P1 | NOT IMPLEMENTED | 0% | Немає завершених .mrpack/launcher import та дозволеного export | Manifest, preview, ліцензії, транзакції |
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
| Auto Updater / trusted origin / rollback | P2 | NOT IMPLEMENTED | 0% | GitHub releases не є updater | Криптографічне походження, preview й відкат |
| GitHub Release Automation | Release | PARTIALLY VERIFIED | 75% | Реальний успішний Windows CI/prerelease з чотирма assets | Кожен новий commit перевіряти окремо |
| Локалізація восьми мов / accessibility | Release | PARTIALLY VERIFIED | 50% | 233 UI keys кожної мови після нового stop-dialog; технічні діагностики, theme names, DPI/linguistic QA часткові | Завершувати після стабілізації P0 |
| Theme Manager / graphite UI | Release | PARTIALLY VERIFIED | 75% | SQLite, DynamicResource, п'ять тем; custom/system та DPI review часткові | Контраст, клавіатура, system/custom QA |
| Цифровий підпис | Release | BLOCKED EXTERNALLY | 0% | Потрібні власний сертифікат і процедура підписання | Не називати поточні binaries підписаними |
| Sinytra / Kilt / Cross-Loader bridges | Deferred | DEFERRED | 0% | Відкладено власником; усі напрями збережені у traceability | Не реалізовувати в поточному обсязі |

Повний інвентар вимог — `REQUIREMENTS_TRACEABILITY.md`; початкові 184 ID збережено в `docs/requirements-baseline.json`. Нове завдання збережено повністю в `docs/DEVELOPMENT_BRIEF_2026-10-28.md`. Додаткові Paper/Purpur і deferred Kilt/Cross-Loader вимоги не підміняють початкові.

Наступний незалежний блок P0: матриця Java 8/17/21/25, контроль receipt/path/architecture, історичні assets/launch arguments. Після Microsoft approval — ручний сценарій `docs/HOME_TEST_CHECKLIST.md`. Installer lifecycle перевіряти у придатному ізольованому Windows-середовищі; попередній WinError 5 guard не обходити. Stable Release заблоковано до реальних P0-перевірок.

**Created and developed with AI assistance (OpenAI Codex).**
