# PTenoLauncher 0.2.0 Development Preview

**Створено та розробляється за допомогою ШІ (OpenAI Codex).**
**Created and developed with AI assistance (OpenAI Codex).**

This preview fixes cancellation after a game process starts, cleans up owned subprocesses after cancellation or console errors, and bounds inherited-console draining. Home and Game console now expose Stop Minecraft with a default-cancel dialog, explicit acknowledgement of possible unsaved-world loss and a stale-launch confirmation guard. Prefer closing the game through its menu.

Retains eight UI languages, profiles/themes/SQLite, Vanilla/Fabric/Quilt preparation, verified Java downloads, deterministic Modrinth/local-mod dependencies, safe profile deletion/backups and optional OpenAI assistant/translation workflows. Core features remain independent of AI. The new progress report grades 43 modules and retains all 184 original requirements plus four explicit additions; Sinytra, Kilt and cross-loader bridges are deferred.

Local Release build: zero errors/warnings; 330 tests passed. A real existing Java 21 x64 process returned the expected version/error exit codes with no network or game launch. Each published preview is gated by its own Windows build/tests/installer compilation/ZIP verification and extracted Portable startup.

Microsoft application approval and full entitlement/profile/refresh, licensed Minecraft gameplay/world saves and real-game stopping remain unverified. Live paid OpenAI requests/translation quality, translation-pack activation, Java 8/17/25 matrix and the latest installer installation/upgrade/uninstall lifecycle also remain unverified. Technical localization and some loader/legacy formats are partial. Binaries are unsigned. This is not a stable release.

Artifacts: self-contained Windows x64 Setup EXE, Portable ZIP, source ZIP and SHA256SUMS. Do not include Minecraft binaries, worlds or credentials. Installer lifecycle is not established by compiling an EXE.

See PROJECT_PROGRESS_SUMMARY.md, docs/PROCESS_LIFECYCLE.md, docs/PROCESS_LIFECYCLE_RELEASE_CHECKPOINT.md and docs/HOME_TEST_CHECKLIST.md for evidence, limits and continuation.
