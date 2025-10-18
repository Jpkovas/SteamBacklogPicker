# Recent Changes

- Refactored the Steam library refresh workflow to run heavy operations asynchronously and updated the view model to marshal UI updates safely.
- Added a localized slider in the filters pane to control recent draw exclusions and verified persistence in unit tests.
- Cached Steam VDF fallbacks with dependency tracking and exposed invalidation hooks for library refreshes, including regression tests for reuse behavior.
