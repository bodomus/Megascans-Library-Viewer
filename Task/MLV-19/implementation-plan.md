# MLV-19 Implementation Plan

1. Добавить presentation-only `LoadingWindow` с существующими ресурсами, требуемым текстом и indeterminate progress bar.
2. Изменить только `App.OnStartup`: explicit shutdown, loading show/yield, существующая initialization, main assignment, loading close, main show, normal shutdown mode.
3. Не изменять `VirtualizingWrapPanel.cs`; проверить отсутствие diff относительно `b50bd27` после реализации.
4. Расширить STA `MainWindowTests`: loading первым, 200 карточек, loading закрыт до main show, настоящий `DispatcherFrame`, Input callback и пятисекундный Send-priority watchdog.
5. Обновить архитектурную документацию без изменений Core/Infrastructure.
6. Выполнить targeted test, post-change CRG/Graphify, Release restore/build/test и доступный реальный UI smoke test.
7. Зафиксировать ограничения ручной проверки и не объявлять окончательную готовность без пользовательской проверки.
