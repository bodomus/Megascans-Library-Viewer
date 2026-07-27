# MLV-3 — EXE падает при реализации карточки из-за TwoWay binding IsHoverOpen

- URL: https://bodomus.youtrack.cloud/issue/MLV-3
- Type: Bug
- State: In Progress
- Priority: Critical
- Subsystem: Root
- Assignee: ChatGPT
- Estimation: 1h
- Fix versions: не заданы
- Affected versions: не заданы

## Ошибка

ScanVault аварийно завершается при отображении первой карточки актива в виртуализированной сетке.

## Фактический результат

Во время выполнения `VirtualizingWrapPanel.RealizeRange(firstIndex, lastIndex)` возникает:

```text
System.Windows.Markup.XamlParseException:
A TwoWay or OneWayToSource binding cannot work on the read-only property
'IsHoverOpen' of type 'ScanVault.App.ViewModels.AssetCardViewModel'.
```

## Ожидаемый результат

Карточки индексированных активов должны создаваться и отображаться без XAML-исключения. Hover popup должен открываться и закрываться только состоянием ViewModel.

## Результаты исследования

`RealizeRange` не является источником ошибки. Метод лишь создаёт контейнер, после чего WPF применяет `DataTemplate` карточки из `src/ScanVault.App/MainWindow.xaml`.

Проблемная привязка:

```xml
<Popup IsOpen="{Binding IsHoverOpen}">
```

У `Popup.IsOpen` режим привязки по умолчанию допускает обратную запись в источник. При этом `AssetCardViewModel.IsHoverOpen` имеет приватный setter и предназначен для изменения только методами `BeginHoverAsync()` и `EndHover()`. При подключении binding WPF считает свойство источника read-only и выбрасывает `XamlParseException`.

Graphify связал `MainWindow.xaml`, `AssetCardViewModel`, `MainWindow.xaml.cs` и существующие ViewModel-тесты. CRG обновлён до commit `3864989` (287 nodes, 551 edges, 49 files). Прямой просмотр исходников подтвердил найденную связь.

## Исправление

Задать явный однонаправленный режим:

```xml
<Popup IsOpen="{Binding IsHoverOpen, Mode=OneWay}">
```

Публичный setter добавлять не нужно: состояние popup принадлежит ViewModel.

## Покрытие

Текущие App-тесты проверяют ViewModel, но не создают реальный `DataTemplate`, поэтому ошибка проходит XAML-компиляцию и существующие тесты. Нужен WPF-регрессионный тест, который создаёт `MainWindow`/карточку с непустой коллекцией активов и принудительно выполняет layout/реализацию контейнера.

## Критерии приёмки

- Непустая сетка активов реализует карточки без `XamlParseException`.
- `Popup.IsOpen` использует `Mode=OneWay`.
- `IsHoverOpen` сохраняет приватный setter.
- Hover delay/cancellation и закрытие popup продолжают работать.
- Регрессионный тест воспроизводит создание карточки.
- Полная Release-сборка и все тесты проходят.

## Ограничения

Исправление не меняет алгоритм `VirtualizingWrapPanel`, данные индекса, SQLite, пользовательские ассеты или `tests/Megascan`.
