# Project storage cleanup — 2026-10-30

Owner-authorized cleanup completed. Removed generated payload: approximately **119.117 GiB** before subtracting small retained evidence copies. Final measured project size: **4.426 GiB** across 15323 files. These are logical sizes; Windows free-space changes can include unrelated activity.

- Removed eleven historical updater executable E2E source/build/package/synthetic rollback trees, old project source snapshots, old distribution archives and extracted binaries, redundant compiled test/tool outputs, build intermediates, NuGet HTTP cache and the downloaded Inno installer (the installed compiler stays).
- Preserved historical successful AND failed logs/result/status records, including the original updater E2E evidence paths. PAYLOADS_REMOVED.txt markers distinguish evidence from runnable installations. No test result has been changed or claimed as a new run.
- Preserved all current source edits, docs, current Desktop/helper binaries, local .NET SDK, installed build dependencies and existing local Minecraft/Java components. User data, ordinary launcher backups, worlds, accounts, translation data and portable-data folders were excluded from deletion. No files outside the project were changed.
- Verified 723 protected file hashes before/after cleanup, then independently rechecked them. No project build, gameplay or test rerun was performed for this maintenance operation.
- Source requirements and grades remain unchanged: 189 requirement IDs detected; 5 DEFERRED rows. Development remains paused at docs/AI_USABILITY_CHECKPOINT.md, including its two unresolved scoped test failures and unbuilt final XAML edits.

Keep `.local/nuget` as installed dependencies, `.tools/dotnet` as the working SDK/runtime, and the current `src/PTenoLauncher.Desktop/bin/Release/net10.0` launch directory. Deleting these would require rebuilding/downloading components and could break the present offline workflow. The preserved live-integration components are not disposable user data.

Historical artifact/checkpoint references may now name removed old binaries or snapshots. Use current source; do not try to restore a deleted snapshot. Detailed plan/completed records and verified evidence copies: `artifacts\maintenance\20261030-100610-9bf04f`.

On development resume, regenerate obj/restore assets from the retained `.local/nuget` dependencies before a no-restore build, and rebuild the test runner. Previously compiled test binaries were intentionally removed. A network-blocked vulnerability audit remains unverified; this cleanup does not change that.

Current launcher: `%USERPROFILE%\Documents\PTenoLaucher\src\PTenoLauncher.Desktop\bin\Release\net10.0\PTenoLauncher.exe`.
