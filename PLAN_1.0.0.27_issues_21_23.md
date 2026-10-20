# План исправлений 1.0.0.27 — issues #21, #23

Версия: **1.0.0.26 → 1.0.0.27** (увеличивается только последнее число).
Issues: **#21 «Долгое переключение между вкладками "Все", "Избранное"»** (также «Недавние»), **#23 «Заголовок окна проверки обновлений»** — оба открыты. Issues **НЕ закрывать** (закрытие — только после подтверждения пользователем).

---

## Часть 1. Issue #21 «Долгое переключение между вкладками "Все", "Избранное"» — устранение каскада пересчётов и роста задержки/памяти

Хронология (из комментариев):
- 7OH: на «Недавние» тоже долго; список в памяти; из «Избранного» обратно долговато.
- 7OH: после запуска память меньше, после 10 переключений «Недавние» ↔ обратно память выросла «и не опускается».
- 1.0.0.26 ввёл кэш отфильтрованных списков и массовые обновления коллекций — **не помогло**.
- 7OH: «легче не стало. Задержка становится больше с каждым переключением. И память, выделенная приложению — тоже».
- 7OH: за 6 переключений с интервалом 1 сек — **12 строк «CM_COLUMNS: total=…»** (2 пересчёта колонок на переключение); разница времени между переключениями растёт (17:13:06 → 17:13:18); приложение завершает работу.
- ArtyoZ: «Подтверждаю слова 7OH, релиз 1.0.0.26 улучшений не принёс».

### 1.1. Корневая причина

Кэширование фильтрации и массовые `Reset` (Этап A/B/C плана 1.0.0.26) убрали *пересоздание данных*, но **каскад UI-пересчётов при каждом переключении вкладки остался и именно он даёт «паузу», 12 строк CM_COLUMNS и рост**. По коду цепочка при каждом клике по вкладке (WPF):

1. `ListViewMode` setter → `RebuildGroupTreeCore()` без инвалидации кэша ([`MainViewModel.Display.cs`](Workspace1C/ViewModels/MainViewModel.Display.cs:143), [`MainViewModel.Theme.cs`](Workspace1C/ViewModels/MainViewModel.Theme.cs:876)).
2. Плоский режим: `_flatNode.ResetFlatContent(...)` (4× `OnPropertyChanged` + `Infobases.Clear/Add` + `PopulateItems` → `Items.ReplaceAll`) ([`GroupNodeViewModel.cs`](Workspace1C/ViewModels/GroupNodeViewModel.cs:403)); затем `ReplaceGroupNodes` → `TreeRebuilding` → `GroupNodes.ReplaceAll(next)` → `TreeRebuilt` ([`MainViewModel.Tools.cs`](Workspace1C/ViewModels/MainViewModel.Tools.cs:91)).
3. `TreeRebuilt` (WPF) → `RestoreTreeKeyboardFocus()` **и** `QueueHeaderAlign()` ([`MainWindow.xaml.cs`](Workspace1C/Views/MainWindow.xaml.cs:304)).
4. `QueueHeaderAlign` → `HeaderAlignStabilizeStep` — до 8 итераций; каждая итерация: `AlignHeaderToData` → `UpdateTreeMinWidthContent` (замер до **12 видимых строк** `Measure(∞, h)`) + `SyncHeaderWidthWithList` → `UpdateTreeMinWidth` → при изменении `effective` **пишется `CM_COLUMNS`** ([`MainWindow.Columns.cs`](Workspace1C/Views/MainWindow.Columns.cs:544), [`MainWindow.Columns.cs`](Workspace1C/Views/MainWindow.Columns.cs:601), [`MainWindow.Columns.cs`](Workspace1C/Views/MainWindow.Columns.cs:781), [`MainWindow.Columns.cs`](Workspace1C/Views/MainWindow.Columns.cs:869)).
5. `RestoreTreeKeyboardFocus` планирует `RevealAndSelectAfterRebuild` на Render ([`MainWindow.Tree.cs`](Workspace1C/Views/MainWindow.Tree.cs:263)) — а он вызывает **принудительный `MainTree.UpdateLayout()`** при каждом переключении ([`MainWindow.Tree.cs`](Workspace1C/Views/MainWindow.Tree.cs:364)). Если `SelectedInfobase` **недостижим в новом режиме вкладки** (например, выбранная база не входит в «Избранное»), `FindTargetContainer` возвращает null, и выполняются **повторные проходы с новой полной раскладкой до `MaxRevealAttempts = 6`** ([`MainWindow.Tree.cs`](Workspace1C/Views/MainWindow.Tree.cs:396)).
6. `MainTree.SizeChanged` → ещё один `QueueHeaderAlign()` ([`MainWindow.Events.cs`](Workspace1C/Views/MainWindow.Events.cs:286)) — повторная стабилизация.
7. Прокрутки: `OnListScroll_ScrollChanged` (изменение viewport → `UpdateTreeMinWidth`) ([`MainWindow.Scroll.cs`](Workspace1C/Views/MainWindow.Scroll.cs:289)) и `OnTreeScroll_ScrollChanged` (`ExtentWidthChange` + `_treeMinWidthTotal > viewport` → `UpdateTreeMinWidthContent` + `UpdateTreeMinWidth`) ([`MainWindow.Scroll.cs`](Workspace1C/Views/MainWindow.Scroll.cs:319)).

**Итог на одно переключение:** 1–2 принудительные полные раскладки, 2–3 цикла стабилизации выравнивания (по 1–3 итерации с замером до 12 строк), 2+ пересчёта минимальной ширины с записью CM_COLUMNS — «пауза» в секунды на больших списках.

**Рост задержки с каждым переключением** — самоусиление: чем тяжелее раскладка (мусор от массовых пересозданий, больше реализованных контейнеров), тем больше итераций стабилизации/попыток `Reveal` успевает пройти за переключение, тем заметнее следующее. Плюс повторные проходы `RevealAndSelectAfterRebuild` при недостижимой цели добавляют по полной раскладке на каждую попытку.

**Рост памяти** — основные источники:
- режим «Все» с включённой группировкой **пересоздаёт всё группированное дерево** (`GroupNodeViewModel`, обёртки `PinnedInfobaseItem`) на каждое переключение вкладки ([`MainViewModel.Theme.cs`](Workspace1C/ViewModels/MainViewModel.Theme.cs:904));
- `UpdateLayout`/`Measure(∞)` и пересоздание контейнеров при `Reset` генерируют gen2-мусор; контейнеры `TreeViewItem` материализуются/освобождаются пачками, и без GC рабочее множество растёт;
- (проверяется профилировщиком) возможен рост реализованных контейнеров, если при `Reset`/`UpdateLayout` часть контейнеров остаётся в кэше `VirtualizingStackPanel`.

**Avalonia/Linux** (подтверждается 7OH паттерном, но CM_COLUMNS — WPF-диагностика): `ApplyFilterCore` ([`MainViewModel.Avalonia.cs`](Workspace1C/ViewModels/MainViewModel.Avalonia.cs:1027)) даёт те же события; `_groupNodesChanged` → `UpdateEmptyState + QueueHeaderAlign` ([`MainWindow.Avalonia.cs`](Workspace1C/Views/MainWindow.Avalonia.cs:1633)), `ContainerPrepared` → `QueueHeaderAlign` на каждую материализацию строки ([`MainWindow.Avalonia.cs`](Workspace1C/Views/MainWindow.Avalonia.cs:1392)), `TreeRebuilt` → `RestoreTreeSelection` ([`MainWindow.Avalonia.cs`](Workspace1C/Views/MainWindow.Avalonia.cs:278)); стабилизация выравнивания — аналог `QueueHeaderAlign` ([`MainWindow.Avalonia.Columns.cs`](Workspace1C/Views/MainWindow.Avalonia.Columns.cs:803)).

#### Как проверить гипотезы по коду/на билде

- Включить флаг `CM_COLUMNS` в `trace.json` и переключать вкладки: до правки — ≥2 строки на переключение, время между строками растёт; после правки — 0–1 строка на переключение, без роста.
- Профилировщик (VS/`dotnet-trace` async/CPU) на одном переключении: проверить доли `Layout/Measure` (`MainTree.UpdateLayout`, `UpdateTreeMinWidthContent.MeasureRow`), `RevealAndSelectAfterRebuild` (число попыток) и построения дерева «Все».
- Временный контрольный тест: отключить `RestoreTreeKeyboardFocus` в обработчике `TreeRebuilt` (1 строка) — если переключение резко ускоряется, гипотеза H1 (пп. 3–5 цепочки) подтверждена.

### 1.2. Решение (поэтапно, каждый этап самостоятелен)

#### Этап A. Не восстанавливать выделение/фокус при чистом переключении вкладки (WPF)

- В `MainViewModel` ввести признак «идёт чистое переключение вкладки»: флаг `_listModeSwitchInProgress`, выставляется в setter `ListViewMode` вокруг `RebuildGroupTreeCore()` и сбрасывается после (либо параметр в событии `TreeRebuilt`).
- В обработчике `TreeRebuilt` ([`MainWindow.xaml.cs`](Workspace1C/Views/MainWindow.xaml.cs:304)): при чистом переключении вкладки вызывать **только `QueueHeaderAlign()`**, а `RestoreTreeKeyboardFocus()` не вызывать (старое выделение в новом режиме в большинстве случаев недостижимо; прокрутку не дёргаем).
- В `RevealAndSelectAfterRebuild` ([`MainWindow.Tree.cs`](Workspace1C/Views/MainWindow.Tree.cs:293)): **ранний выход без повторных проходов**, если цель недостижима в текущем дереве: `FindGroupNodeByInfobase(ib)` вернул null И база отсутствует в плоском списке `_viewModel.GroupNodes` текущего режима. Вынести проверку в хелпер VM (см. Этап C/тесты): `bool IsRevealTargetPresent(object target)`.
- Отдельный путь «Найти в списке» (`_revealFindInListPending`, issue #285) сохраняет прежнее поведение (там `MaterializeRevealTarget` нужен и вызывается только по явной команде).

#### Этап B. Сократить каскад колонок/выравнивания при переключении вкладок (WPF, затем Avalonia)

- **B1. Кэш-хит в `UpdateTreeMinWidthContent`** ([`MainWindow.Columns.cs`](Workspace1C/Views/MainWindow.Columns.cs:973)): если верхняя видимая строка (`_treeMinWidthAnchorData`) и состав/ширины колонок не изменились с прошлого замера — не выполнять `Measure` по 12 строкам, вернуть кэш `_treeMinWidthContent`. (Уже есть guard по смене вершины в `OnTreeScroll_ScrollChanged`; распространить на путь стабилизации.)
- **B2. Ограничить стабилизацию выравнивания** `HeaderAlignStabilizeStep` ([`MainWindow.Columns.cs`](Workspace1C/Views/MainWindow.Columns.cs:562)): при переключении вкладок достаточно 1–2 итераций; если за проход компенсатор и `_treeMinWidthContent` не изменились — завершить цикл (счётчик `_headerAlignStabilizeCount` уже есть, снизить лимит для сценария переключения вкладки либо завершать при втором подряд «без изменений»).
- **B3. Убрать двойную стабилизацию**: `MainTree.SizeChanged → QueueHeaderAlign` ([`MainWindow.Events.cs`](Workspace1C/Views/MainWindow.Events.cs:286)) не должен запускать новый цикл, если стабилизация уже была выполнена для этой пересборки (метка «последняя стабилизация применена» сбрасывается только при изменении данных/размера окна); либо вызывать `AlignHeaderToData()` напрямую без повторного цикла.
- **B4. Дебаунс пересчёта ширины прокрутками**: `OnListScroll_ScrollChanged` ([`MainWindow.Scroll.cs`](Workspace1C/Views/MainWindow.Scroll.cs:289)) — использовать существующий `QueueTreeMinWidthRefresh()` (Background) вместо синхронного `UpdateTreeMinWidth()`.
- **Avalonia**: то же по смыслу — кэш-хит замера ширины, ограничение `QueueHeaderAlign`-цикла в [`MainWindow.Avalonia.Columns.cs`](Workspace1C/Views/MainWindow.Avalonia.Columns.cs:803), не запускать `QueueHeaderAlign` из `_groupNodesChanged` при чистой смене вкладки без изменения состава колонок; `ContainerPrepared` уже склеивается флагом — проверить, что серия материализаций даёт не более одного цикла.

#### Этап C. Кэш построенного дерева режима «Все» (VM, обе платформы)

- В `MainViewModel` ввести кэш группированного дерева «Все» (список корневых `GroupNodeViewModel`), единый с `_listFilter` по инвалидации: `_listFilter.Invalidate()` (изменение данных/фильтров/сортировки) сбрасывает и этот кэш.
- `RebuildGroupTreeCore` для режима «Все» при группировке: если кэш действителен — переиспользовать те же узлы, обновить только контейнеры (`GroupNodes.ReplaceAll(кэш)`) без пересоздания `GroupNodeViewModel`/`PinnedInfobaseItem`.
- Восстановление состояния после переключения: развёрнутость по `NodeKey` и `SelectedGroupNode` по `Id` (логика `ApplyExpandedState`/`FindGroupNodeById` уже есть — перенести вызов на переиспользуемые узлы).
- Это убирает основной источник мусора и памяти (#21: «память растёт с каждым переключением») в группированном режиме «Все».

#### Этап D. Аудит подписок (страховка от утечек)

- Проверить в `MainWindow`/`MainWindow.Avalonia`: все подписки `TreeRebuilding`/`TreeRebuilt`/`SizeChanged`/`Loaded`/`ContainerPrepared`/`Bounds` — единоразовые (не добавляются на каждую пересборку); отписки в `OnClosing` (образец — `UnsubscribeLanguageChanged`, [`MainViewModel.Theme.cs`](Workspace1C/ViewModels/MainViewModel.Theme.cs:855)).
- Avalonia: у `_tree.GetObservable(Visual.BoundsProperty).Subscribe(...)` ([`MainWindow.Avalonia.cs`](Workspace1C/Views/MainWindow.Avalonia.cs:1398)) — проверить lifetime (диспоуз при закрытии), чтобы подписка не держала окно.

#### Этап E. Диагностика за флагом

- Под флагом `CM_COLUMNS` (или новый `CM_TREESWITCH`) логировать на переключение вкладки: число итераций стабилизации, число проходов `RevealAndSelectAfterRebuild` (attempt), факт кэш-хита B1, время VM-части и раскладки. Нужно для контрольных замеров пользователем (7OH) и для подтверждения «задержка не растёт».

### 1.3. Критерии успеха

- Переключение «Все» → «Избранное» → «Все» на списке 500+ баз (включая отрисовку) — ощутимо мгновенное, замер < 100–200 мс.
- 10–20 последовательных переключений: задержка **не растёт** (медиана второй половины цикла ≤ медианы первой).
- Память: рабочий набор (Task Manager / `WorkingSet64`) после прогрева не растёт на цикл переключений; после ручного GC возвращается к базовому уровню (нет накопления контейнеров/дерева).
- В логах при включённом `CM_COLUMNS`: **не более 0–1 записи на переключение** (сейчас ≥2), без роста времени между записями.
- Повторные проходы `RevealAndSelectAfterRebuild` (attempt > 0) при переключении вкладок отсутствуют (видно по диагностике Этапа E).

### 1.4. Тесты

Существующие (прогнать без регрессий): [`Workspace1C.Tests/InfobaseListFilterTests.cs`](Workspace1C.Tests/InfobaseListFilterTests.cs) (кэш/инвалидация), [`Workspace1C.Tests/ObservableRangeCollectionTests.cs`](Workspace1C.Tests/ObservableRangeCollectionTests.cs) (один Reset), [`Workspace1C.Tests/ListSwitchPerformanceTests.cs`](Workspace1C.Tests/ListSwitchPerformanceTests.cs) (VM-часть переключения), [`Workspace1C.Tests/Etap13ListStateTests.cs`](Workspace1C.Tests/Etap13ListStateTests.cs), [`Workspace1C.Tests/EtapHotkeysFavoritesTests.cs`](Workspace1C.Tests/EtapHotkeysFavoritesTests.cs).

Добавить/обновить:
- `RevealTargetPresent`-логика (Этап A): если вынесена проверка «цель есть в дереве текущего режима» — юнит-тест: база вне «Избранного» при режиме Favorites → недостижима; при режиме All (группировка) → достижима по группе/«Без группы».
- Кэш дерева «Все» (Этап C): тест — при чистом переключении вкладки с неизменными данными узлы дерева «Все» не пересоздаются (`ReferenceEquals` корней до/после) и кэш инвалидируется при добавлении/удалении базы и смене сортировки.
- [`ObservableRangeCollectionTests.cs`](Workspace1C.Tests/ObservableRangeCollectionTests.cs): дополнить — `ResetFlatContent`/`PopulateItems` порождают только Reset (не `Add`/`Remove` на элемент) на 600 базах.
- [`ListSwitchPerformanceTests.cs`](Workspace1C.Tests/ListSwitchPerformanceTests.cs): добавить сценарий «30 переключений подряд» с проверкой «нет роста» (медиана последних 10 ≤ медианы первых 10 + запас на флуктуации, например ×2 или +10 мс); порог 200 мс сохранить.

---

## Часть 2. Issue #23 «Заголовок окна проверки обновлений» — точечная правка заголовка

Текст issue: «У этого окна заголовок всегда одинаковый ) Может быть просто "Проверка обновлений" в заголовок ?» — окно результата ручной проверки обновлений приложения во **всех исходах** показывает заголовок «Доступна новая версия», даже когда версия актуальна или произошла ошибка.

### 2.1. Диагноз

`UpdateService.CheckForUpdatesManualAsync` печатает результат в `IDialogService.ShowInfo/ShowError` с жёстким заголовком `Update.NewVersionAvailable` («Доступна новая версия»), независимо от исхода:

- **WPF** [`UpdateService.cs`](Workspace1C/Services/UpdateService.cs:138): ошибка проверки (строки 145–147), «версия актуальна» (152–155), catch (174–176), а также все ошибки загрузки/установки через `ShowErrorOnUi` (752–753).
- **Avalonia** [`UpdateService.Avalonia.cs`](Workspace1C/Services/UpdateService.Avalonia.cs:182): ошибка проверки (189–191), «версия актуальна» (197–199), catch (214–216).

Ключ «Проверка обновлений» — `Updates.CheckTitle` — уже существует и локализован: ru «Проверка обновлений» ([`ru.json`](Workspace1C/Localization/Languages/ru.json:2638)), en «Check for updates» ([`en.json`](Workspace1C/Localization/Languages/en.json:2638)). Новые ключи перевода не нужны.

### 2.2. Решение (WPF + Avalonia)

Заменить заголовок результата проверки `Update.NewVersionAvailable` на `Updates.CheckTitle`:

**WPF — [`Workspace1C/Services/UpdateService.cs`](Workspace1C/Services/UpdateService.cs):**
- строка 146: `LocalizationManager.T("Update.NewVersionAvailable")` → `LocalizationManager.T("Updates.CheckTitle")` (ShowError «не удалось проверить»);
- строка 153: то же (ShowInfo «версия актуальна»);
- строка 175: то же (catch);
- строка 753 (`ShowErrorOnUi`): заголовок ошибок загрузки/установки → `Updates.CheckTitle` — единый заголовок окна проверки обновлений во всех исходах.

**Avalonia — [`Workspace1C/Services/UpdateService.Avalonia.cs`](Workspace1C/Services/UpdateService.Avalonia.cs):**
- строки 190–191: ShowError → `Updates.CheckTitle`;
- строки 198–199: ShowInfo → `Updates.CheckTitle`;
- строки 215–216: catch → `Updates.CheckTitle`.

Опционально (единая точка + тестируемость): в обоих `UpdateService` ввести приватную константу заголовка результата, например `private const string CheckResultTitleKey = "Updates.CheckTitle";` и использовать её во всех местах замены.

**Намеренно НЕ меняем:**
- окно собственно «Доступна новая версия» `UpdateAvailableWindow` (заголовок `Update.NewVersionAvailable` там корректен — [`UpdateAvailableWindow.xaml`](Workspace1C/Services/UpdateAvailableWindow.xaml:6), [`UpdateAvailableWindow.xaml.cs`](Workspace1C/Services/UpdateAvailableWindow.xaml.cs:39));
- диалог «новая версия доступна, но установка невозможна» (AppImage/deb) `ShowManualUpdateDialog` ([`UpdateService.Avalonia.cs`](Workspace1C/Services/UpdateService.Avalonia.cs:1320)) — там заголовок «Доступна новая версия» уместен по смыслу;
- окно проверки обновлений **конфигураций** `UpdateCheckWindow` — его заголовок уже `Updates.CheckTitle` ([`UpdateCheckWindow.xaml`](Workspace1C/Views/UpdateCheckWindow.xaml:6), [`UpdateCheckWindow.Avalonia.cs`](Workspace1C/Views/UpdateCheckWindow.Avalonia.cs:102)).

### 2.3. Проверка

- WPF: «Настройки → О программе → Проверить обновления» при актуальной версии — окно «Установлена актуальная версия.» с заголовком **«Проверка обновлений»**; при ошибке сети — заголовок так же «Проверка обновлений».
- Avalonia: то же на Linux; диалог «Доступна новая версия» (если она есть и автообновление выключено) сохраняет свой заголовок.

### 2.4. Тесты

- `UpdateService` не покрыт юнит-тестами (HttpClient/GitHub API); добавить:
  - тест локализации: `LocalizationManager.T("Updates.CheckTitle")` не пуст и **≠** `T("Update.NewVersionAvailable")` в ru и en (место: существующие `LocalizationCoverageServicesTests`/`LocalizationFallbackTests` или рядом);
  - если введена константа `CheckResultTitleKey` — тест константы (`== "Updates.CheckTitle"`), чтобы решение не «разъехалось» с локализацией.

---

## Часть 3. Сводка затронутых файлов

| Issue | Файл | Изменение |
|---|---|---|
| #21 | [`Workspace1C/ViewModels/MainViewModel.Display.cs`](Workspace1C/ViewModels/MainViewModel.Display.cs) | Флаг «чистое переключение вкладки» вокруг `RebuildGroupTreeCore` в setter `ListViewMode` |
| #21 | [`Workspace1C/ViewModels/MainViewModel.cs`](Workspace1C/ViewModels/MainViewModel.cs) | Кэш дерева «Все» (инвалидация как у `_listFilter`); хелпер достижимости цели |
| #21 | [`Workspace1C/ViewModels/MainViewModel.Theme.cs`](Workspace1C/ViewModels/MainViewModel.Theme.cs) | `RebuildGroupTreeCore` — переиспользование кэша «Все», `_listFilter.Invalidate` сбрасывает и кэш дерева |
| #21 | [`Workspace1C/Views/MainWindow.xaml.cs`](Workspace1C/Views/MainWindow.xaml.cs) | `TreeRebuilt` — без `RestoreTreeKeyboardFocus` при чистом переключении вкладки |
| #21 | [`Workspace1C/Views/MainWindow.Tree.cs`](Workspace1C/Views/MainWindow.Tree.cs) | `RevealAndSelectAfterRebuild` — ранний выход при недостижимой цели (кроме «Найти в списке») |
| #21 | [`Workspace1C/Views/MainWindow.Columns.cs`](Workspace1C/Views/MainWindow.Columns.cs) | Кэш-хит `UpdateTreeMinWidthContent`, лимит итераций `HeaderAlignStabilizeStep`, анти-дубль стабилизации от `SizeChanged` |
| #21 | [`Workspace1C/Views/MainWindow.Events.cs`](Workspace1C/Views/MainWindow.Events.cs) | `MainTree.SizeChanged` — не запускать повторный цикл выравнивания |
| #21 | [`Workspace1C/Views/MainWindow.Scroll.cs`](Workspace1C/Views/MainWindow.Scroll.cs) | `OnListScroll_ScrollChanged` — дебаунс через `QueueTreeMinWidthRefresh` |
| #21 | [`Workspace1C/ViewModels/MainViewModel.Avalonia.cs`](Workspace1C/ViewModels/MainViewModel.Avalonia.cs) | Аналоги Этапа A/C в `ApplyFilterCore`/`SetListMode` (кэш «Все», признак переключения вкладки) |
| #21 | [`Workspace1C/Views/MainWindow.Avalonia.cs`](Workspace1C/Views/MainWindow.Avalonia.cs) | Не запускать `QueueHeaderAlign` из `_groupNodesChanged` при чистой смене вкладки; lifetime подписки Bounds |
| #21 | [`Workspace1C/Views/MainWindow.Avalonia.Columns.cs`](Workspace1C/Views/MainWindow.Avalonia.Columns.cs) | Кэш-хит/лимит цикла выравнивания (аналог WPF) |
| #23 | [`Workspace1C/Services/UpdateService.cs`](Workspace1C/Services/UpdateService.cs) | Заголовок результата проверки → `Updates.CheckTitle` (стр. 146, 153, 175, 753) |
| #23 | [`Workspace1C/Services/UpdateService.Avalonia.cs`](Workspace1C/Services/UpdateService.Avalonia.cs) | То же (стр. 190–191, 198–199, 214–216); константа `CheckResultTitleKey` |
| Тесты | [`Workspace1C.Tests/ListSwitchPerformanceTests.cs`](Workspace1C.Tests/ListSwitchPerformanceTests.cs) | Сценарий «нет роста задержки и VM-времени на 30 переключениях» |
| Тесты | [`Workspace1C.Tests/ObservableRangeCollectionTests.cs`](Workspace1C.Tests/ObservableRangeCollectionTests.cs) | Только Reset при `ResetFlatContent`/`PopulateItems` на 600 базах |
| Тесты | новые/существующие | Тест кэша дерева «Все» (ReferenceEquals корней), тест достижимости цели, тест константы заголовка №23 |

---

## Часть 4. Версия 1.0.0.27, CHANGELOG, README

1. **Версия** — [`Workspace1C/Workspace1C.csproj`](Workspace1C/Workspace1C.csproj:62), строки 62–65: `Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion` — `1.0.0.26` → `1.0.0.27` (увеличивается только последнее число).
2. **CHANGELOG.md** — добавить раздел `## [1.0.0.27] — <дата>` в начало (после шапки, перед `## [1.0.0.26]`, [`CHANGELOG.md`](CHANGELOG.md:13)) с блоком `### Исправлено`:
   - **#21**: переключение вкладок «Все/Избранное/Недавние»: устранён каскад пересчётов колонок и принудительных раскладок при каждом переключении (CM_COLUMNS не множатся), дерево режима «Все» кэшируется, выделение не «догоняет» недостижимую цель; задержка и память больше не растут (Windows и Linux);
   - **#23**: окно проверки обновлений приложения во всех исходах показывает заголовок «Проверка обновлений» (вместо «Доступна новая версия») — Windows и Linux.
3. **README.md** — обновить:
   - строку 3: бейдж `Версия-1.0.0.26` → `Версия-1.0.0.27` ([`README.md`](README.md:3));
   - строку 9: «актуальная версия — **1.0.0.26**» → «**1.0.0.27**» ([`README.md`](README.md:9));
   - при необходимости — формулировку пункта о вкладках списка баз (если такой пункт есть в шапке возможностей).
4. Issues #21 и #23 **не закрываем** — после релиза отчёт идёт комментариями в issues (по подтверждению результата пользователем), а не штатным закрытием из плана.