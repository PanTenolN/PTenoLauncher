# PTenoLauncher

**Створено та розробляється за допомогою ШІ (OpenAI Codex).**
**Created and developed with AI assistance (OpenAI Codex).** Requirements and development direction are set by the project owner.

PTenoLauncher is an independent Minecraft Java Edition launcher project for Windows 11 x64, built with C#, .NET 10 and Avalonia UI. The current local implementation is **0.2.0 Development Preview**. This public project page describes work in progress; a production-ready release is not yet available.

## Purpose

Manage isolated Minecraft profiles, prepare official Minecraft installations, manage local and catalog mods, and support offline use when the required files and a previously verified licensed account are available.

## Microsoft / Minecraft authentication

The project uses its owner's registered desktop public-client application and MSAL to sign in through the system browser. The intended official flow continues through Xbox/XSTS and Minecraft Services, checks Java Edition ownership and retrieves the Minecraft profile. Minecraft access depends on the application's permitted service access and a licensed user account.

The launcher does not collect Microsoft passwords, use borrowed application IDs, or offer an authentication/license bypass. Windows account tokens are stored locally using DPAPI CurrentUser protection. No client secret is embedded in the desktop application.

Browser OAuth has been observed working, but the current downstream Minecraft Services request returns HTTP 403. Full sign-in, token refresh and actual online/offline Minecraft gameplay have **not** been verified. This is not a claim of Microsoft or Mojang approval.

## Current local implementation

- Ukrainian/English interface, graphite styling, live themes and SQLite settings.
- Isolated profiles, confirmed deletion safeguards and backups.
- Official Minecraft release/snapshot catalog, Vanilla/Fabric/Quilt installation preparation, managed Java and verified local download caches.
- Modrinth integration and deterministic dependency resolution independent of AI; local JAR inspection and transactional mod installation.
- Static Fabric/Quilt/Forge/NeoForge metadata checks and installed-file integrity checks. Native Forge/NeoForge loader installation and complete nested-mod resolution remain pending.

The latest local source build passed 281 automatic test cases with zero compilation errors/warnings. Many tests use synthetic fixtures; these results do not establish real gameplay, complete authentication, installer readiness or absolute security. Native NuGet vulnerability-audit connectivity remains unresolved.

The project source and development artifacts are currently maintained locally; this repository initially serves as the public project overview. Public source/release publication is a separate step.

## AI disclosure

AI assistance in development is separate from optional AI features inside the launcher. The runtime AI switch defaults to off. An online AI assistant/translator and local AI models are not included in the current implementation. Core launcher and mod-management functionality does not depend on an AI provider.

## Project status

Source review, official service access, licensed game tests, loader coverage and Windows distribution verification are ongoing. Minecraft game files and user credentials are not distributed through this repository.

This is an unofficial community project, not an official Minecraft product and not approved by or associated with Mojang or Microsoft.
