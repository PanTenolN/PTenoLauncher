# PTenoLauncher 0.2.2 Development Preview

Created and developed with AI assistance (OpenAI Codex).

- Local Forge servers prepared for Minecraft 1.21.1 / Forge 52.1.0 start with 64-bit Java 21. The launcher verifies the prepared files before starting the server, including when reopening an existing server card.
- The server form selects the supported Forge version from the official catalog or a usable cache. Startup avoids reapplying the same language to the open window.
- Installed mods always appear alphabetically by display name, including after search, refresh and installation. Download Mods keeps the installation cart, and local JAR imports open a review before installation.
- Windows x64 Setup and Portable include .NET. Extract the entire Portable ZIP and double-click `PTenoLauncher.exe`. Portable uses `portable-data` next to the executable. The installed launcher continues to use `%LOCALAPPDATA%\PTenoLauncher`.
- The launcher detects loss and recovery of internet access and switches automatically between offline and online operation. A catalog outage by itself does not switch the launcher offline.
- Profile tabs reuse cached lists and update when files change, so switching tabs does not wait for another full scan.
- Mark a verified game session as working to save the exact Minecraft and loader versions, mod hashes and enabled states. Preview the changes before confirming a restore. Verified local files allow offline restoration. Missing files require the exact matching bytes.

Windows CI verifies the full suite, the managed Java matrix, package checksums and startup from a fresh extraction of the self-contained Portable ZIP. Licensed Minecraft gameplay and launching it offline with a real account remain manual checks. This unsigned development prerelease is available for manual download.
