# Selection Engine Deterministic RNG Cache

## Overview
`SelectionEngine.NextRandom` now caches a seeded `System.Random` instance instead of recreating and rewinding the generator on every call. When players enable deterministic picks by supplying a seed, the engine:

- Hydrates a single cached PRNG that matches the stored `RandomPosition` counter during startup.
- Resets and fast-forwards the cache if the persisted state moves backwards (for example, when the seed changes or preferences are reloaded).
- Advances both the cached generator and the saved position exactly once per selection.

This preserves deterministic sequences across sessions while eliminating the repeated replay cost that previously scaled with the number of games picked.

## Testing Notes
- Added `PickNext_ShouldResumeSeededSequenceAcrossSessions` to confirm that a fresh engine instance continues the same seeded sequence that a prior session started.
- Existing determinism coverage continues to pass, ensuring the optimization is behaviorally transparent to callers.
