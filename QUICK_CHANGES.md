# Quick Changes

- Updated the Epic catalog SQLite fixture to include offer key images so composite art tests pass.
- Added an Epic-aware hero art locator and composite fallback so Epic and Steam covers resolve correctly.
- Fixed Epic catalog parsing to accept camelCase fields and adjusted Epic cache tests for namespaced IDs.
- Added the full Microsoft.Data.Sqlite bundle so Epic catalog cache tests can read SQLite fixtures.
