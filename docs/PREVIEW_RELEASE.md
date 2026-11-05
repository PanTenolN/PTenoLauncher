# PTenoLauncher 0.2.1 Development Preview

Created and developed with AI assistance (OpenAI Codex).

- Profile resource packs and shaders can be added from ZIP files. The Modrinth catalog filters by Minecraft version and content type, then verifies the selected ZIP before adding it to the profile. Worlds can be copied or moved to the launcher trash, with an optional backup.
- Creating a profile starts Java x64 preparation. The launcher uses verified existing or cached Java where available. The profile survives an interrupted or unavailable download and offers a retry.
- The Minecraft console renders Log4j messages and exception traces, colors levels, filters warnings and errors, and supports copying the saved transcript. The saved transcript reports its 10 MiB limit. Its historical view is paginated.
- Mod search supports debounced suggestions, exact-version cart selection and automatic compatible optional dependencies. New installations keep readable JAR names and record file hashes and origin.
- New profile folders retain a stable ID. Existing GUID-only folders still open. The move action previews both paths and supports moving back to the legacy path. It blocks when there are path-bound translation data, Java or pending transactions.
- Two .mrpack archives can be compared read-only for Minecraft and loader versions, delivery method, file versions and SHA-512 hashes.

The CurseForge catalog requires approved API access. Importing an existing CurseForge App profile remains a separate local action. Modrinth live testing depends on network availability. Comparison covers supported .mrpack client content. It does not synchronize the profiles. This preview is unsigned and is intended for manual download. A trusted automatic update feed is required for automatic delivery.

Release packages are Windows x64 Setup, Portable and Source. Publication requires the Windows build, full regression suite, managed Java matrix, package checks and fresh Portable startup to pass for the same commit.

### 0.2.1 local regression and packaging evidence

Final local Windows regression on the release source: 990 passed, zero failed, and two junction scenarios reported as NotRunnable. Release compilation completed with zero warnings and errors. The local Setup compiled, and both ZIPs passed SHA-256 and CRC checks. A fresh Portable extraction initialized successfully in an isolated data root. Live Modrinth and an installer lifecycle test are separate from these results.
