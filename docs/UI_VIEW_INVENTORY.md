# Наявні UI views — 2026-11-02

На запит власника перевірено XAML у поточному Desktop source. Редизайн/AI UX змінюють існуючий `MainWindow.axaml`; паралельних old/new main windows не створено. Видалення невикористовуваних screens у цьому блоці не виконувалося: усі дев’ять знайдених XAML мають поточне призначення/посилання.

| XAML | Поточне використання |
|---|---|
| App.axaml | Завантаження application resources/Fluent/styles |
| MainWindow.axaml | Єдине головне вікно, основні сторінки й AI/translation controls |
| Themes/Graphite.axaml | Поточні централізовані theme resources |
| Dialogs/ProfileEditor.axaml | Створення/редагування профілю |
| Dialogs/DeleteProfileDialog.axaml | Preview/підтвердження видалення профілю |
| Dialogs/LanguageSelectionDialog.axaml | Перший вибір мови |
| Dialogs/LauncherUpdateDialog.axaml | Явна пропозиція/прогрес оновлення |
| Dialogs/ModBaselineDialog.axaml | Локальний mod integrity review |
| Dialogs/StopGameDialog.axaml | Підтвердження керованого завершення гри |

JSON editor та ручні технічні опції збережені в «Додатково» за вимогами; це потрібний advanced workflow, а не дубль старого UI. Наявність файла в bin/obj не доводить окремого невикористовуваного інтерфейсу. SDK/dependencies/current binaries/user data та позначена ручна збірка не видаляються для такого cleanup. Основний попередній надлишок диска був у disposable E2E payloads; актуальне очищення контролюється [CONTINUATION_RULES.md](CONTINUATION_RULES.md).
