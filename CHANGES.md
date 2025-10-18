# Recent Changes

- Fixed Deck compatibility toggles to respect deselected states so the persisted filter matches the UI configuration.
- Added singleplayer, multiplayer/co-op, VR, and mood tag filters with updated persistence, UI controls, and coverage in selection logic tests.
- Refactored the Steam library refresh workflow to run heavy operations asynchronously and updated the view model to marshal UI updates safely.
- Added a localized slider in the filters pane to control recent draw exclusions and verified persistence in unit tests.
- Cached Steam VDF fallbacks with dependency tracking and exposed invalidation hooks for library refreshes, including regression tests for reuse behavior.
- Introduced Steam Deck compatibility flag filters with matching UI toggles and backward-compatible settings handling.
- Introduced weighted selection sliders for installation state, recency, and Deck compatibility with full localization and tests.
- Introduced backlog status tracking with notes, session targets, and HowLongToBeat estimates persisted alongside selection settings and editable from the game details view.
- Documented backlog status management and offline HowLongToBeat integration in the README.
