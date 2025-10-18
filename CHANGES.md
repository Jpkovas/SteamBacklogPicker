# Recent Changes

- Refactored the Steam library refresh workflow to run heavy operations asynchronously and updated the view model to marshal UI updates safely.
- Added a localized slider in the filters pane to control recent draw exclusions and verified persistence in unit tests.
- Cached Steam VDF fallbacks with dependency tracking and exposed invalidation hooks for library refreshes, including regression tests for reuse behavior.
- Introduced Steam Deck compatibility flag filters with matching UI toggles and backward-compatible settings handling.
- Introduced weighted selection sliders for installation state, recency, and Deck compatibility with full localization and tests.
