# План исправления 1.0.0.29 — issue #21 «Долгое переключение между вкладками "Все", "Избранное"»

Версия: **1.0.0.28 → 1.0.0.29** (увеличивается только последнее число).
Issue: **#21** — НЕ закрываем (закрытие только после подтверждения пользователем по итогам релиза).

---

## 1. Сводка симптомов (последние комментарии, ориентируемся на них)

| Источник | Симптом |
|---|---|
| 7OH (2026-10-20 17:44) | «при переключении вкладки отъедает до 70 МБ памяти каждый раз и не возвращает её»; «скорость падает с каждым переключением» — оба симптома связаны |
| 7OH (2026-10-20 17:46, скриншот) | «поймал момент переключения на Все базы из Недавние… чем дольше переключаешь — тем проще дождаться вот такого» (зависание/артефакт интерфейса) |
| 7OH (ранее, логи) | за 6 переключений — **12 записей CM_COLUMNS** (по 2 на переключение), время между переключениями растёт |
| ArtyoZ | «релиз 1.0.0.26 улучшений не принёс» |

Что уже сделано в 1.0.0.26/1.0.0.27 и **не дало полного эффекта**:
- кэш отфильтрованных списков «Избранное»/«Недавние» (`_listFilter`), один Reset вместо тысяч событий;
- постоянный плоский узел `_flatNode` (контейнер корня не пересоздаётся);
- кэш дерева «Все» `_groupedTreeCache` (WPF);
- флаг `_listModeSwitchInProgress` — без восстановления выделения/фокуса при чистом переключении;
- ранний выход `RevealAndSelectAfterRebuild` по `IsRevealTargetReachable`;
- кэш-хит `UpdateTreeMinWidthContent`, дебаунс прокруток, склейка `QueueHeaderAlign`.

**Вывод:** пересоздание *данных* (VM) устранено; не устранено пересоздание *контейнеров строк* и остатки каскада UI-пересчётов — именно они дают 70 МБ на переключение и падение скорости.

---

## 2. Корневые причины (по коду)

### Причина A — Avalonia: полное пересоздание контейнеров строк при каждой смене вкладки (+ ~70 МБ на переключение)

- В `LeveledTreeView` **виртуализации нет вовсе** (комментарий в [`MainWindow.Avalonia.cs`](Workspace1C/Views/MainWindow.Avalonia.cs:1435): «у TreeView в Avalonia 11.3.20 виртуализации нет вовсе, панель элементов по умолчанию обычный StackPanel»).
- Каждая смена вкладки делает `FlatItems.ReplaceAll(...)` / `GroupNodes.ReplaceAll(...)` / `_flatNode.ResetFlatContent(...)` → `ObservableRangeCollection.ReplaceAll` шлёт **Reset** ([`ObservableRangeCollection.cs`](Workspace1C/Collections/ObservableRangeCollection.cs:33)) → Avalonia TreeView **уничтожает и заново строит все 500+ карточек строк** (`BuildTreeRow` → `BuildInfobaseRow`, [`MainWindow.Avalonia.Tree.cs`](Workspace1C/Views/MainWindow.Avalonia.Tree.cs:46)) со всеми Binding/подписками/Transitions.
- Результат: за одно переключение выделяются сотни МБ (пик), из них ~70 МБ остаётся «держаться» ген.2-мусором/LOH-фрагментацией — WorkingSet ОС не возвращается (.NET не отдаёт сегменты), время каждого следующего переключения растёт из-за всё более частых и дорогих gen2/LOH GC. Это точно согласуется и с «до 70 МБ», и с «скорость падает с каждым переключением», и со скриншотом-зависанием.
- В **WPF** дерево виртуализировано (Recycling, [`MainWindow.xaml`](Workspace1C/Views/MainWindow.xaml:1203)), но при Reset TreeView заново разворачивает контейнеры видимого диапазона и пересоздаёт привязки; переход «плоское Избранное» ↔ «группированное Все» меняет состав корней полностью. Повторный клик на ту же вкладку всё равно шлёт Reset.

### Причина B — Avalonia: полный обход визуального дерева в AlignHeaderToRows

- `AlignHeaderToRows` выполняет `_tree.GetVisualDescendants().OfType<InfobaseRowCard>()` ([`MainWindow.Avalonia.Columns.cs`](Workspace1C/Views/MainWindow.Avalonia.Columns.cs:838)) — рекурсивный обход **всего** визуального дерева (десятки тысяч узлов при 500+ строках) при каждом выравнивании.
- `ContainerPrepared` на каждую строку зовёт `QueueHeaderAlign` ([`MainWindow.Avalonia.cs`](Workspace1C/Views/MainWindow.Avalonia.cs:1399)) — склейка в 1 проход уже есть, но каждый проход — это полный обход дерева → сотни мс «паузы» после каждого переключения.

### Причина C — обе платформы: остатки каскада выравнивания/ширины при переключении

- WPF: `MainTree.SizeChanged → AlignHeaderToData()` синхронно на каждую порцию контейнеров ([`MainWindow.Events.cs`](Workspace1C/Views/MainWindow.Events.cs:292)); `HeaderAlignStabilizeStep` делает до 8 итераций ([`MainWindow.Columns.cs`](Workspace1C/Views/MainWindow.Columns.cs:565)); `UpdateTreeMinWidthContent` меряет до 12 строк `Measure(∞)`.
- Это даёт оставшиеся 2 записи `CM_COLUMNS` на переключение (стабилизация + SizeChanged), хотя дорогой замер уже кэшируется.
- Avalonia: `UpdateEmptyState`/`QueueHeaderAlign` из `_groupNodesChanged` + каскад LayoutUpdated от массового появления контейнеров.

### Причина D — нет наблюдаемости

- Нечем измерить «сколько контейнеров пересоздано за переключение», «сколько карточек живёт», «сколько было проходов Align/стабилизации» — невозможно подтвердить устранение у 7OH иначе как субъективно.

---

## 3. Архитектурное решение

Принцип: **переключение вкладки не должно пересоздавать то, что уже есть.** Минимально трогаем визуальное дерево; всё, что можно измерить, измеряем флагом.

### Этап A (обе платформы): дифф-обновление коллекций вместо Reset + ранний выход

1. В [`ObservableRangeCollection.cs`](Workspace1C/Collections/ObservableRangeCollection.cs) добавить `ReplaceItemWise(IReadOnlyList<T> next)`:
   - если состав и порядок совпадают по `ReferenceEquals` — **не генерировать событий вообще** (критично для повторного клика на текущую вкладку и переходов с неизменным составом корней);
   - иначе — вычислить минимальный diff по reference-идентичности в стабильном порядке и эмитить только `Add`/`Remove` по изменённым позициям (без `Reset`).
2. Использовать вместо `ReplaceAll` в местах, отвечающих за переключение вкладок:
   - `GroupNodes` — [`ReplaceGroupNodes`](Workspace1C/ViewModels/MainViewModel.Tools.cs:91) (WPF) и [`ApplyFilterCore`](Workspace1C/ViewModels/MainViewModel.Avalonia.cs:1032) (Avalonia);
   - `FlatItems` (Avalonia, [`MainViewModel.Avalonia.cs`](Workspace1C/ViewModels/MainViewModel.Avalonia.cs:1053));
   - `GroupNodeViewModel.Items` в [`PopulateItems`](Workspace1C/ViewModels/GroupNodeViewModel.cs:357) и [`ResetFlatContent`](Workspace1C/ViewModels/GroupNodeViewModel.cs:403).
3. Ранний выход в VM:
   - [`RebuildGroupTreeCore`](Workspace1C/ViewModels/MainViewModel.Theme.cs:877) / `ApplyFilterCore`: если новый состав корней `ReferenceEquals` текущему (тот же `_flatNode`, тот же кэш) — не делать замены коллекции и не поднимать `TreeRebuilt`;
   - `ReplaceGroupNodes`: если `next` равен текущему содержимому — просто вернуться (сейчас даже повторный клик шлёт Reset).

Эффект:
- **WPF:** контейнеры строк, оставшихся на месте, не пересоздаются; скролл не сбрасывается; повторный Reset исключён.
- **Avalonia:** карточки существующих строк сохраняются; за переключение пересоздаются только реально изменившиеся строки (а не все 500+) → падение пиковых аллокаций и рабочего набора с ~70 МБ до единиц МБ.

### Этап B (Avalonia): убрать полный обход в AlignHeaderToRows

В [`MainWindow.Avalonia.Columns.cs`](Workspace1C/Views/MainWindow.Avalonia.Columns.cs:838):
- заменить `GetVisualDescendants().OfType<InfobaseRowCard>()` на получение **первой реализованной карточки по данным**: кэш «первой карточки», обновляемый из `ContainerPrepared` дерева (сброс при GroupNodes Reset/diff) + поиск первого реализованного контейнера через `ItemContainerGenerator` (не рекурсивный обход всего дерева);
- в плоском режиме — `ContainerFromIndex(0)`.

Эффект: `AlignHeaderToRows` из O(все визуальные узлы = десятки тысяч) превращается в O(первые строки) → выравнивание после переключения < 5–10 мс на 600 базах.

### Этап C (обе платформы): ограничить каскад выравнивания при чистом переключении вкладки

- **WPF** ([`MainWindow.Columns.cs`](Workspace1C/Views/MainWindow.Columns.cs:547)):
  - ввести признак «пересчёт при переключении вкладки» (ставится из `TreeRebuilt` при `IsListModeSwitchInProgress` и из `SizeChanged` в окне пересборки);
  - в `HeaderAlignStabilizeStep`: в этом сценарии делать **один** проход (лимит стабилизации 1, без цикла «до сходимости») — желанное `CM_COLUMNS: 0–1` на переключение (0 при кэш-хите);
  - проверить замену синхронного `AlignHeaderToData()` из `MainTree.SizeChanged` ([`MainWindow.Events.cs`](Workspace1C/Views/MainWindow.Events.cs:292)) на `QueueHeaderAlign()` (склейка серии SizeChanged) — кэш-хит `UpdateTreeMinWidthContent` уже защищает дорогой замер.
- **Avalonia** ([`MainWindow.Avalonia.cs`](Workspace1C/Views/MainWindow.Avalonia.cs:1633)): `_groupNodesChanged` при чистом переключении уже не ставит `QueueHeaderAlign` — оставить; `ContainerPrepared → QueueHeaderAlign` оставить (склейка работает), затраты закрывает Этап B.

### Этап D (Avalonia, отдельный шаг, средний риск): виртуализация строк LeveledTreeView

- В шаблоне [`LeveledTreeViewItem.Avalonia.cs`](Workspace1C/Controls/LeveledTreeViewItem.Avalonia.cs:103) `ItemsPresenter.ItemsPanel` по умолчанию StackPanel; попробовать `VirtualizingStackPanel` для панели элементов дерева (корневой уровень) и уровней строк.
- Обязательные проверки: высота контента/`ScrollViewer.CanContentScroll`, раскрытие/сворачивание групп, `BringIntoView`/прокрутка, работа кэша «первой карточки» (Этап B), поведение при `AutoScrollToSelectedItem=false`.
- **Флаг/откат:** включается настройкой в `trace.json` (например `AV_VIRTUALIZE_TREE`) с безопасным default; при регрессиях — отключить.
- Выполняется **после Этапа B** (при виртуализации `GetVisualDescendants` станет опаснее).
- Эффект при успехе: живых карточек одновременно ~видимый диапазон (~20), а не 500+ → память и время константны к размеру списка, «70 МБ на переключение» исчезают полностью.

### Этап E: диагностика за флагом (`CM_TREESWITCH` в trace.json)

- Avalonia (счётчики на смену вкладки, метка через `TreeRebuilding`/`TreeRebuilt`):
  - число `ContainerPrepared` за переключение (до/после);
  - число «живых» карточек: статический счётчик (инкремент в конструкторе `InfobaseRowCard`, декремент в `DetachedFromVisualTree`);
  - время `AlignHeaderToRows`;
  - время VM-части `ApplyFilterCore`.
- WPF:
  - число `SizeChanged`-событий за переключение, число итераций `HeaderAlignStabilizeStep`, число проходов `RevealAndSelectAfterRebuild`;
  - существующий `CM_COLUMNS`.
- Одна строка `CM_TREESWITCH: …` на переключение вкладки — 7OH снимет логи до/после правки.

### Этап F: тесты (Workspace1C.Tests)

- [`ObservableRangeCollectionTests.cs`](Workspace1C.Tests/ObservableRangeCollectionTests.cs):
  - `ReplaceItemWise` с тем же составом/порядком → **0 событий** (повторный клик по вкладке);
  - `ReplaceItemWise` с пересечением → только Add/Remove по реальным изменениям, без Reset;
  - на переходе «Все»→«Избранное» (600 баз) число событий меньше размера списка (часть строк сохраняется);
  - `ReplaceAll` остаётся для пересборок по изменению данных (без регрессий).
- [`ListSwitchPerformanceTests.cs`](Workspace1C.Tests/ListSwitchPerformanceTests.cs):
  - «30 переключений подряд»: медиана второй половины ≤ медианы первой × 2 + 10 мс (нет роста);
  - «повторный клик на вкладку»: VM-часть ≈ 0, коллекции не шлют событий;
  - существующие тесты (кэш/инвалидация `_listFilter`, один Reset, `Etap13ListStateTests`, `EtapHotkeysFavoritesTests`) — без регрессий.
- Если потребуется (упрощение `ReplaceGroupNodes`) — тест на ранний выход при `ReferenceEquals` составе корней.

### Этап G: версия 1.0.0.29, CHANGELOG, README

1. [`Workspace1C/Workspace1C.csproj`](Workspace1C/Workspace1C.csproj): `Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion` `1.0.0.28 → 1.0.0.29`.
2. [`CHANGELOG.md`](CHANGELOG.md): раздел `## [1.0.0.29] — <дата>` с блоком «Исправлено»: переключение вкладок «Все/Избранное/Недавние» перестало пересоздавать контейнеры строк целиком (дифф-обновление коллекций), на Linux устранён полный обход визуального дерева при выравнивании заголовка и (при включённой виртуализации) строки больше не создаются все подряд; память и задержка не растут с каждым переключением (Windows и Linux).
3. [`README.md`](README.md): бейдж версии (строка 3) и «актуальная версия — 1.0.0.29» (строка 9).
4. Issue #21 **не закрываем** — отчёт пользователю комментарием после подтверждения результата.

---

## 4. Сводка затронутых файлов

| Файл | Изменение | Этап |
|---|---|---|
| [`Workspace1C/Collections/ObservableRangeCollection.cs`](Workspace1C/Collections/ObservableRangeCollection.cs) | Метод `ReplaceItemWise` (diff, без событий при равенстве, без Reset) | A |
| [`Workspace1C/ViewModels/GroupNodeViewModel.cs`](Workspace1C/ViewModels/GroupNodeViewModel.cs) | `PopulateItems`/`ResetFlatContent` → `Items.ReplaceItemWise` | A |
| [`Workspace1C/ViewModels/MainViewModel.Tools.cs`](Workspace1C/ViewModels/MainViewModel.Tools.cs) | `ReplaceGroupNodes`: ранний выход при `ReferenceEquals`, diff вместо `ReplaceAll` | A |
| [`Workspace1C/ViewModels/MainViewModel.Theme.cs`](Workspace1C/ViewModels/MainViewModel.Theme.cs) | `RebuildGroupTreeCore`: ранний выход при неизменном составе корней | A |
| [`Workspace1C/ViewModels/MainViewModel.Avalonia.cs`](Workspace1C/ViewModels/MainViewModel.Avalonia.cs) | `ApplyFilterCore`: diff для `FlatItems`/`GroupNodes`, ранний выход | A |
| [`Workspace1C/Views/MainWindow.Avalonia.Columns.cs`](Workspace1C/Views/MainWindow.Avalonia.Columns.cs) | `AlignHeaderToRows`: кэш «первой карточки» вместо `GetVisualDescendants` | B |
| [`Workspace1C/Views/MainWindow.Columns.cs`](Workspace1C/Views/MainWindow.Columns.cs) | Лимит стабилизации 1 при переключении вкладки; признак сценария | C |
| [`Workspace1C/Views/MainWindow.Events.cs`](Workspace1C/Views/MainWindow.Events.cs) | `MainTree.SizeChanged` → склейка через `QueueHeaderAlign` (проверка/замена) | C |
| [`Workspace1C/Controls/LeveledTreeViewItem.Avalonia.cs`](Workspace1C/Controls/LeveledTreeViewItem.Avalonia.cs) | `VirtualizingStackPanel` в панели элементов (за флагом) | D |
| [`Workspace1C/Views/MainWindow.Avalonia.cs`](Workspace1C/Views/MainWindow.Avalonia.cs) | Диагностика `CM_TREESWITCH` (счётчики карточек/containers) | E |
| [`Workspace1C/Views/MainWindow.xaml.cs`](Workspace1C/Views/MainWindow.xaml.cs) | Диагностика: число итераций стабилизации/проходов Reveal | E |
| [`Workspace1C.Tests/ObservableRangeCollectionTests.cs`](Workspace1C.Tests/ObservableRangeCollectionTests.cs) | Тесты `ReplaceItemWise` (0 событий при равенстве; только diff; без Reset) | F |
| [`Workspace1C.Tests/ListSwitchPerformanceTests.cs`](Workspace1C.Tests/ListSwitchPerformanceTests.cs) | «30 переключений: нет роста»; «повторный клик ≈ 0» | F |
| [`Workspace1C/Workspace1C.csproj`](Workspace1C/Workspace1C.csproj), [`CHANGELOG.md`](CHANGELOG.md), [`README.md`](README.md) | Версия 1.0.0.29, раздел CHANGELOG, бейдж/строка версии | G |

---

## 5. Порядок работ

1. **Этап E** (диагностика) — чтобы все следующие изменения мерились до/после одним инструментом.
2. **Этап A** + тесты Этапа F — главное лекарство (diff + ранний выход), даёт снижение аллокаций и устранение повторного Reset.
3. **Этап B** — ускорение `AlignHeaderToRows` (Avalonia).
4. **Этап C** — ограничение каскада выравнивания (WPF; Avalonia — проверка склейки).
5. **Этап D** — виртуализация строк Avalonia за флагом (отдельный коммит; откат при регрессиях).
6. **Прогон тестов** (WPF-профиль), сборка Avalonia, ручная проверка на обеих платформах.
7. **Этап G** — версия 1.0.0.29, CHANGELOG, README; комментарий в issue #21 (без закрытия).

---

## 6. Критерии готовности

1. **WPF:** при включённом `CM_COLUMNS` на переключение вкладки — не более 0–1 записи; повторный клик на текущую вкладку — 0 записей. Задержка 20 переключений подряд не растёт (медиана второй половины ≤ первой × 2 + 10 мс). Рабочий набор после прогрева на цикл переключений не растёт; после ручного GC возвращается к базовому уровню.
2. **Avalonia:** в логах `CM_TREESWITCH` число созданных карточек за переключение ≤ 10% размера списка (при включённой виртуализации — ≤ видимого диапазона); `AlignHeaderToRows` < 10 мс на 600 базах; WorkingSet не растёт за 20 переключений; задержка не растёт.
3. **Тесты:** новые тесты `ReplaceItemWise` и «нет роста» зелёные; без регрессий существующие (`ListSwitchPerformanceTests`, `ObservableRangeCollectionTests`, `Etap13ListStateTests`, `EtapHotkeysFavoritesTests`).
4. **Функциональные регрессии отсутствуют:** скролл (issue #252), восстановление выделения после правок, «Найти в списке» (issue #285), раскрытие/сворачивание групп (issue #119), выравнивание заголовка (issue #214/#309) — проверено на Windows (WPF) и Linux (Avalonia).
5. **Версия 1.0.0.29**, раздел в CHANGELOG.md, обновлённый README.md, комментарий в issue #21 (issue открыт).
