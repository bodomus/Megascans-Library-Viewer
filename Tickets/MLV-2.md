# MLV-2 — Сканирование падает на строковом поле resolution в метаданных Megascans

- URL: https://bodomus.youtrack.cloud/issue/MLV-2
- Effective YouTrack Subsystem: Root; the requested Infrastructure / Parsing value is not configured in the project.
- Type: Bug
- State: In Progress
- Priority: Major
- Assignee: admin
- Estimation: 3h
- Fix versions: не заданы
- Affected versions: не заданы
- Subsystem: не задан — API YouTrack не публикует допустимые значения и отклонил новое значение `Infrastructure / Parsing`
- Fixed in build: будет заполнено после исправления и фиксации commit SHA

## Ошибка

Сканирование библиотеки Megascans завершается исключением, если поле `resolution` в JSON-метаданных хранится строкой.

## Среда и тестовые данные

- Windows, .NET 10, ScanVault
- Каталог воспроизведения: `tests/Megascan`
- Набор содержит 10 JSON-файлов реальных метаданных Megascans

## Шаги воспроизведения

1. Запустить ScanVault.
2. Выбрать каталог `tests/Megascan` как библиотеку.
3. Запустить полное сканирование.

## Фактический результат

Сканирование прерывается исключением:

```text
System.InvalidOperationException:
The requested operation requires an element of type 'Number',
but the target element has type 'String'.
```

Полный диагностический прогон через production-парсер воспроизводит ошибку на 10 из 10 JSON-файлов; успешно обработано 0.

## Ожидаемый результат

Допустимые строковые значения разрешения не должны прерывать сканирование. Кандидаты preview должны корректно ранжироваться, а неизвестное или повреждённое значение `resolution` должно игнорироваться.

## Результаты исследования

Корневая причина находится в `src/ScanVault.Infrastructure/Parsing/PreviewPathResolver.cs`, метод `ReadMetadataCandidates`.

Код вызывает `JsonElement.TryGetInt32` без предварительной проверки `JsonValueKind`. В реальных метаданных одно и то же поле встречается в двух формах:

- число: `"resolution": 8192`;
- строка: `"resolution": "1024x1024"`.

Для строкового значения `TryGetInt32` выбрасывает указанное `InvalidOperationException`. Исключение проходит через `PreviewPathResolver.Resolve` и `MegascansMetadataParser.ParseAsync`, потому что parser обрабатывает ошибки данных/ввода-вывода, но не программное предположение о неверном JSON-типе.

Существующий тест покрывает только числовые разрешения (`256`, `2048`), поэтому регрессия не обнаруживалась.

## План исправления

1. Разбирать `resolution` безопасно для чисел и строк формата `<ширина>x<высота>`.
2. Для ранжирования прямоугольного разрешения использовать максимальную сторону.
3. Неизвестные, отрицательные и повреждённые значения считать отсутствующими и не прерывать сканирование.
4. Добавить регрессионные тесты для числового, строкового и повреждённого значения.
5. Повторно прогнать production-парсер по всем 10 файлам `tests/Megascan` и выполнить полную Release-проверку решения.

## Критерии приёмки

- Сканирование `tests/Megascan` не выбрасывает исключение из-за строкового `resolution`.
- Все 10 JSON-файлов обрабатываются production-парсером.
- Выбор preview остаётся детерминированным и предпочитает большее корректное разрешение.
- Повреждённое `resolution` не прерывает обработку.
- Все автоматические тесты проходят.

## Ограничения

Исходные ассеты и пользовательские тестовые данные в `tests/Megascan` не изменяются и не добавляются в Git.
