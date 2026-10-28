# Microsoft/Xbox sign-in — HTTP 400 fix

## Quilt mod metadata checkpoint - 2026-10-28

User retest of the auth-fix payload completed browser OAuth, then failed with HTTP 403 from api.minecraftservices.com. The screenshot identifies the host/status but not the exact endpoint or safe server reason. An application-service access/registration issue is possible, not proven. Full entitlement/profile/refresh and online/offline gameplay remain unverified. No new application, borrowed Client ID or authentication bypass was used.

## Observed on 2026-10-27

The owner registered PTenoLauncher for personal Microsoft accounts, configured the mobile/desktop redirect `http://localhost`, saved the application's own Client ID and completed the system-browser Microsoft step. The launcher then reported HTTP 400 from `user.auth.xboxlive.com`; no Minecraft account/entitlement success was established. Browser completion is not a completed Minecraft sign-in.

No browser callback URL, password, MFA code, access token, refresh token or DPAPI file is included in this report. The owner's registered public Client ID is now the default PTenoLauncher application ID. It is not a secret and does not itself grant Xbox/Minecraft API access or a Minecraft license. Existing saved advanced overrides remain supported; normal players use the visible Sign in button without registering their own application.

## Changes

- Explicit JSON serializer options preserve the exact protocol field names: `Properties`, `AuthMethod`, `SiteName`, `RpsTicket`, `RelyingParty`, `TokenType`, `SandboxId` and `UserTokens`. The previous default Web naming policy changed them to camelCase.
- Xbox user authentication and XSTS requests include `x-xbl-contract-version: 1` and accept JSON. The header is set per request, not on the shared HTTP client, so it is not sent to Minecraft Services or downloads.
- Minecraft Services retains its own `identityToken` spelling. Entitlement/profile validation, MSAL browser flow and DPAPI account storage are unchanged.
- The header and Account page show **Виконати вхід / Sign in** directly when no account is selected; the header button works from every section without navigating to Settings. It changes to the account nickname after verified account selection and reappears after logout. Busy operations disable the button. The existing licensed account is refreshed before an optional skin download.
- SecretVault now uses a synchronous atomic file write with disk flush inside synchronous DPAPI/MSAL callbacks, avoiding a sync-over-async deadlock on the UI thread. DPAPI CurrentUser protection and atomic replacement remain unchanged.
- Authentication service failures now record only the fixed service hostname and HTTP code in the launcher log. Request/response bodies and tokens are excluded.

Official Xbox request contract (user authentication/XSTS sections): https://learn.microsoft.com/en-us/gaming/gdk/docs/services/fundamentals/s2s-auth-calls/service-authentication/live-website-authentication?view=gdk-2604 . This source's web-server client-secret instructions are not applied to the desktop public client.

`AuthenticationHttpTests` checks actual serialized request bodies/headers through an in-memory HTTP handler, Minecraft field compatibility and error-log secret exclusion. Synthetic values are used only in tests; no real user credentials are read. Final verification: **209/209 passed, 0 failed/skipped**, Release build **0 errors/warnings**. See `artifacts/0.2.0/auth-fix-build.log`, `auth-fix-tests-direct.log`, `tests/auth-fix-direct.trx` and `auth-header-test.log`. The actual-XAML test covers visible buttons, direct command binding, busy state, synthetic protected legacy-account nickname/logout, default client configuration and advanced override. VSTest failed its data-collector handshake before execution; the same complete test assembly was run with its bundled xUnit v3 in-process runner. No test was excluded to obtain a pass. Earlier interrupted test attempts did not produce a passing result.

## Retest

The separate self-contained test build is `artifacts/0.2.0/auth-fix-win-x64/PTenoLauncher.exe`. Close the old launcher and start this EXE with its accompanying files. It uses the same normal Windows user data root, so the saved Client ID/settings should remain available. It does not migrate/delete profiles, worlds or credentials. Do not copy only the EXE to another folder.

Keep Offline mode disabled and select Sign in with Microsoft. Enter credentials only in the official browser. After browser completion return to the launcher and report the account result or its bottom status line. Do not send the browser callback/address or token files.

**Full Xbox/XSTS/Minecraft entitlement/profile sign-in after this fix is not yet verified.** API permissions/account prerequisites may still cause a service rejection. Actual licensed online/offline gameplay remains pending. The earlier standard Setup/Portable packages contain the pre-fix payload until explicitly rebuilt; use the separate test build for this retest.
