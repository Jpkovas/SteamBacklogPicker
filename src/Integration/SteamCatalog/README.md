# Steam catalog and optional Steam Families session

This .NET 8 module has no dependency on Domain, AppCore or a UI toolkit. It reads public Steam product information through SteamKit2 **3.3.1** PICS, falls back to the public store API, and stores results in an application-owned SQLite database using Microsoft.Data.Sqlite **8.0.30**. It never modifies Steam's appcache, userdata or authentication files.

## Composition

```csharp
var cache = new SqliteCatalogCache();
var steam = new SteamKitCatalogTransport(new MemorySteamTokenStore());
var store = new SteamStoreMetadataSource();
var source = new FallbackCatalogMetadataSource(steam, store);
var catalog = new SteamCatalogService(cache, source);
var family = new SteamFamilySessionService(steam, cache);
```

These constructors do not connect to Steam. Register one shared instance of each service. The owner disposes `store`, `catalog` and `cache`, and preferably awaits `steam.DisposeAsync()` after cancelling background requests. Synchronous `steam.Dispose()` is also supported: it cancels operations and disconnects immediately, then observes asynchronous cleanup without blocking the UI.

`catalog.EnrichAsync(ids, "brazilian", CatalogNetworkMode.Offline, progress, token)` uses only the application cache. Explicit `Online` permits PICS (anonymous login when no QR session exists) and public store requests. Pass Steam language names such as `english`, `brazilian`, `portuguese`, or `spanish`, rather than assuming UI culture identifiers are identical. Results distinguish confirmed `Found`, confirmed `NotFound`, and `Unavailable`; stale positive metadata remains available offline or after a transient network failure.

SQLite keys are `(appid, language)`. Positive and negative TTLs default to seven days and six hours. Transient failures are never cached as missing games. Request batches reject more than 50,000 distinct nonzero IDs. The default network concurrency is two, with a shared minimum request interval, bounded retries honoring `Retry-After`, cancellable backoff, and a one-minute PICS circuit break on unavailable responses. Global locks prevent duplicate successful lookups for the same ID/language across overlapping requests. Store responses have an overall 15-second deadline, a 2 MiB decoded-body limit and bounded JSON depth. Artwork URLs use a fixed Steam CDN origin.

`SqliteCatalogCache` also implements the optional `IBatchCatalogCache` capability: offline and fresh-cache reads reuse one connection per catalog request, querying at most 256 IDs per parameterized statement. Stale/missing online entries retain the shared per-key recheck before fetching. Cache implementations without this capability continue to work. Disposal cancels pending work while allowing active operations to release their managed gates safely.

## Explicit QR authentication and Family snapshots

```csharp
var identity = await family.LoginWithQrAsync(loginProgress, cancellationToken);
var snapshot = await family.GetFamilySnapshotAsync(
    identity.SteamId, "brazilian", CatalogNetworkMode.Online, cancellationToken);
await family.LogoutAsync();
```

`SteamLoginProgress` reports the current `ChallengeUrl` and subsequent refreshes. Encode that URL as a QR image in the login UI. The user approves it in the Steam mobile app. The cancellation token cancels QR polling and closes the transport; a three-minute upper bound also applies. Challenge URLs are ephemeral credentials: do not log them, persist them or send them to a third-party QR service. The DTO's `ToString()` redacts the URL.

The implementation calls `BeginAuthSessionViaQRAsync`, polls the authentication session, logs on using the returned refresh token and verifies the authenticated Steam ID. A random per-transport Steam `LoginID` avoids reusing SteamKit's bind-address default alongside other clients. It then calls the generated `FamilyGroups.GetFamilyGroupForUser` and `GetSharedLibraryApps` services with `include_own`, `include_excluded`, and `include_non_games` enabled. No `max_apps` cap is requested. These calls and their response shapes compile against the installed SteamKit 3.3.1 assembly; no credentials are required for the test suite.

The default token store is **memory only**. The module provides no plaintext token persistence and never harvests client cookies or passwords. An optional `ISteamTokenStore` implementation must use an OS-protected credential facility, and must report persistence truthfully. Logout clears its credential and deletes cached Family evidence for that identity across languages. A failed cache purge is surfaced; it must not be presented as successful deletion.

`FamilySnapshot.Status` distinguishes success, authentication required, revoked access and transient error. A valid empty success replaces earlier Family evidence. `HasFamily=false` means the account has no Steam Family; it is **not** a complete owned-games response and must not erase local discovery. Cache reads require an explicit identity and language; an online refresh additionally requires that identity to match the authenticated session. Expired offline snapshots are marked stale. A transient error can retain last-known apps but remains `Error` and stale. Revocation clears cached rights and prevents offline reuse within the session.

`FamilyApp.OwnerSteamIds`, `Access`, and the original exclusion reason retain entitlement evidence independently of installation state. An app can be owned by the current user even when it is excluded from sharing. FamilyGroups does not provide current copy locks here: `IsCurrentAvailabilityKnown` is always false. The UI/domain must not equate a shared entitlement with an installed or currently launchable copy; Steam remains the authority on launching.

## Validation and primary references

Tests under `tests/Integration/SteamCatalog.Tests` exercise real SQLite persistence, language and identity isolation, TTLs, negative-cache semantics, malformed/oversized HTTP responses, fixed endpoints, concurrency/single-flight, cancellation, PICS `KeyValue` mapping, actual generated Family protobuf mapping, valid-empty responses, offline evidence and revocation. They do not log into a real account. An end-to-end QR/mobile approval and Family response still requires an explicitly participating user.

- [SteamKit 3.3.1 QR authentication sample](https://github.com/SteamRE/SteamKit/blob/3.3.1/Samples/001_AuthenticationWithQrCode/Program.cs)
- [SteamKit 3.3.1 unified-message implementation](https://github.com/SteamRE/SteamKit/blob/3.3.1/SteamKit2/SteamKit2/Steam/Handlers/SteamUnifiedMessages/SteamUnifiedMessages.cs)
- The installed SteamKit2 3.3.1 XML documentation describes `LogOnDetails.LoginID`, PICS, QR polling and token-session flags. Generated `SteamKit2.Internal.FamilyGroups` and its request/response types were inspected directly from that assembly.
- [Published Microsoft.Data.Sqlite package versions](https://api.nuget.org/v3-flatcontainer/microsoft.data.sqlite/index.json)
