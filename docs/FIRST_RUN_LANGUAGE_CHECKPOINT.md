# First-run language return point — 2026-10-29

**Created and developed with AI assistance (OpenAI Codex).**

Verified release source: `780f22642a658ddb4f8fee5bc9ccbdc795fcf3c0`. Windows CI **40827117958 SUCCESS**: https://github.com/PanTenolN/PTenoLauncher/actions/runs/40827117958 . Windows job 123292583715 and publication job 123293630847 both succeeded. The full suite passed **344/344 tests**, zero failed/errors/skips/not-run; Release build zero errors/warnings. Both ZIP CRC checks, required source/progress files, requirement guard and freshly extracted Portable startup passed (initialized=True, gameLaunched=false).

Actual prerelease: https://github.com/PanTenolN/PTenoLauncher/releases/tag/v0.2.0-preview.780f22642a65

| Asset | Bytes | SHA-256 |
|---|---:|---|
| PTenoLauncher-Portable-0.2.0-win-x64.zip | 50219673 | 5f3d59a5b50f3063a40633bed1ab3f7a7d3973b4d97050e39e858b571da65e30 |
| PTenoLauncher-Setup-0.2.0-win-x64.exe | 37441248 | 907e702d726d934f589892d2e17a2d161a7124439f44697e22a1793a993d552d |
| PTenoLauncher-Source-0.2.0.zip | 770485 | f012c0560e6bf87460022c2f8048e81339bd55409eaf4eef7a6afd88abbae365 |
| SHA256SUMS.txt | 410 | 61464501849d0ad4716396af37c77bd222afba69a2219724a13c61a203298606 |

The first normal launcher startup opens an eight-language menu when ui.language is missing/unsupported. Native names, immediate preview, explicit Continue persistence in SQLite, supported saved-preference skipping (including case normalization), safe exit and write-failure retry are implemented. The hidden Settings picker does not auto-save previews. The existing profile/world/theme/account settings and authentication/license/readiness gates remain. No internet, AI or Microsoft sign-in is needed to choose a language. See FIRST_RUN_LANGUAGE.md.

Fourteen new actual-XAML/SQLite/offline/restart/preservation/error-retry cases passed on Windows, alongside all previous tests. Catalogs contain 238 keys per language. All 184 original requirements and four previous additions remain; UI-FIRSTLANG makes 189 current requirement rows, with 44 graded modules. The first-run module is 75% on the owner's scale pending interactive native/DPI review; automated functional behavior is verified. Sinytra/Kilt/all listed cross-loader bridges stay DEFERRED.

Local environment: the former laptop workspace is unavailable; this session used a Linux clone of main 2bd009db0c1c0778a42845553fa532c63dafeb7b. The .NET 10.0.403 SDK was downloaded from official metadata and SHA-512 verified into the workspace. Linux compilation passes with zero errors/warnings; 17 catalog/picker tests pass. The broader Linux filter had 23 passes / 15 Windows-DPAPI-dependent startup failures, retained as failed/platform-limited evidence rather than a passing Linux startup test. Windows CI provides the actual complete 344-case verification. DPAPI was not weakened. Local logs/TRX are under artifacts/0.2.0. No Windows laptop files were changed in this session; source and final documentation are saved in GitHub. On reconnection compare branches/diffs and preserve this newer source rather than overwriting it from an old laptop snapshot.

The unattended --smoke-test deliberately skips the modal welcome prompt and does not persist a language. Normal startup uses the chooser. Interactive native first-menu/DPI review is not claimed by the unattended startup. Documentation-only commits recording the final CI result do not change binary source; reports inside the Source ZIP may predate this final result.

Still unverified: Microsoft approval/entitlement/profile/refresh, actual licensed Vanilla/Fabric/Quilt gameplay and disposable-world saves/reopening, real-game stop, Java 8/17/25 live matrix, historical virtual/mapped assets, live OpenAI quality, translation-pack activation and current installer install/upgrade/reinstall/uninstall lifecycle. Binaries remain unsigned; stable release remains blocked by real P0 acceptance.

Next independent P0 block: Java matrix, managed-runtime receipt/path/architecture integrity and historical asset/launch arguments. Preserve the architecture, language preferences, profiles, 189 requirements and deferred scope. On Microsoft approval use HOME_TEST_CHECKLIST.md with disposable worlds. Do not bypass authentication, licensing or the earlier blocked installer registry ownership guard.
