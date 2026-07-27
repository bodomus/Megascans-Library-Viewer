# MLV-2 — Implementation plan

1. Replace the unsafe direct `TryGetInt32` call with a type-aware resolution parser.
2. Preserve numeric resolution behavior and add deterministic `WxH` ranking using the maximum dimension.
3. Treat malformed or unsupported values as an absent optional resolution.
4. Extend generated-fixture tests for numeric, string, and malformed values.
5. Run targeted tests and the production parser against all 10 user-provided JSON files without changing them.
6. Update CRG, review impact, and run full Release restore/build/test validation.
7. Complete the implementation and review reports, update YouTrack, and record the final commit SHA.
