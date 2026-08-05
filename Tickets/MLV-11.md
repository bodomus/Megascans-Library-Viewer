# Export reports

## Summary

Добавить в ScanVault экспорт отчётов по текущему индексу библиотеки Megascans.

Пользователь должен иметь возможность экспортировать:

* полный каталог ассетов;
* текущую отфильтрованную выборку;
* список неполных ассетов;
* список ассетов с проблемами;
* список ассетов без FBX;
* список ассетов без LOD;
* inventory по файлам и текстурам;
* результаты конкретного scan run из MLV-9;
* состав smart collection из MLV-8.

Поддерживаемые форматы первой версии:

```
CSV
JSON
Markdown
```

Экспорт должен быть read-only, не изменять библиотеку и не блокировать UI при больших объёмах данных.

---

## Context

Проект: **ScanVault / Megascans Library Viewer**

Технологии:

* C#;
* .NET;
* WPF;
* SQLite;
* локальная библиотека Megascans;
* read-only анализ содержимого.

Уже реализовано:

* выбор корневой папки;
* сканирование;
* SQLite-индекс;
* дерево физических папок;
* карточки ассетов;
* поиск;
* сортировка;
* Content Inventory;
* completeness;
* diagnostics;
* versioning;
* GitHub Actions CI;
* MLV-8 Saved Filters and Smart Collections;
* MLV-9 Scan History and Change Detection.

Текущая проблема: результаты анализа доступны только внутри приложения. Нельзя сохранить их для аудита, сравнения, передачи другому пользователю или дальнейшей обработки.

---

## Mandatory pre-ticket workflow

Перед изменением кода:

1. Прочитать `AGENTS.md`.
2. Прочитать `.codex/PRE_TICKET_WORKFLOW.md`.
3. Выполнить `$graphify-repository-analysis`.
4. Выполнить `$code-review-graph-analysis`.
5. Проверить вывод обоих инструментов по фактическому коду.
6. Создать:

```
Task/MLV-11/investigation.md
Task/MLV-11/implementation-plan.md
```

7. В `investigation.md` зафиксировать:
   * текущую модель выборки ассетов;
   * query/filter pipeline;
   * модели Content Inventory и completeness;
   * текущие ViewModel;
   * существующие диалоги выбора файлов;
   * текущую работу с background tasks;
   * существующую систему логирования;
   * наличие MLV-8 и MLV-9 в текущей ветке;
   * текущую SQLite schema;
   * blast radius.

Не начинать реализацию до завершения investigation и implementation plan.

---

## Goals

 1. Экспортировать данные текущего индекса без изменения библиотеки.
 2. Поддержать CSV, JSON и Markdown.
 3. Экспортировать как полный индекс, так и текущую выборку.
 4. Экспортировать scan history changes.
 5. Экспортировать smart collection result.
 6. Обеспечить стабильный контракт данных.
 7. Не выполнять тяжёлую сериализацию на UI thread.
 8. Поддержать cancellation и progress.
 9. Обеспечить корректную работу на больших библиотеках.
10. Сохранить совместимость с текущими фильтрами, сортировками и inventory.

---

## Non-goals

В этот тикет не входят:

* Excel `.xlsx`;
* PDF;
* HTML;
* импорт отчётов обратно в приложение;
* двусторонняя синхронизация;
* автоматическая отправка по email;
* облачная публикация;
* экспорт превью-изображений;
* архивирование файлов ассетов;
* копирование FBX, ABC или текстур;
* генерация Unreal Engine manifest;
* пользовательский конструктор шаблонов;
* scheduled export;
* CLI export;
* экспорт бинарных хешей файлов;
* редактирование отчёта внутри ScanVault.

---

## Export scopes

### 1. Entire library

Экспортировать все ассеты текущей библиотеки.

### 2. Current view

Экспортировать текущую выборку с учётом:

* выбранной папки;
* поиска;
* активных фильтров;
* smart collection;
* текущей сортировки.

Экспорт должен использовать ту же query semantics, что и UI.

### 3. Selected assets

Экспортировать только выбранные карточки.

Если множественный выбор пока отсутствует:

* не реализовывать скрытую или временную модель выбора;
* либо ограничить scope текущим выбранным ассетом;
* решение явно описать в implementation plan.

### 4. Issues report

Экспортировать только ассеты:

```
Has Issues = true
```

и включить details проблем.

### 5. Completeness report

Экспортировать:

```
Complete
Partial
Incomplete
Ambiguous
Unknown
```

с возможностью выбрать один или несколько статусов.

### 6. Scan history report

При наличии MLV-9 экспортировать выбранный completed scan run:

```
Added
Changed
Removed
Unchanged
```

Для `Changed` включить change reasons.

### 7. Smart collection report

При наличии MLV-8 экспортировать результат выбранной smart collection.

Экспорт должен фиксировать:

* имя коллекции;
* описание;
* definition version;
* условия;
* дату и время экспорта;
* фактический результат на момент экспорта.

---

## Export profiles

Добавить встроенные профили:

```
Asset Catalog
Asset Inventory
Issues Report
Completeness Report
Scan Changes
Smart Collection Result
```

Профиль определяет набор колонок и структуру.

Пользовательский конструктор профилей не входит в MLV-11.

---

## Asset Catalog fields

Минимальный набор:

```
AssetId
Name
AssetType
LibraryRelativePath
Biome
Region
ResolutionWidth
ResolutionHeight
TexelDensity
CompletenessStatus
HasIssues
HasFbx
HasAbc
HasLods
LodCount
HasVariants
VariantCount
HasAtlas
HasBillboard
TextureSetCount
FileCount
LastIndexedAtUtc
```

Если часть полей отсутствует в модели, использовать nullable/empty значения, а не выдуманные данные.

---

## Asset Inventory fields

Для подробного inventory предусмотреть один из двух вариантов.

### Flat row model

Одна строка на физический файл:

```
AssetId
AssetName
AssetType
AssetRelativePath
FileRelativePath
FileName
Extension
FileCategory
TextureMapType
ResolutionWidth
ResolutionHeight
Variant
Lod
FileSizeBytes
LastWriteTimeUtc
IssueFlags
```

### Hierarchical JSON model

JSON может содержать вложенные:

```
asset
variants
lods
textureSets
files
issues
```

CSV и Markdown должны использовать плоскую модель.

---

## Issues report fields

Минимум:

```
AssetId
Name
AssetType
LibraryRelativePath
CompletenessStatus
IssueCode
IssueCategory
IssueMessage
RelatedFile
Severity
```

Нельзя экспортировать только локализованный UI-текст как единственный идентификатор проблемы.

Должен быть стабильный `IssueCode`.

---

## Scan Changes fields

Минимум:

```
ScanRunId
ScanStartedAtUtc
ScanFinishedAtUtc
ChangeKind
AssetId
Name
AssetType
PreviousPath
CurrentPath
ChangeFlags
PreviousCompleteness
CurrentCompleteness
```

Для `Changed` желательно включить:

```
MetadataChanged
PathChanged
ResolutionChanged
InventoryChanged
FilesChanged
CompletenessChanged
```

---

## Export metadata

Каждый отчёт должен содержать metadata.

Минимум:

```
ReportType
ExportFormat
GeneratedAtUtc
ApplicationVersion
CommitSha
SchemaVersion
NormalizationVersion
FingerprintVersion
LibraryIdentity
LibraryRoot
SourceScope
FilterSummary
SortSummary
AssetCount
RowCount
```

Правила:

* в JSON metadata хранится отдельным объектом;
* в Markdown metadata выводится в начале документа;
* в CSV metadata сохраняется либо отдельным companion-файлом `.metadata.json`, либо в первых comment rows.

Предпочтение: companion JSON для CSV.

Решение зафиксировать в implementation plan.

---

## CSV requirements

1. Кодировка:

```
UTF-8 with BOM
```

для удобного открытия в Excel на Windows.

2. Разделитель:

* по умолчанию `,`;
* допустимо использовать `;`, но это должно быть настройкой или обоснованным решением;
* не определять delimiter случайно по culture.

3. Обязательное корректное quoting:

* запятые;
* точки с запятой;
* кавычки;
* переводы строк;
* Unicode.

4. Заголовки:

* стабильные machine-readable;
* не зависят от языка UI.

5. Даты:

```
ISO 8601 UTC
```

6. Числа:

```
InvariantCulture
```

7. Не использовать `ToString()` доменных объектов как контракт экспорта.
8. Для большого отчёта писать потоково, не строить весь CSV в памяти.

---

## JSON requirements

1. Формат UTF-8.
2. Имена полей стабильны.
3. Использовать explicit DTO.
4. Добавить `ReportSchemaVersion`.
5. Поддержать pretty print как опцию.
6. Не сериализовать ViewModel, EF entities или SQLite rows напрямую.
7. Не включать абсолютные пути, если пользователь явно не выбрал такую опцию.
8. По умолчанию экспортировать relative paths.
9. Большие коллекции сериализовать потоково, если это поддерживается текущим stack.

---

## Markdown requirements

Markdown предназначен для читаемого отчёта, а не полного машинного interchange.

Структура:

```
# Report title

## Metadata

## Summary

## Assets / Changes / Issues
```

Требования:

* таблицы для компактных наборов;
* для больших inventory использовать секции или ограниченный flat format;
* корректно экранировать `|`, переводы строк и backticks;
* не вставлять бинарные или base64 данные;
* при очень большом количестве строк предупреждать пользователя о размере;
* отчёт должен оставаться валидным Markdown.

---

## Export dialog

Добавить диалог **Export Report**.

Поля:

```
Report profile
Scope
Format
Destination file
Include absolute paths
Include metadata
Pretty JSON
Include unchanged scan items
```

Параметры должны показываться только когда применимы.

Перед запуском показать оценку:

```
Assets: 9,458
Estimated rows: 28,321
```

Оценка может быть приблизительной, но должна быть обозначена как estimate.

---

## File naming

Имя по умолчанию:

```
scanvault-<report-type>-<yyyyMMdd-HHmmss>.<ext>
```

Примеры:

```
scanvault-asset-catalog-20260730-181400.csv
scanvault-scan-changes-20260730-181400.json
scanvault-issues-report-20260730-181400.md
```

---

## Overwrite behavior

Если файл существует:

* запросить подтверждение;
* не перезаписывать молча;
* temporary file создавать рядом с destination;
* после успеха выполнить atomic replace/move, где возможно;
* при cancellation или failure удалить temporary file;
* существующий destination не должен быть повреждён.

---

## Progress and cancellation

Экспорт должен выполняться асинхронно.

Показывать:

```
Preparing query
Reading assets
Writing report
Finalizing
```

Также:

```
Processed assets
Written rows
Elapsed time
```

Требования:

* `CancellationToken` проходит через query и writer;
* cancellation не считается ошибкой;
* UI остаётся отзывчивым;
* частичный файл не остаётся под финальным именем;
* progress updates не должны перегружать UI thread.

---

## Large library handling

Экспорт должен работать при:

```
10,000+ assets
100,000+ inventory rows
```

Требования:

* streaming;
* batch reads;
* без `ToList()` всего inventory, если это можно избежать;
* без N+1 SQL;
* без загрузки previews;
* без чтения содержимого FBX/ABC/texture файлов;
* без повторного сканирования библиотеки;
* использовать данные текущего индекса.

---

## Architecture requirements

Предпочтительные границы:

```
IReportExportService
IReportDataSource
IReportWriter
IReportProfile
```

Форматные writer:

```
CsvReportWriter
JsonReportWriter
MarkdownReportWriter
```

Доменные DTO:

```
ReportMetadataDto
AssetCatalogRowDto
AssetInventoryRowDto
IssueReportRowDto
ScanChangeRowDto
```

Не создавать огромный `ExportService` с ветвлением по всем форматам и профилям.

Не связывать writers с WPF.

Не выполнять file dialog и сериализацию в одном классе.

---

## Report schema versioning

Добавить:

```
ReportSchemaVersion
```

Отдельно от:

```
DatabaseSchemaVersion
NormalizationVersion
FingerprintVersion
```

Правила:

* JSON обязательно содержит schema version;
* metadata CSV companion содержит schema version;
* Markdown metadata содержит schema version;
* изменение набора или семантики полей требует оценки bump версии.

---

## Absolute path policy

По умолчанию экспортировать relative paths.

Абсолютные пути включать только по явной опции:

```
Include absolute paths
```

Причины:

* приватность;
* переносимость;
* стабильность отчёта;
* независимость от конкретного диска.

---

## Logging

Добавить структурированные события:

```
Report export started
Report export completed
Report export cancelled
Report export failed
```

Поля:

```
ReportProfile
ExportFormat
Scope
DestinationExtension
AssetCount
RowCount
DurationMs
OutputSizeBytes
IncludeAbsolutePaths
```

Не логировать полный destination path на `Information`, если это противоречит текущей политике.

---

## Diagnostics

При наличии уместного раздела добавить:

```
Last export status
Last export format
Last export duration
Last export row count
Report schema version
```

Не хранить destination path без необходимости.

---

## Error handling

Обработать:

 1. destination directory не существует;
 2. нет прав на запись;
 3. destination file занят;
 4. диск заполнен;
 5. serialization error;
 6. SQLite read error;
 7. cancellation;
 8. invalid report profile;
 9. unsupported report format;
10. corrupted smart collection;
11. отсутствующий scan run;
12. scan run не Completed;
13. temporary file cleanup failure;
14. overwrite conflict;
15. недопустимый filename;
16. слишком длинный путь Windows.

Критическое правило:

> Ошибка экспорта не должна изменять индекс, библиотеку или существующий destination file.

---

## Acceptance criteria

### General

* Есть entry point `Export Report`.
* Экспорт работает для Entire Library.
* Экспорт работает для Current View.
* Экспорт использует текущий query/filter pipeline.
* Экспорт не изменяет библиотеку.
* Экспорт не запускает повторное сканирование.
* UI остаётся отзывчивым.
* Поддерживается cancellation.
* Partial file не остаётся под финальным именем.

### CSV

* CSV создаётся в UTF-8 with BOM.
* Quoting корректно для delimiter, quotes и newlines.
* Даты в ISO 8601 UTC.
* Числа используют InvariantCulture.
* Header names стабильны.
* Большой CSV пишется потоково.
* Metadata сохраняется выбранным согласованным способом.

### JSON

* JSON использует explicit DTO.
* JSON содержит ReportSchemaVersion.
* JSON содержит metadata.
* JSON не сериализует ViewModel.
* Relative paths используются по умолчанию.
* Pretty print работает.
* Большой отчёт не требует хранения всей модели в памяти.

### Markdown

* Markdown содержит title, metadata и summary.
* Таблицы корректно экранируются.
* Символ `|` не ломает таблицы.
* Переводы строк не ломают строки.
* Large report формируется без зависания UI.
* Markdown остаётся читаемым.

### Profiles

* Asset Catalog работает.
* Asset Inventory работает.
* Issues Report работает.
* Completeness Report работает.
* Scan Changes работает при наличии MLV-\`, если это не соответствует текущей политике логирования.

---

## Diagnostics

Если соответствует текущей архитектуре, добавить:

```
Last export status
Last export format
Last export duration
Last export row count
Report schema version
```

Не сохранять destination path без необходимости.

---

## Error handling

Обработать минимум:

 1. destination directory отсутствует;
 2. нет прав на запись;
 3. destination file занят;
 4. диск заполнен;
 5. serialization error;
 6. SQLite read error;
 7. cancellation;
 8. invalid report profile;
 9. unsupported format;
10. corrupted smart collection;
11. отсутствующий scan run;
12. scan run не имеет статус `Completed`;
13. temporary file cleanup failure;
14. overwrite conflict;
15. недопустимое имя файла;
16. слишком длинный Windows path.

Критическое правило:

> Ошибка экспорта не должна изменять индекс, библиотеку или существующий destination file.

---

## Acceptance criteria

### General

* Есть entry point `Export Report`.
* Работает экспорт Entire Library.
* Работает экспорт Current View.
* Используется общий query/filter pipeline.
* Экспорт не изменяет библиотеку или индекс.
* Экспорт не запускает Rescan.
* UI остаётся отзывчивым.
* Поддерживается cancellation.
* Partial file не остаётся под финальным именем.

### CSV

* UTF-8 with BOM.
* Корректное quoting delimiter, quotes и newlines.
* ISO 8601 UTC для дат.
* `InvariantCulture` для чисел.
* Стабильные headers.
* Потоковая запись.
* Companion metadata JSON.

### JSON

* Используются explicit DTO.
* Есть `ReportSchemaVersion`.
* Есть metadata.
* ViewModel не сериализуется.
* Relative paths используются по умолчанию.
* Pretty print работает.
* Большой отчёт не требует хранения всей модели в памяти.

### Markdown

* Есть title, metadata и summary.
* Таблицы корректно экранируются.
* `|` и переводы строк не ломают структуру.
* Большой отчёт не блокирует UI.
* Результат открывается стандартным Markdown renderer.

### Profiles

* Asset Catalog работает.
* Asset Inventory работает.
* Issues Report работает.
* Completeness Report работает.
* Scan Changes работает при наличии MLV-9.
* Smart Collection Result работает при наличии MLV-8.
* Неподдерживаемый профиль не приводит к падению приложения.

### File safety

* Существующий файл не перезаписывается без подтверждения.
* Используется temporary file.
* При успехе выполняется final move/replace.
* При cancellation temporary file удаляется.
* При failure существующий destination остаётся целым.

### Performance

* Нет N+1 SQL.
* Не загружаются previews.
* Не читается binary content asset files.
* Измерен экспорт 10,000+ assets.
* Проверены 100,000+ inventory rows.
* Progress updates не перегружают UI.

### Compatibility

* Поиск и фильтры работают без регрессий.
* MLV-8 работает.
* MLV-9 работает.
* Content Inventory работает.
* Diagnostics работает.
* GitHub Actions остаётся зелёным.

---

## Test plan

### Unit tests

 1. CSV escaping delimiter.
 2. CSV escaping quotes.
 3. CSV escaping newline.
 4. CSV UTF-8 BOM.
 5. Invariant number formatting.
 6. UTC date formatting.
 7. JSON schema version.
 8. JSON DTO serialization.
 9. Markdown pipe escaping.
10. Markdown newline escaping.
11. Default filename generation.
12. Invalid filename sanitization.
13. Profile field mapping.
14. Relative path default.
15. Absolute path opt-in.
16. Metadata generation.
17. Cancellation behavior.
18. Temporary filename generation.
19. Row count.
20. Unsupported profile handling.

### Integration tests

 1. Entire Library CSV.
 2. Current View CSV.
 3. Asset Inventory JSON.
 4. Issues Report Markdown.
 5. Smart Collection Result.
 6. Completed Scan Changes.
 7. Rejection of non-completed scan run.
 8. SQLite batch reading.
 9. No N+1 query.
10. Temporary file cleanup on failure.
11. Existing destination remains intact.
12. Access denied.
13. Cancellation mid-export.
14. Large synthetic dataset.
15. Unicode and special characters.

### ViewModel/UI tests

 1. Initial dialog state.
 2. Profile-dependent options.
 3. Scope selection.
 4. Format selection.
 5. Extension update.
 6. Estimate display.
 7. Progress display.
 8. Cancel command.
 9. Success state.
10. Error state.
11. Overwrite confirmation.
12. Disabled export for invalid destination.
13. Scan run selector.
14. Smart collection selector.

### Manual tests

 1. Экспортировать Entire Library в CSV.
 2. Открыть CSV в Excel.
 3. Проверить кириллицу.
 4. Проверить кавычки, delimiter и multiline fields.
 5. Экспортировать Current View в JSON.
 6. Проверить metadata и schema version.
 7. Экспортировать Issues Report в Markdown.
 8. Открыть Markdown preview.
 9. Экспортировать Asset Inventory.
10. Проверить row count.
11. Экспортировать scan changes из MLV-9.
12. Экспортировать smart collection из MLV-8.
13. Запустить большой экспорт.
14. Отменить в середине.
15. Проверить отсутствие partial destination.
16. Проверить overwrite confirmation.
17. Проверить access denied.
18. Проверить occupied file.
19. Проверить cleanup temporary file.
20. Убедиться, что индекс и библиотека не изменились.

---

## Performance validation

В `implementation-report.md` зафиксировать:

```
Asset count
Inventory row count
Format
Output size
Duration
Peak managed memory
Cancellation latency
```

Минимум проверить:

```
10,000 assets — Asset Catalog CSV
100,000 rows — Asset Inventory CSV
10,000 assets — JSON
5,000 issues — Markdown
```

Если реальной библиотеки такого размера нет, использовать synthetic integration dataset и явно это указать.

---

## Regression matrix

```
Fresh database
Existing indexed library
No filters
Active manual filters
Active smart collection
Completed scan run
Cancelled scan run
CSV
JSON
Markdown
Existing destination
Access denied
Cancellation
Large dataset
Unicode names
Comma/semicolon/quotes in values
Long relative paths
Application restart
```

---

## Implementation constraints

* Не сериализовать ViewModel.
* Не использовать UI column reflection как export contract.
* Не строить весь отчёт в памяти без необходимости.
* Не читать физическое содержимое asset files.
* Не изменять индекс.
* Не изменять библиотеку.
* Не оставлять partial file под финальным именем.
* Не использовать локализованные headers как machine contract.
* Не смешивать `ReportSchemaVersion` и database schema version.
* Не добавлять Excel или PDF в MLV-11.
* Не выполнять export на UI thread.
* Не дублировать query semantics ручных фильтров и smart collections.

---

## Documentation

Обновить:

* `README.md`;
* пользовательское описание Export Reports;
* список профилей;
* список форматов;
* CSV encoding и delimiter policy;
* JSON schema version;
* Markdown limitations;
* relative/absolute path policy;
* metadata contract;
* cancellation behavior;
* large dataset behavior;
* ограничения первой версии.

Добавить пояснение:

> Export reports are generated from the current SQLite index. ScanVault does not reread or copy binary asset contents during export.

---

## Deliverables

Codex должен создать или обновить:

```
Task/MLV-11/investigation.md
Task/MLV-11/implementation-plan.md
Task/MLV-11/implementation-report.md
```

В `implementation-report.md` указать:

* изменённые файлы;
* архитектуру profiles и writers;
* export DTO;
* `ReportSchemaVersion`;
* metadata contract;
* CSV policy;
* JSON policy;
* Markdown policy;
* temporary file strategy;
* progress/cancellation;
* performance measurements;
* тесты;
* известные ограничения;
* blast radius;
* Graphify/CRG update status.

---

## Required final validation

Перед завершением задачи:

 1. Обновить code-review-graph.
 2. Проверить blast radius.
 3. Обновить Graphify при архитектурных изменениях.
 4. Выполнить restore.
 5. Выполнить build.
 6. Выполнить все tests.
 7. Выполнить format verification.
 8. Выполнить `git diff --check`.
 9. Проверить CSV в Excel.
10. Проверить JSON внешним parser.
11. Проверить Markdown renderer.
12. Проверить cancellation.
13. Проверить overwrite safety.
14. Проверить large dataset.
15. Проверить GitHub Actions на push или pull request.

Ожидаемый итог:

```
Build: passed
Tests: passed
Format: passed
git diff --check: passed
CSV validation: passed
JSON validation: passed
Markdown validation: passed
Cancellation validation: passed
Large dataset validation: passed
GitHub Actions: green
```
