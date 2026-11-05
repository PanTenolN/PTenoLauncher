# UI specification checkpoint — 2026-11-03

Compared all 17 sections of the attached specification with Avalonia views, services and the previous checkpoint. This is a partial increment. The existing 189 requirements, 44 modules and five DEFERRED entries are unchanged.

| Spec | Current implementation | Remaining |
|---|---|---|
| 1 Theme | Existing graphite and compact control resources used by new views | Full contrast, density and DPI review |
| 2 Navigation | Existing main navigation and contextual profile menu | Final routing and duplicate removal |
| 3 Home/library | State-based primary action; search, loader/archive filters, name/loader sorting, persisted list/grid and archive/delete overflow | Custom groups and additional sorting; repair state currently follows readiness check |
| 4 New instance | Existing manual/import modes, exact compatible loader selection, preview and cancellation | Complete keyboard/layout review |
| 5.1–5.2 Overview and versions | Contextual status, exact versions, edit and install | Consolidate Java, repair and backup shortcuts |
| 5.3 Installed mods | Compact selectable table, inspected name/version, search/order, picker/drop and offline local workflow | Verified source, compatibility/update columns and guarded row actions |
| 5.4 Catalogue | Profile-bound Modrinth modal, compatible exact versions, cart, optional dependencies, consent and guarded install | Result badges, more filters/sort and clearer dependency labels |
| 5.5–5.10 Content managers | Existing independent features retained | Dedicated profile pages for packs, shaders, worlds, notes, servers and screenshots |
| 5.11–5.12 Logs/settings | Per-profile run output, search/copy/wrap/autoscroll/clear, safe folders and existing editor | Consolidation and removal of duplicate global tabs |
| 6 Global settings | Existing appearance/account/updater/AI/translation functions retained | Final navigation and Java settings |
| 7 Modpacks | Existing .mrpack, plain ZIP, Prism/MultiMC and documented Minecraft Launcher workflows | Remaining adapters, exports and comparison |
| 8 Servers | Existing Servers page retained | Full global and contextual UX |
| 9 AI | Existing key card, chat, offline translation editor and resource pack export retained | Live quality and gameplay verification |
| 10 Dialogs | Existing wizard/import dialogs plus the Modrinth window | Consistency and remaining dialogs |
| 11 Downloads | Existing tracked operations and cancellation | Full queue/history UX |
| 12 States | Catalogue follows offline, busy, consent and error states | Review other empty, blocked and recovery states |
| 13 Accessibility | New controls use existing theme and localized labels | Full keyboard, theme and physical DPI review |
| 14 Locales | New keys in all eight locales | Visual walkthrough in all locales |
| 15 Acceptance | Avalonia control fixture with two profiles; existing import and controlled game process checks | Live Modrinth, authorized Minecraft and physical DPI |
| 16 P0–P2 | Initial library/profile/catalogue increment | Finish 5.3/5.4 and consolidate duplicates before moving on |
| 17 Outcome | Partial delivery | Full acceptance |

The catalogue captures the profile record on open. A cart review is tied to the profile and its current requests. The existing installer still verifies hashes, paths and inventory and applies the guarded transaction. Successful content installation does not prove a successful Minecraft launch.
## Continuation: profile mod workflow (2026-11-03)

The retained source already included the profile table, profile-bound catalogue dialog and isolated installation fixture. The old global catalogue and cart were still duplicated in the main window. They now route to the same dialog, while mod-update controls and local JAR drop remain available. A Cart button in the profile lets a user return to an unfinished selection; local imports from either entry point open that same review. Installation still requires a reviewed plan and explicit acceptance, and closing the dialog refreshes the open profile's table.

The catalogue and table remain partial against sections 5.3–5.4: verified sources, icons, compatibility/update columns, per-row enable/disable/remove actions, richer catalogue filters and accessibility/DPI review are not complete. The offline catalogue has no search, but its cart supports local files and cached metadata where available. Live Modrinth, physical UI review and Minecraft gameplay are not claimed by fixture tests.

Verification: Release build 0 warnings/errors; full suite 953 passed, 2 not run, 0 failed/errors (`verification-all-20261103-225934.log`). The earlier two stale test expectations and their failing run are recorded in SESSION_CHECKPOINT.md. Real online installation and physical UI review remain pending.
