# TODO

- Cache Epic hero art locally when only remote URIs exist so offline sessions still have covers available.
- Localize the new launch/install error messages emitted by the launch service.
- Evaluate replacing the base64 SQLite fixture with scripted generation to simplify maintenance and reduce repository churn when schemas change.
- Add regression fixtures for additional Epic catalog JSON shapes (e.g., nested data payloads) to ensure the parser stays resilient.
- Audit additional Epic catalog SQLite table naming patterns (e.g., OfflineOffers) to keep the parser aligned with launcher updates.
- Add regression coverage for the new Epic entitlement and metadata fetchers to guard token parsing and GraphQL parsing behaviors.
