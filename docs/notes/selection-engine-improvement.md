# Selection Engine Improvement Suggestion

## Context
The `SelectionEngine` orchestrates filtering and selecting backlog entries. While reviewing the implementation, the `NextRandom` method stood out because it re-instantiates `System.Random` each time a seeded sequence is requested:

```
if (_state.Preferences.Seed is int seed)
{
    var random = new Random(seed);
    for (var i = 0; i < _state.RandomPosition; i++)
    {
        _ = random.NextDouble();
    }

    var value = random.NextDouble();
    _state.RandomPosition++;
    return value;
}
```

This approach guarantees determinism, but it also scales poorly when many selections are produced under the same seed. Every pick rebuilds the PRNG and replays the full sequence to reach the desired position.

## Suggested improvement
Caching the seeded `Random` instance (or an equivalent deterministic generator) removes the need to replay the sequence. A lightweight refactor could:

1. Store a persistent seeded `Random` alongside the `_state.RandomPosition` counter.
2. Recreate the cached instance only when the seed changes.
3. Advance the generator a single step on each call.

This keeps determinism intact, lowers CPU usage during long sessions, and simplifies the logic.

## Follow-up considerations
- Update `SelectionSettings` to persist the cached seed and reset the generator if a user changes it.
- Add targeted unit tests to ensure deterministic sequences remain reproducible across sessions.
- Evaluate whether a cryptographically stronger generator is needed once weighting logic evolves beyond a flat distribution.
