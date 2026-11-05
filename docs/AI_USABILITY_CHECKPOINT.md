# AI usability — verified local workflow, external checks pending

> **Current continuation 2026-11-02:** [MINECRAFT_LAUNCHER_IMPORT_CHECKPOINT.md](MINECRAFT_LAUNCHER_IMPORT_CHECKPOINT.md) records the verified AI manual-test additions, separate Minecraft Launcher adapter, 947/945/two-not-run regression and exact cleanup. The dated source/evidence below is historical, not a missing-feature claim. UX-01–UX-06 matrix and retained hand-test build: [HEX_TRANSLATION_CHECKPOINT.md](HEX_TRANSLATION_CHECKPOINT.md).

## Current local AI usability / test-data lifecycle — 2026-10-30

Current source Release **0 errors / 0 warnings** (`artifacts\0.2.0\ai-verified-build-20261030-110500.log`). NEW full regression: **828 total / 826 executed and passed / two explicit junction cases NOT RUN / zero failures/errors** (`artifacts\0.2.0\tests\ai-verified-all-20261030-110650.trx`). The earlier 787/785 and paused 34/36 outcomes are historical. A targeted 76/76 pass preceded final ownership/recovery/closing refinements; the new full run covers those final refinements. Three fresh native Windows UI probes passed (connection, assistant, saved library at 980×680), with screenshots inspected; fixtures cover eight locale catalogs, placeholders, minimum/default logical sizes, Tab focus, copy/cancel/clear and offline workflows. Current host DPI only; other physical Windows scaling and comprehensive manual accessibility/linguistic review remain unverified.

Fixed canonical legacy/directional generated-pack ownership, integrity receipts/backup/pending-publication recovery; real non-English resources; initial profile-selection busy race; library selection/row loss on refresh; hidden library operations blocking Vanilla/Forge; unchanged-row review; unsaved/stale/close guards; optional model-catalog permissions and separately proven Responses state. Eight-language connection/chat/library/editor/pack UI is available. User guide: **docs/AI_USER_GUIDE.md**; permissions: **docs/OPENAI_PERMISSIONS.md**.

Owned test fixtures and executable runner probes now track roots, budgets, handles/process identities and compact evidence; the full updater harness has finally cleanup and evidence retention. Success/failure/cancel/repeated/abandoned-root recovery, changed ownership, busy files, missing-marker/newer-bytes refusal and budget disposal are tested. **The full four-build updater E2E was NOT rerun**; executable lifecycle probes exercise cleanup only. Its 2026-10-29 three-scenario result remains historical.

Cleanup measured across 2021 operation roots: **726.50 MiB created/measured before disposal, 726.41 MiB removed; 86209 bytes retained in 1 exceptional old native-probe root(s)**, plus deliberately retained compact evidence. Exact paths/reasons: **artifacts/0.2.0/ai-ux-cleanup-report-20261030.json**. New native runs completed cleanly. No claim of zero leftovers. No broad historical cleanup, SDK/dependency/product-cache removal, or user-process termination occurred.

Live paid AI/model quality, real user keys, Minecraft pack activation/gameplay, new vulnerability audit, production update key/feed, final self-contained distribution and registry-guarded Installed lifecycle remain NOT RUN/blocked. Modern outer-JAR flat string JSON/eight locales only; legacy/nested/component formats remain unsupported. Exact pack catalog is deliberately narrow; unknown versions need verified installed client metadata or an explicit advanced override. Legacy generated packs lacking integrity proof are preserved/refused for automatic overwrite.

All **189 requirements / 184 original IDs / 44 modules / five DEFERRED Sinytra/Kilt/Cross-Loader rows** and existing feature grades are preserved. Current Prism/MultiMC source was re-read and preserved; it still lacks the complete metadata/transaction/UI adapter. Next independent block: finish Prism/MultiMC, then separate Minecraft Launcher and Modrinth/CurseForge App imports, then license-aware export/comparison. Always carry **docs/CONTINUATION_RULES.md** into the next continuation prompt. No push/release/signing or paid API occurred.

### Failed/intermediate evidence retained

- Initial restore through the old child-process wrapper returned exit 1 without text; diagnostic restore showed MSBuild parallel graph failure. Single-node local-package restore succeeded with BuildInParallel=false/-m:1. NuGetAudit=false applied only to this explicit offline restore invocation; committed audit settings are unchanged and no fresh audit is claimed.
- Initial resumed scoped run: 58/59; the generated-pack proof was incorrectly enumerated as a draft. Proofs moved outside the draft namespace directory.
- Later 69/70 and 74/75 scoped outcomes exposed selection clearing during a library refresh. Rebinding now cannot turn internal collection rebuilding into a user deselection; original assertions remain.
- First full run: 828 total, 825 passed, one Forge UI failure, two explicit not run (`ai-verified-all-20261030-105525`). Hidden translation UI was starting an operation on unrelated profile switches. Reading is now limited to the active library/navigation.
- Native repeat `ai-verified-native-20261030-104542` FAILED during cleanup. A nonempty root later lacked its internal marker, so recovery refuses newer bytes. The primary exception was obscured by Dispose in that earlier run; its exact initial cause is not proven. New probes record primary failures separately, register launched PID/start-time/executable before further inspection, reject cleanup with a running recorded child, and keep/restore the marker through partial deletion. This failed evidence was not rewritten as success.
- The following 828/826 full pass at 10:59 preceded final close/budget safeguards; the newest full run above verifies the final code.

### Important correction to the old checkpoint

The old 36-case checkpoint called its UI failure an empty row collection at line 50. The original assertion there was an empty **SourceLanguages** collection: an initial hidden profile-selection operation made RunAsync skip JAR discovery as busy. The later empty-editor-row refresh regression was separate, newly exercised by unchanged-row acceptance. Both now have meaningful UI coverage.

## Historical paused checkpoint (superseded above)

# AI usability — PAUSED at explicit owner request — 2026-10-30

Continued the existing source, with AI assistance (OpenAI Codex). This block is unfinished. Do not restore an older snapshot over current edits. No remote push, release, production signing, installer lifecycle, live AI request or user-account/key/world test was performed. No build/test/update/import process from this block remains active.

## Baseline newly verified before editing

The previously unbuilt Prism/MultiMC `InstanceSource.cs` and shared ZIP eligibility refactor were preserved and compiled. NEW baseline Release: **0 errors / 0 warnings**, `artifacts/0.2.0/ai-ux-baseline-build.log`. NEW baseline full suite: **787 total / 785 executed and passed / zero failures/errors / two explicit junction cases NOT RUN**, 111.953 seconds, `ai-ux-baseline-tests.log`, `tests/ai-ux-baseline.trx`. This verifies compilation/regressions of the starting source, not completion of Prism import and not the AI changes below. Pre-AI snapshot: `.local/checkpoints/pre-ai-usability-1793352324`. No applicable AGENTS.md or .git was found.

## Implemented source, not yet a verified completed workflow

- `AI/OpenAiClient.cs`: typed safe HTTP errors without provider bodies/key disclosure; explicit-consent model-catalog GET; bounded real session-context requests (12 messages / 32,000 characters including current request, store=false, no tools); actual source-locale translation instructions. Existing direct-request API retained.
- `Security/SecretVault.cs`: presence check without decrypting. `LauncherServices.cs`: optional injected AI transport/key-reader for isolated tests; normal construction still uses the owner's protected key and fixed endpoint.
- `AI/TranslationWorkspace.cs`, `TranslationLibrary.cs`: source/target scoped `translations/v2/<source>/<target>/<namespace>.json`, actual source resources, no silent language fallback, source provenance hashes, legacy en_us migration with byte-exact `.legacy-v1-backup` and retained original; manual/incremental values and review flags; saved library listing; stale editor fingerprint and transfer fingerprint; direction-specific generated pack paths.
- `TranslationPackMetadata.cs`: exact published 1.20→15 and 1.21→34 entries with official release-note links; otherwise hash-verified matching client/version.json resource format. No version-prefix inference. Unknown versions remain blocked; advanced manual format remains available. No installed game or gameplay claim.
- `MainViewModel.Ai.cs`, `MainWindow.axaml/.cs`: separate connection card, key actions/states/consent/model selection; simple bounded in-memory chat, redacted preview, wait/cancel/copy/clear/examples; actual language selectors and per-file missing-language findings; profile/library/search/pending filter/table/manual save/discard; translation preview/consent and offline pack/folder actions. Unsaved editor guards; advanced JSON/model/format fields. Initialization's library failure is caught so corrupt translation data does not block core startup.
- `TextCatalog.AiUsability.cs`, six additional locale JSONs and `scripts/localize-ai-usability.py`: **77 new message definitions** across eight languages. Final catalog/UI parity still needs verification. Do not claim linguistic/DPI/keyboard/native review yet.
- `AiUsabilityTests.cs`, `AiUsabilityUiTests.cs`: isolated HTTP fixtures and actual Avalonia workflows. Existing `OnlineAiTests.cs` HTTP-error assertions were changed to require the new safe typed limit/provider codes; they still test no provider-body disclosure and checkpoint recovery.

## Actual outcomes — keep failures visible

1. `ai-direction-core-build.log`: **0 errors / 0 warnings** before UI connection.
2. `ai-usability-initial-build.log`: FAILED, generated control name collided with the event handler. Handler renamed.
3. `ai-usability-controls-build.log`: FAILED, missing VerifyAsync size, missing Avalonia clipboard extension namespace; one captured-parameter warning. Fixes applied.
4. `ai-usability-first-build.log`: build succeeded with **one obsolete Watermark warning**, no errors. The old-only AI run was **13/15 passed, two failures** because tests expected the former exception type/raw HTTP number. Those tests now assert the explicit safe code contract. Failed log/TRX retained.
5. `ai-usability-fixtures-build.log`: build succeeded, **0 errors / one obsolete Watermark warning**. `ai-usability-first-tests.log` / `tests/ai-usability-first.trx`: **36 executed / 34 passed / two failed**, no errors/skips. This is not a full regression run.

Remaining failures from that run:

- `AiUsabilityTests.OfflineManualExportSeparatesDirectionsAndStaleEditorCannotOverwriteNewerBytes`: the existing SQLite artifact ownership validator rejects the new source-to-target generated-pack filename (`Translation resource pack is outside its profile.`). Do not bypass/weaken ownership or ignore registration failure. Define an exact bounded filename/locale contract and update all relevant ownership/deletion checks with tests.
- `AiUsabilityUiTests.NonEnglishSourceLibraryTableAndAutomaticPackWorkOfflineWithoutAiKey`: line 50 expected a populated translation row collection after import; the actual collection was empty. Diagnose real binding/library/profile-selection sequencing; do not merely change expected results. Separate it from the known export registration failure.

After that test build started, `.local/finish-ai-controls.py` updated XAML to `PlaceholderText` and decoupled `SelectedAiModel` from manual model ID. **These final XAML edits have not been built/tested**. Current source therefore has no final verification. Two earlier inline Python shell invocations failed because of quoting; file-based scripts were subsequently used. Logs remain retained. No existing failure is recast as successful.

## Resume order and unresolved review

Resume only on the owner's next explicit instruction. First re-read current files, build and fix the two meaningful failures. Preserve current Prism edits. Run appropriate AI/translation tests, full regression and Release build after fixes; retain failed evidence.

Review before declaring the AI UX block complete:

- Model list/manual selection notifications; actual waiting/cancellation/copy; no key reads or requests on save/initialization; all source/target/selection/model consent invalidation paths. Source=target must never read a key or issue a paid request.
- Draft migration validation, concurrency/cancellation/backup, unsaved edits, library load/selection and reopening without JARs. A manual translation needing review currently has no explicit accept-unchanged-row action; implement a deliberate review action rather than silently marking it verified.
- Generated pack filename/ownership contract, previous-pack backup and rejection of altered/unowned generated files; unknown/new-style version metadata must fail with a localized blocker. Exact 1.20/1.21 catalog is deliberately narrow; do not infer 1.21.1 from 1.21. Verified installed client metadata is a separate path.
- Actual eight-language placeholder/control checks; native screenshots at supported logical sizes; keyboard/contrast/long strings and Windows DPI checks. Not yet performed. Live AI quality/payment and activation in Minecraft remain separate explicit-consent/authorized checks.
- Do not claim legacy .lang, nested or component-valued language JSON support. Static/modpack/unrelated core regressions require a fresh full run after these edits.

Once available AI UX work is verified, resume unfinished Prism/MultiMC metadata/transaction/UI; then separate Minecraft Launcher adapter (remaining MODPACKS-03), Modrinth App/CurseForge App (MODPACKS-04), then license-aware export/friend comparison. These are not completed by the new AI UI.

All **189 requirements / 184 original IDs / 44 modules / five DEFERRED Sinytra/Kilt/Cross-Loader rows** are retained. No requirement/module grade is raised for unfinished classes/UI or fixtures. Core must remain AI-independent; preserve auth/licensing gates, eight languages/onboarding, profiles/worlds/caches/translations/backups and installer registry guard.
