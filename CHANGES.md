# Recent Changes

- Added persistent backlog metadata with UI editing, completion-time caching, and status/time filters backed by new domain and UI tests.
- Added a unit test that covers target session length filtering in the selection engine.
- Refactored the Steam library refresh workflow to run heavy operations asynchronously and updated the view model to marshal UI updates safely.
- Added a localized slider in the filters pane to control recent draw exclusions and verified persistence in unit tests.
