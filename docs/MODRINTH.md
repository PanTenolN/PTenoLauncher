# Modrinth and deterministic dependency installation

In a modded profile, open Mods, search Modrinth, choose a result, load compatible versions and add a version with its dependencies to the cart. Adding another project retains previous root choices. Review required entries, source/version, reason, size, optional suggestions and warnings. Installation requires explicit confirmation. Vanilla profiles hide this page.

Search and resolution do not use AI. On an explicit resolution action, installed and explicitly selected local JAR SHA512 hashes are sent to Modrinth to identify versions; the files themselves are not uploaded. The UI states this. No Modrinth account/API key is used.

The read-only v2 client uses a PTenoLauncher User-Agent, exact HTTPS API/CDN hosts, bounded metadata and at most four requests per second. Metadata is cached for offline use; cached/fallback results are labeled. Uncached offline requirements remain unavailable rather than fabricated.

The resolver uses MC/loader metadata, exact dependency versions, recursive required edges, installed identities, incompatibilities, cycles and bounded deterministic backtracking. Optional edges are listed separately, not installed automatically. Installed files are not silently upgraded or replaced. Limits: 128 selected projects, 4096 search steps and bounded recursion.

Before copying anything into the profile, files are checked against CDN SHA512/size metadata and statically inspected. A fingerprint rejects a changed installed set. The existing transaction journal handles staging/commit/recovery.
Fabric declared nested JARs/provided IDs are inspected with depth/count/size/path guards; nested requirements are still checked. No Java code is executed by inspection.

Fabric is installed from Home using Install / repair; see FABRIC.md. Other loader installers remain pending. Exact hash-known local JARs now seed the resolver and share a cart/transaction with catalog selections; see LOCAL_MOD_DEPENDENCIES.md. Full cross-provider solving, unidentified-JAR dependency discovery, Forge/NeoForge/Quilt parsing and mod updates remain pending. Unsupported version expressions stay Unknown. A checksum/provenance check is not antivirus scanning.

Opt-in live integration:

~~~powershell
./scripts/dotnet-local.ps1 run --project tests/PTenoLauncher.IntegrationSmoke -c Release -- .local/modrinth-test --modrinth
~~~

This creates isolated profiles, downloads actual mods and tests cached installation; it does not launch Minecraft.

Official API references:
- https://docs.modrinth.com/api/
- https://docs.modrinth.com/api/operations/getversion
- https://docs.modrinth.com/api/operations/searchprojects/
- https://docs.modrinth.com/api/operations/getprojectversions/

See TEST_REPORT.md for measured results and limitations.

Local identity reference: https://docs.modrinth.com/api/operations/versionfromhash/ . Secondary published JARs are matched by their exact hash, not substituted with a version’s primary file.
