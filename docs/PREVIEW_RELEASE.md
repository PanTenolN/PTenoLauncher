**Created and developed with AI assistance (OpenAI Codex).**

This is a Windows 11 x64 development preview, not a stable release.

The build is published only after the Windows runner completes locked dependency restore, Release build, all automatic tests, self-contained publication, Inno Setup compilation, archive/hash checks and an isolated Portable startup smoke test. See the workflow run and its TRX/verification artifacts for the exact results. These checks do not establish Minecraft gameplay or installer lifecycle correctness.

- **Setup EXE:** installs the self-contained launcher for the current user. The current installer lifecycle/upgrade/uninstall and interactive Windows 11 checks remain unverified. Prior local lifecycle evidence applies to an older payload.
- **Portable ZIP:** extract all files together, then start PTenoLauncher.exe. No separately installed .NET runtime is required. Keep portable-data private.
- **Source ZIP:** application source, tests, build scripts and documentation.
- **SHA256SUMS.txt:** hashes for the generated packages and executable.

Known limits:

- Microsoft browser OAuth has completed in the owner's test, but the subsequent Minecraft Services request returned HTTP 403. The owner reports submitting the AppID review request; approval remains pending. Full licensed sign-in, refresh, entitlement/profile retrieval and actual online/offline Minecraft gameplay have not been verified.
- Application and installer are unsigned. The pipeline does not test signing or circumvent Windows security prompts.
- Ukrainian and English are selectable; localization is partial. Some diagnostics remain Ukrainian or English regardless of selection. Other languages are not yet provided.
- Forge/NeoForge native mod metadata is partially supported; their loader installation and full nested-mod resolution remain incomplete.
- An AI assistant/translator, server manager, modpacks and several other roadmap features are unfinished. AI is optional and off by default; no local AI model is included.
- NuGet auditing is enabled in the build. Inspect the individual run for restore/audit failures or warnings; a passing build is not a claim of absolute security.

Minecraft binaries, Microsoft tokens, user worlds and private API keys are not included. This is an unofficial community project, not an official Minecraft product and not approved by or associated with Mojang or Microsoft.
