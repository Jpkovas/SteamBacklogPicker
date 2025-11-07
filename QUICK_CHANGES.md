# Quick Changes

- Updated the Epic catalog SQLite fixture to include offer key images so composite art tests pass.
- Added a storefront-aware launch service plus Epic launch/install handling in the UI and tests covering Epic scenarios.
- Added an Epic-aware hero art locator and composite fallback so Epic and Steam covers resolve correctly.
- Fixed Epic catalog parsing to accept camelCase fields and adjusted Epic cache tests for namespaced IDs.
- Added the full Microsoft.Data.Sqlite bundle so Epic catalog cache tests can read SQLite fixtures.
