# MLV-19 — Исправить зависание UI после startup loading screen

## Проблема

После закрытия `LoadingWindow` главное окно отображалось, но переставало реагировать на мышь и клавиатуру. Причиной был бесконечный цикл `ScheduleGeneratorRetry → DispatcherPriority.Loaded → InvalidateMeasure → MeasureOverride`, который вытеснял `DispatcherPriority.Input`.

## Требования

- Оставить `VirtualizingWrapPanel.cs` в рабочем состоянии из `b50bd27`.
- Не изменять компонент виртуализации ради startup loading screen.
- Не использовать бесконечный dispatcher retry, `NullReferenceException` как управление потоком или generator родительского `ItemsControl` вместо panel generator.
- Реализовать startup lifecycle в порядке:
  1. `ShutdownMode.OnExplicitShutdown`;
  2. показать `LoadingWindow`;
  3. выполнить существующую инициализацию;
  4. создать и назначить `MainWindow`;
  5. закрыть loading до первого layout `MainWindow`;
  6. показать `MainWindow`;
  7. установить `ShutdownMode.OnMainWindowClose`.
- Добавить regression test с большим количеством карточек и настоящим `DispatcherFrame`.
- После показа `MainWindow` проверить, что callback `DispatcherPriority.Input` выполняется за ограниченное время и layout не зацикливается.
- Выполнить реальную проверку мыши, клавиатуры, прокрутки и поиска. `Process.Responding` не считать доказательством работоспособности UI.
- Не объявлять исправление окончательно готовым без пользовательской проверки.

## Ветка и baseline

- Ветка: `codex/MLV-19`.
- Создана непосредственно от чистого `master`.
- Начальный commit: `77cf5ebd1a2dc0354d6749260a79d25d0284cf33`.

