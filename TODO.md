# TODO

- Cache Epic hero art locally when only remote URIs exist so offline sessions still have covers available.
- Localize the new launch/install error messages emitted by the launch service.
- Audit additional Epic catalog SQLite table naming patterns (e.g., OfflineOffers) to keep the parser aligned with launcher updates.
- Add regression coverage for the new Epic entitlement and metadata fetchers to guard token parsing and GraphQL parsing behaviors.
- Consider renaming the UI environment service namespace (or introducing aliases) to avoid collisions with `System.Environment` and keep future additions from needing fully-qualified calls.
