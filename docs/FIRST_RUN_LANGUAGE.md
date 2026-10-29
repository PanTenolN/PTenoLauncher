# First-run language selection

On a normal startup with no supported `ui.language` preference, PTenoLauncher opens a modal language menu before the launcher can be used. It lists all eight languages by their native names: Ukrainian, English, Polish, German, Spanish, French, Brazilian Portuguese and Russian. Choosing a row previews the translated interface immediately. Continue persists the canonical language code in the existing SQLite settings store.

The hidden Settings picker does not auto-save welcome-menu previews. Exit, Escape or closing the dialog before confirming leaves the preference unchanged and exits the normal startup; the menu appears again next time. A failed write keeps the dialog open with a translated error and supports retry. Closing is blocked while the short save operation is in progress.

A supported saved preference skips the welcome menu, including existing installations; matching is case-insensitive and the UI uses the canonical language code. Unsupported/empty preferences require selection. Profiles, saves, themes, accounts and other settings are retained. Settings / Appearance still allows changing the language later. This feature requires neither Microsoft sign-in nor an AI provider or internet access.

The unattended `--smoke-test` path explicitly skips interactive onboarding and does not write a language preference. A normal Portable or installed startup uses the menu. This skip controls a UI prompt only; authentication/readiness gates remain unchanged.

## Implementation and verification

- `App.axaml.cs` calls the normal-startup language gate after successful initialization. Initialization failures remain visible in the main window.
- `MainWindow.EnsureLanguageSelectedAsync` owns the modal dialog; `LanguageSelectionDialog.axaml` and its code-behind provide preview, explicit confirmation, error/retry and exit.
- `MainViewModel.Onboarding.cs`, existing language initialization and SaveLanguageAsync use one existing SQLite preference, with no schema migration or separate completion flag.
- Five new strings are present in all eight complete UI catalogs: 238 keys per language. Overall technical diagnostics/localization and exhaustive DPI/native-speaker review remain partial.
- `LanguageOnboardingTests` adds 14 cases: eight actual-XAML/offline-preview/SQLite/restart scenarios, cancel/retry, four existing/invalid/case-normalization scenarios with synthetic-save preservation, and a real SQLite write-failure/retry case. They do not launch Minecraft or call authentication/AI services.

Linux Release compilation succeeds with zero errors/warnings. The 17 catalog/language-picker cases pass locally. A broader 38-case Linux UI run had 23 passes and 15 failures because full startup invokes the existing Windows-only DPAPI guard (14 onboarding cases and one existing localization case); it is not a passing Windows-startup test. DPAPI was not weakened or replaced. The full Windows suite and release results are recorded in `FIRST_RUN_LANGUAGE_CHECKPOINT.md` after verification.

Manual acceptance: start a fresh disposable Portable folder without previous portable-data, select a language, continue and restart. The menu must not repeat. Verify a copy with an existing supported preference skips the prompt. Do not erase real user data to force the menu. Interactive native desktop/DPI review remains unverified until observed.

## Verified Windows result — 2026-10-29

Windows CI 40827117958 passed all 344 tests, including the 14 onboarding cases, with zero build errors/warnings. Source 780f22642a658ddb4f8fee5bc9ccbdc795fcf3c0 is actually published at https://github.com/PanTenolN/PTenoLauncher/releases/tag/v0.2.0-preview.780f22642a65 . Package integrity and extracted Portable startup also passed; see FIRST_RUN_LANGUAGE_CHECKPOINT.md for exact asset hashes. Interactive native/DPI review and Minecraft gameplay remain unverified.
