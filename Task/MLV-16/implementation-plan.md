# MLV-16 Implementation Plan

1. Add a Core global-search policy that validates/compiles wildcard queries, enumerates indexed searchable fields, returns a deterministic match reason, and classifies asset result types.
2. Add focused Core tests for substring, wildcard, single-character wildcard, literal regex characters, case-insensitivity, fields and type classification.
3. Extend `MainViewModel` with separate global-search query/error/busy/count/type state; cancellable debounced execution; immediate Search/Clear commands; stale-result protection; Find related; and Show in Library navigation.
4. Extend cards with match-reason, relative-location, Show in Library and Find related commands while preserving existing actions.
5. Add a dedicated global-search strip above the asset list, clear/search controls, type selector, count/error/empty state, result metadata, and tree-selection handling.
6. Add ViewModel tests for cross-folder scope, fields, type filter, restore-on-clear, stale completion and safe failure.
7. Run targeted tests, update CRG and inspect impact, then run Release restore/build/tests and manual WPF checks where possible.
8. Update maintained architecture documentation, create implementation/review reports, and update YouTrack fields when authorized/possible.

