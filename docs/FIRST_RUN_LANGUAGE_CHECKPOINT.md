# First-run language return point — 2026-10-29

**Created and developed with AI assistance (OpenAI Codex).**

Resumed from main `2bd009db0c1c0778a42845553fa532c63dafeb7b`; previous binary source `827e8832a95baf5d2c9f805fb3c43f563427c90b`, verified Windows CI `40773211706`, 330 tests. The existing repository was cloned into the available Linux workspace; the previous laptop environment is unavailable. No project recreation or architecture replacement occurred.

New code adds a first-normal-startup eight-language menu, immediate preview, explicit SQLite persistence, saved-preference skipping, invalid-preference handling and write-failure retry. Closing without confirmation does not save. Microsoft authentication/license/readiness gates, AI optionality and user data are preserved. Details: FIRST_RUN_LANGUAGE.md.

Local .NET 10.0.403 SDK was downloaded from official release metadata and SHA-512 verified within the workspace. Release build: zero errors/warnings. Seventeen catalog/picker cases pass. The broader 38-case Linux run fails 15 Windows-DPAPI-dependent startup cases; it is retained as failed/platform-limited evidence, not promoted to PASS. Fourteen new Windows UI/persistence cases require the complete Windows runner. See artifacts/0.2.0/first-run-language-final-build.log, first-run-catalogs-linux-tests.log, first-run-language-linux-tests.log and tests/*.trx.

This source needs its own Windows build/full test/package/prerelease verification. Until a verified result is recorded below, use the previous known binary preview: https://github.com/PanTenolN/PTenoLauncher/releases/tag/v0.2.0-preview.827e8832a95b . A source ZIP can predate documentation-only follow-ups recording its CI result.

All 184 original requirement IDs and the four earlier additions remain. UI-FIRSTLANG adds explicit tracking for this request: 189 current requirements, 44 graded modules. Sinytra/Kilt/cross-loader bridges remain DEFERRED. No stable release, licensed gameplay, live AI/provider quality, native interactive language-menu/DPI review, Java 8/17/25 matrix or current installer lifecycle is claimed.

Next independent P0 work remains Java 8/17/21/25 runtime verification, managed-runtime receipt/path integrity and historical asset/launch arguments. Await the owner's Microsoft review decision for licensed Vanilla/Fabric/Quilt gameplay. Do not bypass authentication or the blocked installer registry ownership guard. Continue the preserved roadmap rather than recreating the project.
