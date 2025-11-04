# TODO

- Implement storefront-specific cover art retrieval for Epic titles instead of falling back to empty placeholders.
- Wire up launch/install actions for Epic Games Store entries once the launcher protocol strategy is defined.
- Evaluate replacing the base64 SQLite fixture with scripted generation to simplify maintenance and reduce repository churn when schemas change.
- Add regression fixtures for additional Epic catalog JSON shapes (e.g., nested data payloads) to ensure the parser stays resilient.
- Audit additional Epic catalog SQLite table naming patterns (e.g., OfflineOffers) to keep the parser aligned with launcher updates.
