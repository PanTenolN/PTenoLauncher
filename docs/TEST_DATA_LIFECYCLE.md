# Owned test data and compact evidence

The mandatory continuation block remains in **CONTINUATION_RULES.md**. Carry it into every subsequent development prompt. Do not substitute broad historical cleanup for a test's own lifecycle.

`tests/Shared/OwnedTestRoot.cs`, linked only into test projects, creates `.local/test-runs/<purpose>-<run>-<nonce>` and a durable ownership record both there and in `artifacts/test-lifecycle/<same-id>`. Normal fixture budgets are 512 MiB, native UI probes 64 MiB and the opt-in full updater harness 16 GiB, checked per phase and on disposal. This is not a sandbox for executing untrusted programs.

`TestWorkspace.Dispose` releases SQLite pools and disposes its own root. The updater runner and native probe use finally blocks, preserve bounded compact evidence before deleting payload, and register exact launched PID/start-time/requested executable plus the owned working directory. A still-running recorded child blocks deletion; no user game/launcher/server is terminated. Full build/source/package/rollback trees are disposable. A failed archive of required evidence retains the root and surfaces the failure. A budget error is reported while owned disposable data is still cleaned when safe.

Cleanup verifies absolute ownership, markers, paths, reparse/link rejection and file locks. The marker survives partial removal; an empty markerless root can be reconciled with its external record. A nonempty markerless root is refused, because newer/foreign bytes must not be adopted. There is no automatic scan-and-delete of other sessions or product data. Source, user profiles/worlds/accounts/translations/backups, product cache/Java, `.tools/dotnet`, `.local/nuget`, current Desktop/helper, documents and PDF remain excluded.

Run from the project root using the retained local SDK/dependencies:

```powershell
.\scripts\Invoke-Verification.ps1 build
.\scripts\Invoke-Verification.ps1 ai
.\scripts\Invoke-Verification.ps1 all
.\scripts\Invoke-Verification.ps1 native
```

The wrapper saves source/binary hashes and logs/TRX, then attempts recovery only for exact current-run residual roots. It reports failures without changing permissions or forcibly closing applications. Do not run a build concurrently with native/executable E2E using its binaries. Following maintenance, restore missing assets first with the supported single-node restore; committed audit settings remain enabled. Any deliberate offline restore without vulnerability audit must be recorded as **not a fresh audit**.

Manual recovery takes exactly one reviewed root, never its parent:

```powershell
& .\.tools\dotnet\dotnet.exe .\tests\PTenoLauncher.IntegrationSmoke\bin\Release\net10.0\PTenoLauncher.IntegrationSmoke.dll --owned-cleanup "<exact owned operation root>"
```

It verifies the durable marker and that its owner/recorded children have exited. An unknown, altered, linked or busy root stays retained. Do not work around a refusal. Compact reports live beside the external ownership record. Controlled executable lifecycle probes cover success/failure/cancel/abandonment and recovery; they are **not** a new four-build updater health E2E or gameplay proof.

Current exceptional retained root, byte count and reason are in `artifacts/0.2.0/ai-ux-cleanup-report-20261030.json`. Final full/native evidence and limits are recorded in **AI_USABILITY_CHECKPOINT.md**. Dispose/finally cannot execute after an abrupt process crash; durable records enable later scoped recovery.
