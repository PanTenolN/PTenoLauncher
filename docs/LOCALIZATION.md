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


## Localization and sign-in diagnostics - 2026-10-28

Development resumed at the owner's request. Added 94 Ukrainian/English catalog messages, localized ViewModel actions/statuses, picker/dialog titles, snapshots, profile/mod counts and major readiness causes. Language switching refreshes computed labels and persisted language is explicitly applied during initialization. Original user data and game console content are not translated. Authentication failures now include a fixed enum stage; no response bodies, tokens or full URLs are logged. See docs/MICROSOFT_REVIEW_FOLLOWUP.md.

Local build/test evidence is recorded in artifacts/0.2.0/localization-final-build.log, localization-tests.log and tests/localization.trx. The previous source checkpoint's Windows runner and prerelease publication both succeeded (run 40728874523, commit 92998deeb0907798a02fef7404f98c5a1e58c0a7). Actual Setup, Portable, Source ZIP and SHA256 sums are public; current localization changes require their own new build. Current installer lifecycle/gameplay remain unverified.

Localization is still PARTIAL: low-level mod/security diagnostics, previously formatted dynamic messages after a live language change, theme names and exhaustive linguistic/accessibility review remain. UI-07/UI-08 must not be promoted to complete. No additional language is claimed. All 184 requirements are preserved. Microsoft review submission is owner-reported; approval remains pending. A scoped connected-mail search for PTenoLauncher or its exact Client ID returned no messages; this does not prove that no approval email exists.

