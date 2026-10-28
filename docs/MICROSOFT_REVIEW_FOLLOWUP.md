# Microsoft / Minecraft application review checkpoint

The owner reports submitting the application review form on 2026-10-28. **Approval is pending; no approval message or successful full licensed sign-in has been observed.** Do not resubmit duplicate applications merely because browser OAuth finishes or a request returns HTTP 403.

Public application information suitable for the review team:

- Application: PTenoLauncher, Windows 11 desktop public client.
- Application ID: 72a454f1-a479-4418-b380-e959b65bc881.
- Redirect URI: http://localhost; personal Microsoft accounts; MSAL/system browser.
- Source: https://github.com/PanTenolN/PTenoLauncher
- Real development preview: https://github.com/PanTenolN/PTenoLauncher/releases/tag/v0.2.0-preview.92998deeb090
- AI-assisted development is disclosed. Runtime AI is optional; an AI assistant/translator is not implemented.

Observed: Microsoft browser OAuth completed, then api.minecraftservices.com returned HTTP 403. That observation alone does not identify the exact reason or establish an application-approval rejection. Code now distinguishes Xbox user authentication, XSTS, Minecraft login, entitlements and profile stages using a fixed enum. Logs include only fixed host, status and stage, never full request URLs, response bodies, tokens or correlation data copied from untrusted responses.

When the review team replies, use their official reply channel for any requested public project information. No follow-up message has been sent by the assistant. Do not send passwords, MFA/device/authorization codes, OAuth callback URLs, bearer/refresh/Xbox/XSTS tokens, DPAPI files or user worlds. The repository does not need a client secret for this desktop flow.

After a decision, test the latest published preview with the existing Client ID. A successful sign-in requires Minecraft ownership and profile verification before the account appears. Record the failed stage and HTTP code if it still fails. Then perform the separate licensed Vanilla launch/refresh/logout/allowed offline checks in docs/HOME_TEST_CHECKLIST.md. A browser completion page and application approval are not successful gameplay evidence.

Official registration resources: https://entra.microsoft.com/ and https://aka.ms/mce-reviewappid . No approval timeframe or automatic resolution of HTTP 403 is promised. Microsoft accounts are created and authenticated through Microsoft's own pages; PTenoLauncher does not register accounts or collect their passwords.
