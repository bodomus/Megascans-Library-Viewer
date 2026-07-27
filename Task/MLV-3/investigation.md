# MLV-3 — Investigation

## Workflow

- Level: 1, narrow WPF binding bug in a known App template.
- Root: `J:/Projects/UE_Projects/Megascans Library Viewer`.
- Branch: `master`.
- Initial commit: `3864989`.
- Initial working tree: only untracked user data `tests/Megascan/`; preserved unchanged.

## Graph evidence

Graphify query:

```powershell
graphify query "IsHoverOpen AssetCardViewModel binding popup hover MainWindow XAML" --budget 2000 --graph graphify-out\graph.json
```

It identified `MainWindow.xaml`, `AssetCardViewModel`, `MainWindow.xaml.cs`, `VirtualizingWrapPanel`, and App ViewModel tests as the relevant neighborhood.

CRG refresh/status:

```powershell
$env:PYTHONUTF8='1'
code-review-graph update --base HEAD~1 --repo . --brief
code-review-graph status --repo .
```

Status: 287 nodes, 551 edges, 49 C# files, branch `master`, commit `3864989`.

## Source validation

- `VirtualizingWrapPanel.MeasureOverride` calls `RealizeRange` when the item count is non-zero.
- `RealizeRange` calls `PrepareItemContainer`, which applies the item `DataTemplate`.
- The template binds `Popup.IsOpen` to `IsHoverOpen` without an explicit mode.
- `IsHoverOpen` has a private setter and is owned by `BeginHoverAsync`/`EndHover`.
- WPF therefore rejects the target's default source-writing binding mode.
- Existing App tests instantiate ViewModels only and never realize the XAML card template.

## Impact

- Direct: one binding in `MainWindow.xaml` and a WPF regression test.
- Adjacent but unchanged: `AssetCardViewModel`, hover event handlers, `VirtualizingWrapPanel`.
- No Core, Infrastructure, SQLite, scanner, parser, image lifetime, or user-data impact.
