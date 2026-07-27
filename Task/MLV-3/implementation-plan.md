# MLV-3 — Implementation plan

1. Set `Popup.IsOpen` binding to explicit `Mode=OneWay`.
2. Preserve the private `IsHoverOpen` setter and existing hover lifecycle.
3. Add a WPF regression test that realizes a card with a non-empty asset collection.
4. Run targeted App tests and a runtime layout smoke check.
5. Update CRG and inspect the final change impact.
6. Run full Release restore/build/test validation.
7. Create implementation/review reports, commit, update YouTrack, and attach `Tickets/MLV-3.md`.
