# Recent Changes

- Reverted the repository to match the codex/add-language-selection-buttons branch, removing backlog metadata and compatibility capability updates added afterwards.
- Introduced storefront-aware `GameIdentifier` support across the domain, selection engine, and Steam services, including updated tests for multi-store scenarios.
- Ensured `GameIdentifier` overrides remain compatible with sealed records to fix the CI build regression introduced by the legacy compatibility shim.
