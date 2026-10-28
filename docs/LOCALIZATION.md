# Interface localization

The current selectable languages are **Ukrainian (uk)** and **English (en)**. Both are partially implemented, not complete translations. The language selector is in Settings / Appearance. Changes apply to Avalonia DynamicResource labels immediately and the selected code can be saved to SQLite.

Implementation: `src/PTenoLauncher.Desktop/Localization/TextCatalog.cs`, `MainViewModel.Foundation.cs`, `MainWindow.axaml` and dialog resource bindings. Existing tests cover runtime preference switching; they do not prove full translation coverage.

Remaining work for Ukrainian and English:

- Move remaining hard-coded labels, dialogs, profile summaries and ViewModel statuses to the catalog.
- Introduce localized presentation for infrastructure diagnostics while keeping stable machine-readable diagnostic codes and original logs.
- Refresh computed labels and status summaries when the selected language changes.
- Verify save/restart behavior, placeholders, plural forms, date/number formats, truncation, access keys and accessibility labels in each language.
- Add completeness and actual-XAML regression tests; perform manual review in both languages.

English coverage is required before calling localization complete. Additional languages should be registered only with real reviewed translations, a defined fallback and equivalent checks; an entry in a selector does not establish support. Further languages have not yet been selected by the owner. This work is independent of optional online AI translation of mod content.
