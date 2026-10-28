# Minecraft process lifecycle — 2026-10-28

This block improves the existing launch engine; it does not bypass authentication or claim a real game session. The previous runner checked cancellation only before Process.Start and then waited without a cancellation token. A running game could not respond to Cancel.

## Implemented behavior

- Cancellation of a started owned process requests termination of its process tree and waits for the parent with a 10-second limit. The actual exit code and explicit cancellation are flushed to the redacted log before cancellation propagates to the UI. A request canceled before startup creates no process/log.
- stdout/stderr are read concurrently with bounded lines, a capped log and throttled live view. An error in a synchronous console consumer triggers process cleanup. A descendant retaining inherited output gets a bounded five-second drain, rather than hanging the launcher indefinitely.
- Home and Settings / Game console expose Stop Minecraft only during a launch operation. Generic Cancel is disabled for that operation. The separate dialog defaults to Cancel and requires explicit acknowledgement of possible unsaved-world data loss before forced termination. Prefer closing Minecraft through its own menu.
- Confirmation captures the launch ID. If the game ends while the dialog is open, accepting that old dialog cannot cancel a different operation or a newer launch.
- Existing profile leases, authentication/readiness gate, active-game marker cleanup and native-directory cleanup remain in the existing GameLauncher. No account/token fixture is used to start actual Minecraft.

## Evidence

Release solution build: zero errors/warnings (`artifacts/0.2.0/critical-process-final-build.log`). Full direct xUnit runner: **330 passed**, zero failed/errors/skipped/not-run (`critical-process-tests.log`, `tests/critical-process.trx`). Ten new cases cover real Windows stdout/stderr/redaction/nonzero exit, parent-and-child cancellation, consumer failure cleanup, pre-cancellation, diagnostic privacy, idle reader cancellation/invalid limits, actual XAML confirmation/default cancellation and ordinary-operation cancellation.

Real installed Temurin **Java 21 x64** was separately executed through JavaManager and LiveProcessRunner: `-version` exit 0; a deliberate invalid JVM option exit 1; **zero HTTP requests, no Minecraft launched**. Evidence: `critical-java-process.log` and `.local/critical-java-process/java-process-result.json`. Java 8/17/25 are not newly verified by that result.

Reproduce the optional Java probe after building, with an explicitly selected existing Java path:

```powershell
.tools/dotnet/dotnet.exe tests/PTenoLauncher.IntegrationSmoke/bin/Release/net10.0/PTenoLauncher.IntegrationSmoke.dll .local/java-process-check --java-process-check <absolute-java.exe-path>
```

The probe writes only in its selected isolated root; it invokes no authentication or game API. The PowerShell subprocess fixtures are isolated test programs, not game replacements or production functionality.

## Limits and next checks

Actual licensed Vanilla/Fabric/Quilt launch, saving/reopening a test world, in-game crash recovery and manual stop behavior are **NOT RUN**. Application approval/entitlement/profile verification remains external. Historical virtual/mapped assets still need implementation; Java 8/17/25 live runtime checks remain next. No current installer lifecycle or paid live AI check was performed.

Process-tree termination is a request, not a security sandbox or a guarantee for hostile/elevated descendants. .NET's parent exit observation does not prove every descendant has exited. The test explicitly waits for its known synthetic child; unknown/reparented or inaccessible descendants need further process-containment work. Kill may lose unsaved data. Official reference: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill?view=net-10.0 . Never weaken Windows permissions or the installer ownership guard to force a test.

Current repository source publication and CI are recorded in `PROCESS_LIFECYCLE_RELEASE_CHECKPOINT.md`. Stable Release remains blocked until P0 real integration checks pass.
