# MLV-19 Investigation

## Workflow

- Level: 2 — WPF startup lifecycle и регрессионная проверка dispatcher responsiveness.
- Repository: `J:/Projects/UE_Projects/Megascans Library Viewer`.
- Branch: `codex/MLV-19`, создана от чистого `master`.
- Initial commit: `77cf5ebd1a2dc0354d6749260a79d25d0284cf33`.
- Initial working tree: clean.

## Graph evidence

- `graphify update . --force` удалил из графа следы откаченной реализации и перестроил baseline: 173 files, 2,631 nodes, 6,277 edges.
- Focused Graphify query связал `App.OnStartup`, `MainViewModel.InitializeAsync`, `MainWindow`, `VirtualizingWrapPanel` и `MainWindowTests`.
- CRG был устаревшим по ветке и полностью перестроен командой `code-review-graph build --repo .`.
- Fresh CRG baseline: 154 files, 1,337 effective nodes, 3,870 edges, branch `codex/MLV-19`, commit `77cf5eb`.

## Source validation and cause

- Текущий `App.OnStartup` создаёт один host, один singleton `MainViewModel`, ожидает `InitializeAsync`, затем создаёт и показывает один `MainWindow`.
- Текущий `VirtualizingWrapPanel.cs` не отличается от `b50bd2710314593075e4d71484302f3b42b2bbd6`; это рабочая реализация, и она остаётся без изменений.
- MLV-17 показывал loading до initialization, но закрывал его только после `MainWindow.Show()`. Первый layout главного окна выполнялся при одновременно открытом loading.
- MLV-18 пытался компенсировать этот WPF timing внутри панели: повторно ставил `InvalidateMeasure` на `DispatcherPriority.Loaded`, смешивал panel generator с generator владельца и перехватывал `NullReferenceException`. Непрерывные Loaded callbacks имели приоритет выше Input и замораживали пользовательский ввод.
- Правильная граница исправления — composition root: назначить готовый `MainWindow`, закрыть loading и только затем вызвать `MainWindow.Show()`.

## Constraints

- `ShutdownMode.OnExplicitShutdown` нужен до показа временного первого окна.
- После `LoadingWindow.Show()` нужен dispatcher yield уровня `Loaded`, чтобы окно успело отрисоваться без искусственной задержки.
- Инициализация базы/индекса и DI остаётся в существующем `MainViewModel.InitializeAsync`; code-behind loading остаётся presentation-only.
- Loading закрывается до первого main layout. После успешного `Show()` включается `OnMainWindowClose`.
- Failure path закрывает loading best-effort, показывает существующий startup error и явно завершает приложение.
- No SQLite schema, transaction, filesystem traversal, JSON, duplicate, settings, image cache, source asset, or MLV-16 search behavior changes.

## Test gap

- Существующий STA UI test создаёт только одну карточку и показывает `MainWindow` без предшествующего loading lifecycle.
- Старый MLV-17 test показывал loading после реализации main, поэтому не проверял настоящий порядок.
- Нужен loading-first regression test с множеством карточек, закрытием loading до main show, настоящим `DispatcherFrame`, Input callback и Send-priority timeout, который способен остановить frame даже при starvation Input.

