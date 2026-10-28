# Interface localization

Selectable languages: Ukrainian (uk), English (en), Polish (pl), German (de), Spanish (es), French (fr), Brazilian Portuguese (pt-BR) and Russian (ru). All eight current UI catalogs contain **230 keys**, including optional AI controls and major readiness messages.

Six added JSON catalogs are embedded in the application under src/PTenoLauncher.Desktop/Localization/Locales. No internet, runtime AI, API key or external translation service is needed to display them. Ukrainian/English defaults and saved language codes are preserved. Switching applies Avalonia DynamicResource values immediately; SQLite stores the selected code. Unknown codes safely fall back to Ukrainian.

TextCatalog.Locales validates complete nonempty keys and composite-format placeholders. AdditionalLanguagesTests checks all eight catalogs, rejects missing/broken translations, drives the actual XAML picker and SQLite reload, and preserves original console data. Mod source texts and user names/files are not implicitly translated when the interface language changes.

Known limits: low-level mod/security diagnostics remain in their original language; some formatted status strings do not re-render on a live switch; theme enumeration names, accessibility/DPI/long-text review and native-speaker linguistic QA remain pending. Full application-wide localization is still partial. Optional AI mod-content translation is a separate feature described in ONLINE_AI.md.
