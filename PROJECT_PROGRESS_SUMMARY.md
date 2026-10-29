# PTenoLauncher — стан модулів

## Verified managed-Java preview - 2026-10-29

Final release source: `e7b058cf31c32e7f3d44ee9b2acedb821e2c148e`; runtime implementation: `957636b2ec16c406c8749bf00aa3e439266e9ab1`. Windows CI **40833002559 SUCCESS**: https://github.com/PanTenolN/PTenoLauncher/actions/runs/40833002559 . Release build **0 errors/warnings**; **386/386 tests passed**, zero failed/errors/skipped/not-run. The real managed Java **8/17/21/25** matrix passed download/inventory/x64/version and zero-HTTP offline reuse for each major, Java 21 corruption detection/cached-archive repair and Java 17 inherited-option isolation. Synthetic data was preserved. Live Mojang Java metadata was reusable offline (observed 1.12.2→8, 1.20.1→17, 1.21.1→21, 26.3→25). `gameLaunched=false`.

Actual prerelease: https://github.com/PanTenolN/PTenoLauncher/releases/tag/v0.2.0-preview.e7b058cf31c3 . Setup/Portable/Source/checksums were read back; package hashes match the build log. ZIP/source/progress checks and fresh Portable initialization passed. All 184 original / 189 current requirements and 44 module grades remain. Detailed evidence: `docs/JAVA_RUNTIME_CHECKPOINT.md`.

The local Adoptium network failure remains explicitly failed/blocked evidence; the successful live checks ran on the Windows runner. Earlier run 40832409126 passed its Windows build/tests/runtime matrix but failed publication because added log paths changed artifact layout; the final source fixes that layout and retains Java report/logs under artifacts. This verified record supersedes historical pending-publication notes. Licensed Minecraft gameplay, Microsoft approval, live AI, native UI/DPI and current installer lifecycle remain unverified; no stable release is claimed. Next independent P0 block: historical virtual/mapped assets and launch arguments.

## Managed Java integrity and runtime verification - 2026-10-29

Restored all 27 newer first-run-language source/report files from main 21eefdd57b2a95c786ea7ecd1601a8925eb85d51 after Git-blob comparison (only CRLF normalization differed); previous local files were backed up. The eight-language menu and 344-test baseline are preserved.

Fixed OfflineReadiness invoking java.exe after detecting managed-runtime corruption. Added exact tracked-executable/folder identity, complete bounded inventory and hash/size checks, untracked-DLL/link rejection, strict Temurin major/platform/source validation, bounded x64-only version inspection and probe-only inherited Java-option removal. Corrupt runtime directories are retained and cached archives can repair into a new verified directory. Added explicit managed-only installation and online/offline Minecraft Java requirement lookup. See docs/JAVA_RUNTIME_VERIFICATION.md.

Local Release build: **0 errors, 0 warnings**. Full suite: **386/386 passed**, zero failed/errors/skipped/not-run, including 42 new JavaRuntimeIntegrityTests. Evidence: artifacts/0.2.0/java-runtime-release-build.log; java-runtime-tests.log; tests/java-runtime.trx. The real local runtime matrix failed before its first install because api.adoptium.net was network-unreachable (SocketException 10051). That failed/blocked evidence is retained and is NOT a successful live install.

Windows CI now gates packaging/publication on the actual managed Java 8/17/21/25 matrix: official download/hash/extraction/version, zero-HTTP offline reuse, Java 21 cached-archive corruption repair, Java 17 process-local option-isolation probe, synthetic-data preservation and cached Mojang Java metadata. Its new source commit/run/release result must be verified separately in docs/JAVA_RUNTIME_CHECKPOINT.md. All 184 original / 189 current requirements and 44 module grades remain; bridges remain DEFERRED. No Microsoft approval, Minecraft gameplay, live AI quality, installer lifecycle or stable release is claimed. Next core work: historical virtual/mapped assets/arguments and licensed gameplay after approval.

## Verified first-run language preview - 2026-10-29

Release source: `780f22642a658ddb4f8fee5bc9ccbdc795fcf3c0`. Windows CI **40827117958 SUCCESS**: https://github.com/PanTenolN/PTenoLauncher/actions/runs/40827117958 . The complete suite passed **344/344 tests**, zero failed/errors/skipped/not-run; Release build **0 errors, 0 warnings**. This includes 14 new actual-XAML/offline/SQLite/restart/preservation/write-failure cases for language onboarding. Both ZIP integrity checks, required-source/progress checks and freshly extracted self-contained Portable startup passed. Requirements guard: 184 original IDs retained, 189 current rows, 44 graded modules.

Actual prerelease: https://github.com/PanTenolN/PTenoLauncher/releases/tag/v0.2.0-preview.780f22642a65 . Setup, Portable, Source ZIP and SHA256SUMS were read back and their package hashes matched the Windows build log. The earlier Linux-only startup failures remain recorded as Windows-DPAPI platform limitations; they were not reclassified as passing Linux tests. The first-run selection feature uses 238-key catalogs across eight languages, preserves supported saved preferences and leaves authentication/readiness gates unchanged.

This supersedes older first-run notes awaiting Windows CI/publication. Interactive native language-menu/DPI review, Microsoft approval/licensed gameplay, live AI quality, Java 8/17/25 and current installer lifecycle remain unverified. Checkpoint: `docs/FIRST_RUN_LANGUAGE_CHECKPOINT.md`. Resume independent P0 Java/runtime/asset work after this completed UI block; do not recreate the project or undo the deferred-bridge decision.

## First-run language selection - 2026-10-29

Added the owner's requested language menu before the first normal launcher session. Eight native language names, immediate preview, explicit SQLite confirmation, saved-preference skipping, unsupported/case-normalized preferences and write-failure retry use the existing architecture. Cancel/exit leaves no saved preview. The hidden Settings picker cannot auto-save onboarding changes. Microsoft/AI/network access is not required; authentication and licensing are unchanged. See docs/FIRST_RUN_LANGUAGE.md.

Local Release compilation: 0 errors/warnings. Seventeen catalog/picker cases pass on Linux. The broader Linux filter ran 38 cases: 23 passed, 15 failed at the existing Windows DPAPI startup guard. This is retained as failed/platform-limited evidence, not a successful startup test. Fourteen new actual-XAML/SQLite/preservation cases are added for the full Windows runner (expected suite 344); Windows CI and a new prerelease must be verified separately in docs/FIRST_RUN_LANGUAGE_CHECKPOINT.md. Prior actual Windows preview remains source 827e8832a95baf5d2c9f805fb3c43f563427c90b, CI 40773211706, 330 passed.

UI-FIRSTLANG adds one explicit requirement: all original 184 IDs and four previous additions remain, 189 total; 44 module grades are tracked. Catalogs now contain 238 keys each. Overall localization/DPI remains partial; deferred compatibility bridges remain deferred. Next P0 core/Java/gameplay/installer and live AI checks remain as recorded in the roadmap. No current Microsoft approval, game launch or latest installer lifecycle is implied.

## Verified process-lifecycle preview - 2026-10-28

Final release source: `827e8832a95baf5d2c9f805fb3c43f563427c90b`; implementation commit: `95d7d7e7ec94dbfee0514f390f239ef35c419505`. Windows CI **40773211706 SUCCESS**: https://github.com/PanTenolN/PTenoLauncher/actions/runs/40773211706 . Release build **0 errors/warnings**, **330/330 tests passed**, 184 original / 188 current requirements and 43 graded modules validated, both ZIP CRC checks passed, fresh extracted self-contained Portable initialized successfully. `gameLaunched=false`.

Actual prerelease: https://github.com/PanTenolN/PTenoLauncher/releases/tag/v0.2.0-preview.827e8832a95b . Setup EXE, Portable ZIP, Source ZIP and SHA256SUMS were verified by release metadata; the final package checks require the progress report and continuation/source files. This supersedes older entries awaiting CI/publication. No installer lifecycle, Microsoft approval, actual Minecraft gameplay or live AI/provider-quality result is implied. Binaries remain unsigned. Detailed return point: `docs/PROCESS_LIFECYCLE_RELEASE_CHECKPOINT.md`; module grades: `PROJECT_PROGRESS_SUMMARY.md`. Next independent P0 work remains the Java 8/17/21/25 matrix, managed-runtime receipt/path integrity and historical assets/arguments.

Оновлено 2026-10-28. Це контрольна шкала готовності з нового завдання, а не математичний відсоток завершення всього продукту. 0% — немає реалізації; 25% — початковий код; 50% — часткова працездатність; 75% — основна реалізація з автоматичними тестами, але важливими прогалинами інтеграції; 100% — завершено й перевірено у відповідному обсязі. Синтетичний процес не є Minecraft, а успішна збірка не є перевіркою гри.

База: release source `aec1846517865a0953b1037bd6b55f8ce8807efc`, Windows CI `40759155278`, документація `4374db9f44bacb2fac2960b2f01a63d532b7e603`. Поточний блок: кероване завершення процесу й підтвердження зупинки; **330/330 тестів**, Release build **0 errors / 0 warnings**. Результат нової публікації записується окремо в `docs/PROCESS_LIFECYCLE_RELEASE_CHECKPOINT.md`; попередній перевірений реліз — https://github.com/PanTenolN/PTenoLauncher/releases/tag/v0.2.0-preview.aec184651786 .

| Module | Priority | State | Readiness | Evidence / limits | Next task |
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
| Локалізація восьми мов / accessibility | Release | PARTIALLY VERIFIED | 50% | 238 UI keys кожної мови після нового stop-dialog; технічні діагностики, theme names, DPI/linguistic QA часткові | Завершувати після стабілізації P0 |
| Theme Manager / graphite UI | Release | PARTIALLY VERIFIED | 75% | SQLite, DynamicResource, п'ять тем; custom/system та DPI review часткові | Контраст, клавіатура, system/custom QA |
| Цифровий підпис | Release | BLOCKED EXTERNALLY | 0% | Потрібні власний сертифікат і процедура підписання | Не називати поточні binaries підписаними |
| Sinytra / Kilt / Cross-Loader bridges | Deferred | DEFERRED | 0% | Відкладено власником; усі напрями збережені у traceability | Не реалізовувати в поточному обсязі |

Повний інвентар вимог — `REQUIREMENTS_TRACEABILITY.md`; початкові 184 ID збережено в `docs/requirements-baseline.json`. Нове завдання збережено повністю в `docs/DEVELOPMENT_BRIEF_2026-10-28.md`. Додаткові Paper/Purpur і deferred Kilt/Cross-Loader вимоги не підміняють початкові.

Наступний незалежний блок P0: матриця Java 8/17/21/25, контроль receipt/path/architecture, історичні assets/launch arguments. Після Microsoft approval — ручний сценарій `docs/HOME_TEST_CHECKLIST.md`. Installer lifecycle перевіряти у придатному ізольованому Windows-середовищі; попередній WinError 5 guard не обходити. Stable Release заблоковано до реальних P0-перевірок.

**Created and developed with AI assistance (OpenAI Codex).**
