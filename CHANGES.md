# Recent Changes

- Expanded the Epic catalog parser with nested/wrapped JSON fixtures so cache loading handles alternative tag/date/size formats.
- Corrected the Epic catalog SQLite test fixture to stop double-prefixing store-specific IDs so cache tests align with production parsing.
- Fixed the Epic catalog SQLite builder by making its row record internal so `CatalogItemBuilder.Build` can emit rows without accessibility errors.
- Replaced the Epic catalog SQLite fixture blob with a reusable builder that materializes the test database via Microsoft.Data.Sqlite.
- Added an in-app launch service with Epic protocol support and updated viewmodels/tests to respect Epic install criteria.
- Fixed the Epic launch service constructor to avoid nullable method group usage and restore the UI build.
- Updated the Epic catalog SQLite fixture to store offer key image metadata so hero art parsing covers Rocket League.
- Added Epic key image parsing plus a composite art locator that surfaces Epic and Steam hero art selections in the UI.
- Bundled the Microsoft.Data.Sqlite native provider so Epic catalog caches load SQLite fixtures in tests.
- Fixed Epic catalog parsing to read camelCase fields and updated cache tests to validate namespaced identifiers.
- Added Epic discovery unit tests with local fixtures, refreshed multi-store selection coverage, and documented Epic cache requirements and offline guarantees.
- Added a combined Steam/Epic library flow with storefront filters, UI indicators, and updated tests covering the new providers.
- Reverted the repository to match the codex/add-language-selection-buttons branch, removing backlog metadata and compatibility capability updates added afterwards.
- Introduced storefront-aware `GameIdentifier` support across the domain, selection engine, and Steam services, including updated tests for multi-store scenarios.
- Ensured `GameIdentifier` overrides remain compatible with sealed records to fix the CI build regression introduced by the legacy compatibility shim.
- Introduced Epic discovery infrastructure (locator, manifest/catalog caches, and DI wiring) to surface Epic Games Store entries alongside Steam data.
- Restored Epic discovery logging dependencies and public catalog models so the build succeeds after locking disposal paths.
- Expanded the Epic catalog SQLite parser to include offer tables, restoring Rocket League entries in cache lookups.
