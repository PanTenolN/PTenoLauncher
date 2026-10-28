# Optional online AI and mod translation

PTenoLauncher now contains a real OpenAI Responses API client, assistant/log-analysis UI and a profile-local translation workflow. Automatic contract/integration tests use synthetic HTTP responses and isolated JARs. **No paid live API/model-quality test has been performed, and no real API key was read or used during development.** Minecraft gameplay/resource-pack activation is not verified.

## Enable and configure

1. Open Settings / Appearance and explicitly enable AI. It defaults to off.
2. Open the AI assistant tab. Enter your own OpenAI API key and an exact model ID available to your OpenAI project. Translation requires a model supporting Responses API structured JSON output. No model, paid request or key is configured automatically.
3. Save. The key is encrypted using Windows DPAPI CurrentUser in the launcher's private secrets directory; the input is cleared. SQLite stores the model ID, not the API key. Delete API key removes the protected file. Never send the key to chat, GitHub or a support report.
4. API usage is billed by OpenAI separately. Review your provider account's budget and the preview before confirming any operation. This app does not estimate or guarantee the total cost.

Provider account: https://platform.openai.com/ . The only API endpoint is https://api.openai.com/v1/responses . Redirects, arbitrary endpoints, tool execution, automatic HTTP retries and local AI models are not used.

## Assistant and selected logs

Paste a question or selected log excerpt (up to 48000 characters). The preview masks common token/key patterns, bearer credentials, selected launch flags, email addresses, IP addresses and user-directory names. This is best-effort redaction, not proof that all private information is removed. Review the preview yourself and approve the transfer before Ask or Analyze selected log. The app never automatically reads/upload worlds, account tokens, complete logs or arbitrary local files.

Responses are advice displayed as text; the assistant has no execution tools and cannot install mods, change files or bypass authentication/antivirus. Answers are not treated as verified diagnoses. No conversation is automatically persisted.

## One mod or multiple mods

Open Settings / Mod translations, choose a saved profile and target Minecraft locale, then select one or more JARs. Import is local and works with AI off. Only modern outer-JAR assets/<namespace>/lang/en_us.json string maps are supported. No JAR code is executed or modified. Legacy .lang files, nested JAR language resources, arbitrary component-valued JSON and automatic translation of every possible mod format remain unsupported.

The namespace selector shows string/pending/manual/review counts. Load the JSON editor to review the source/current values. The selected-namespace translation button sends only that namespace; the separate all-imported-namespaces button includes all saved namespaces for this profile/locale, including earlier imports. Review the list and explicitly confirm paid data transfer. Full JARs and local paths are not uploaded; only selected English keys/values are sent.

Missing strings are sent in bounded batches of up to 32. Several billed requests may occur. Each completed batch is validated and saved atomically; a failed/cancelled operation resumes from saved progress only when explicitly requested again. Already translated unchanged strings are not resent. No automatic network job runs after startup or a mod update.

After updating a mod, import its new JAR again. New/changed non-manual strings become pending. Manual corrections are preserved; changed English sources mark them for review and exclude them from the exported pack until you review/save them. A previous draft is retained locally to recover removed source keys. The JSON editor saves actual changes as manual values. Formatting placeholders and known Minecraft formatting codes must be preserved.

## Offline editing and resource packs

Saved drafts, manual edits and resource-pack export work with AI disabled and without internet. Enter the numeric pack_format appropriate for your Minecraft version; automatic format discovery and validation inside the real game are not implemented. Create resource pack writes a profile-owned ZIP under that profile's resourcepacks directory. Enable it and the matching language in Minecraft yourself. Original mods and worlds are unchanged.

Only available reviewed strings are exported. Missing/unreviewed strings are skipped, allowing the game's normal fallback; the completion summary reports counts. Existing untracked destination files are refused. A prior tracked generated pack is backed up before replacement. If creation succeeds but recording ownership is interrupted, the resulting untracked file is refused on retry rather than silently overwritten. Review/move that file manually.

Turning the global AI switch off blocks new provider requests and cancels active ones. Going offline blocks online requests. Completed translations remain local and usable. Profile leases prevent deleting an actively processed profile.

## Data controls and verification limits

Requests use store=false and no tools. This does not guarantee zero provider retention or replace OpenAI's data policies. Official references, checked 2026-10-28:

- https://developers.openai.com/api/docs/guides/structured-outputs
- https://developers.openai.com/api/docs/guides/your-data

Tests cover policy/offline/consent refusal with zero HTTP/key access, redacted request contracts, cancellation, HTTP error privacy/no retries, placeholder failures, incremental/manual preservation, explicit single-namespace scope, interrupted batch recovery, offline pack ZIP contents, untouched synthetic worlds/JARs, untracked-file refusal, profile-deletion preview and actual XAML key/tab behavior. They do not establish live provider availability, translation quality, token pricing, every mod format, or real game compatibility.
