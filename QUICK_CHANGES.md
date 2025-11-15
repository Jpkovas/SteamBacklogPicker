# Quick Changes

- Added nested Epic catalog fixtures plus parser/test updates so wrapped JSON formats yield correct IDs, sizes, dates, and tags.
- Fixed the Epic catalog SQLite fixture IDs to avoid double namespacing so store identifiers match production behavior.
- Replaced the Epic catalog SQLite fixture blob with a SQL builder so tests no longer ship binary data.
- Updated the Epic catalog SQLite fixture to include offer key images so composite art tests pass.
- Added a storefront-aware launch service plus Epic launch/install handling in the UI and tests covering Epic scenarios.
- Fixed the Epic launch service constructor to compile by wrapping the catalog lookup delegate safely.
- Added an Epic-aware hero art locator and composite fallback so Epic and Steam covers resolve correctly.
- Fixed Epic catalog parsing to accept camelCase fields and adjusted Epic cache tests for namespaced IDs.
- Added the full Microsoft.Data.Sqlite bundle so Epic catalog cache tests can read SQLite fixtures.
- Corrected the Epic catalog SQLite builder to double-quote table names and serialize key images as a parseable array.
- Made the Epic catalog SQLite builder rows internal so the builder's Build method compiles in CI again.
