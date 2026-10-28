# Online AI and eight-language release checkpoint — 2026-10-28

**Created and developed with AI assistance (OpenAI Codex).**

Verified release source: aec1846517865a0953b1037bd6b55f8ce8807efc. The final Windows workflow and publication jobs succeeded: https://github.com/PanTenolN/PTenoLauncher/actions/runs/40759155278 . Logs confirm 320/320 tests passed, zero errors/warnings in the Release build, ZIP CRC checks and a freshly extracted Portable startup with initialized=True. gameLaunched=false.

Actual prerelease: https://github.com/PanTenolN/PTenoLauncher/releases/tag/v0.2.0-preview.aec184651786 . Assets read back: Setup EXE 37417300 bytes; Portable ZIP 50159773 bytes; Source ZIP 713366 bytes; SHA256SUMS.txt 410 bytes. This supersedes earlier notes awaiting the final CI result. Documentation-only commits after the release do not change its source payload.

The release contains eight UI languages with 230 validated keys each, optional OpenAI assistant/selected-log analysis, protected user API keys, explicit paid-transfer consent, modern mod-language JAR import, selected/bulk translation, atomic incremental recovery, manual editing and resource-pack export. See ONLINE_AI.md for setup and exact limits.

No paid live OpenAI request or real API key was used during development. Live API/model quality, Minecraft gameplay/resource-pack activation, Microsoft application approval and current installer installation/upgrade/uninstall lifecycle remain unverified. Binaries are unsigned. The mod-language workflow supports modern outer-JAR en_us JSON; legacy/nested formats and automatic pack_format discovery remain unsupported. Low-level diagnostic localization and native-speaker/DPI review remain partial.

The last local code/test state also passed 320 tests. A later attempt to append this final release evidence locally was blocked before process creation by an execution-environment sandbox path configuration error; this remote checkpoint preserves the verified result. On reconnect, retain the eight-language/AI code already saved locally, merge this documentation-only checkpoint, and inspect current environment access before starting more work. Do not recreate the project or retry a blocked installer ownership guard.
