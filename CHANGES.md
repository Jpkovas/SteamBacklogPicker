# Recent Changes

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
