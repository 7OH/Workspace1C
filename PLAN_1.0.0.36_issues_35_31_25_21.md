# План исправления 1.0.0.36 — issues #35, #31, #25 (доработка автовыбора), #21 (итерация B), замена иконок приложения

Версия: **1.0.0.35 → 1.0.0.36** (увеличивается только последнее число; один релиз на все изменения, по практике прошлых версий, где несколькими issues закрывалась одна версия — см. [`Workspace1C.csproj`](Workspace1C/Workspace1C.csproj:62)).
Issues: **#35, #31, #25, #21** — остаются **открытыми**; после релиза в каждый добавляется комментарий «что исправлено и в какой версии». Каждый пункт работ выполняется отдельной задачей через `new_task` (см. раздел 1).

**Feasibility:** четыре issues лежат в независимых зонах кода:
- #35 — путь «правка свойств базы» и обновление строки при активном отборе ([`MainViewModel.Commands.cs`](Workspace1C/ViewModels/MainViewModel.Commands.cs), [`MainViewModel.Avalonia.cs`](Workspace1C/ViewModels/MainViewModel.Avalonia.cs), модельные уведомления [`Infobase`](Workspace1C/Models/Infobase.cs));
- #31 — механизм reveal «перехода к строке» ([`MainWindow.Tree.cs`](Workspace1C/Views/MainWindow.Tree.cs), [`MainWindow.Avalonia.Scroll.cs`](Workspace1C/Views/MainWindow.Avalonia.Scroll.cs), [`LeveledTreeView.Avalonia.cs`](Workspace1C/Controls/LeveledTreeView.Avalonia.cs)) + тесты [`GoToRowTests.cs`](Workspace1C.Tests/GoToRowTests.cs);
- #25 — окно «Импорт из кластера 1С» и VM автовыбора ([`ClusterImportViewModel.cs`](Workspace1C/ViewModels/ClusterImportViewModel.cs), окна WPF/Avalonia, тесты [`ClusterImportViewModelTests.cs`](Workspace1C.Tests/ClusterImportViewModelTests.cs));
- #21 — переключение вкладок и накопление памяти в WPF ([`ObservableRangeCollection.cs`](Workspace1C/Collections/ObservableRangeCollection.cs), [`MainViewModel.Tools.cs`](Workspace1C/ViewModels/MainViewModel.Tools.cs), диагностика [`MainWindow.xaml.cs`](Workspace1C/Views/MainWindow.xaml.cs), [`MainWindow.Avalonia.cs`](Workspace1C/Views/MainWindow.Avalonia.cs), [`TraceFlags.cs`](Workspace1C/Services/TraceFlags.cs));
- иконки — только asset-файлы `Workspace1C/` и `package/linux/app.png` (csproj при этом не меняется: имена ресурсов `app.ico`/`tray.ico`/`app_icon_preview.png`/`tray_icon_preview.png` сохраняются).

Пересечения по файлам отсутствуют; общая часть (версия/CHANGELOG/README/rel_notes/сборка/релиз) выполняется последней.

---

## 1. Декомпозиция на задачи (порядок и зависимости)

Каждая задача — **отдельный `new_task`**. Задачи 1–5 независимы и могут запускаться параллельно; задача 6 зависит от 1–5; задача 7 от 6; задача 8 от 7.

| № | Задача (new_task, режим Code) | Зависит от |
|---|---|---|
| 1 | **#35** — обновление строки при правке свойств базы с активным отбором (код + тесты) | — |
| 2 | **#31** — переход к строке: диагностика reveal + гарантия «домашней» строки (код + тесты GoToRowTests) | — |
| 3 | **#25** — импорт: автовыбор единственного кластера и сразу список баз в обоих входах (код + тесты) | — |
| 4 | **#21** — вкладки: диагностика CM_TREESWITCH v2, гипотезы, фикс накопления памяти на Windows (код + эксперимент) | — |
| 5 | **Иконки** — замена assets из `design/icons` + `package/linux/app.png`; проверка палитр | — |
| 6 | **Версия 1.0.0.36** + CHANGELOG.md, README.md, rel_notes_1.0.0.36.md | 1–5 |
| 7 | **Сборка** Windows и Linux + полный прогон тестов + ручная проверка сценариев #35/#31/#25/#21 и иконок | 6 |
| 8 | **Релиз**: git push в main, GitHub release `v1.0.0.36` (2 файла), комментарии в issues #35/#31/#25/#21 («исправлено в 1.0.0.36», issues не закрывать) | 7 |

После утверждения плана координатор последовательно создаёт задачи 1–5 (каждая отдельным `new_task` в mode `code`), затем 6 → 7 → 8.

---

## 2. Issue #35 «Не обновляет данные строки при отборах»

### 2.1. Анализ причины (по коду 1.0.0.35)

1. Оба пути правки свойств базы применяют изменения **прямым присваиванием полей без уведомлений**:
   - WPF: [`MainViewModel.Commands.cs`](Workspace1C/ViewModels/MainViewModel.Commands.cs:294-330) — `target.Name = dialog.Result.Name;` и т.д.;
   - Avalonia: [`MainViewModel.Avalonia.cs`](Workspace1C/ViewModels/MainViewModel.Avalonia.cs:1453-1486) — то же для `ib`.
2. [`Infobase`](Workspace1C/Models/Infobase.cs) реализует `INotifyPropertyChanged`, но `Name`, `Group`, `Description`, `PlatformVersion`, `Connection`, `Tags` и др. — **auto-свойства** (уведомления поднимают только `IsFavorite`, `IsPinned`, `IsSelected`, `IsBatchSelected`, `IsRunning` и пр. — см. строки 47-143). Прямое присваивание не порождает `PropertyChanged`.
3. При активном отборе (тег/поиск/«Избранное»/«Недавние») или при выключенной группировке дерево строится плоским узлом: [`RebuildGroupTreeCore`](Workspace1C/ViewModels/MainViewModel.Theme.cs:892) при `IsFilterModeActive()` идёт в ветку [`_flatNode.ResetFlatContent(...)`](Workspace1C/ViewModels/MainViewModel.Theme.cs:909-918). Сами базы (те же ссылки `Infobase`) кладутся в `Items` через `ReplaceItemWise` ([`GroupNodeViewModel.PopulateItems`](Workspace1C/ViewModels/GroupNodeViewModel.cs:389-427)).
4. [`ResetFlatContent`](Workspace1C/ViewModels/GroupNodeViewModel.cs:438-464) переиспользует **постоянный** `_flatNode`; [`ReplaceGroupNodes`](Workspace1C/ViewModels/MainViewModel.Tools.cs:91-120) при неизменном списке корней (тот же один `_flatNode`) делает **ранний выход** — коллекции `GroupNodes` не трогаются, контейнеры строк не пересоздаются, событий нет.
5. Итог: в плоском режиме контейнер строки сохраняется с прежним DataContext (тот же `Infobase`) и связка при `{Binding Name}` **не перечитывает значение** (источник не уведомляет). В режиме с группировкой (без отборов) `RebuildGroupTree` сбрасывает кэш и строит новые узлы → новые контейнеры → значения читаются заново. Отсюда «без отборов обновляется, с отбором — нет».

### 2.2. Правки

1. **[`Workspace1C/Models/Infobase.cs`](Workspace1C/Models/Infobase.cs)** — добавить публичный метод:
   ```csharp
   /// <summary>Уведомляет привязки обо всех свойствах (после правки базы через
   /// прямое присваивание полей, issue #35).</summary>
   public void NotifyAllPropertiesChanged() =>
       PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
   ```
2. **WPF** [`MainViewModel.Commands.cs`](Workspace1C/ViewModels/MainViewModel.Commands.cs:316-330): после переноса всех полей (после `target.AdditionalFields = ...`, перед `SyncFavoriteHotkeys()`) вызвать `target.NotifyAllPropertiesChanged();`.
3. **Avalonia** [`MainViewModel.Avalonia.cs`](Workspace1C/ViewModels/MainViewModel.Avalonia.cs:1486-1493): аналогично — после `ib.AdditionalFields = ...` вызвать `ib.NotifyAllPropertiesChanged();`.
4. (Опционально, для юнит-тестируемости) вынести применение полей в чистый статический помощник, например `InfobaseEditor.ApplyEdits(Infobase target, Infobase result)` в `Services/`, и вызывать его из обоих путей; он же (или явный вызов) поднимает уведомление. Если помощник не вводится — ограничиться пунктами 1–3 и тестами на сам метод уведомления.
5. **Не менять** поведение фильтров: если при правке с базы снят активный тег, база должна скрыться из отбора (текущее поведение `RebuildGroupTreeCore` сохраняется — это корректно).

### 2.3. Тесты

1. `Infobase_NotifyAllPropertiesChanged_RaisesAllProperties_Issue35` — подписка на `PropertyChanged` получает событие с `PropertyName == null` (означает «все свойства»).
2. Если введён помощник `ApplyEdits`: `ApplyEdits_CopiesEditableFieldsAndNotifies_Issue35` — поля скопированы на существующий объект (ссылка та же), событие поднято, нередактируемые поля (IsRunning, история запусков) не затронуты.
3. Регресс: полный прогон `Workspace1C.Tests` — правки не влияют на существующие тесты фильтров/дерева.

Ручная проверка (Windows + Linux): включить отбор по тегу, отредактировать видимую базу (имя/описание/версия) → строка обновляется **сразу**, отбор не сбрасывается; снять с базы активный тег → база исчезает из отбора (ожидаемо); в режиме без отборов и при выключенной группировке — без регрессий.

---

## 3. Issue #31 «Переход к строке» (третья итерация)

### 3.1. Анализ причины (что проверять)

Прежние фиксы: 1.0.0.32 (не сработал), 1.0.0.34 (добавлены `GoToRowTests`, но автор сообщает: переход «останавливается» на копии базы в «Закреплённых»). Комментарий 7OH 2026-10-21T17:37:40Z: «открывает папку правильно, а потом останавливается при поиске на Закрепленной базе…».

Механика reveal (WPF):
- [`NavigateToBookmark`/переходы](Workspace1C/ViewModels/MainViewModel.Commands.cs:860-903): `IsRevealNavigationInProgress=true` → `ExpandPathTo(ib)` → `RebuildTree()` → `SelectedInfobase = ib` → `RevealFindInListRequested?.Invoke()`.
- [`OnFindInListRequested`](Workspace1C/Views/MainWindow.Tree.cs:293-304) запоминает липкую цель и планирует [`RevealAndSelectAfterRebuild`](Workspace1C/Views/MainWindow.Tree.cs:314-492): `revealToHome=true` → `leaf = FindInfobaseHomeNode(_viewModel.GroupNodes, ib)` (игнорирует узел «Закреплённые», см. [`GroupNodeViewModel.FindInfobaseHomeNode`](Workspace1C/ViewModels/GroupNodeViewModel.cs:560-593)) → раскрытие предков → [`FindTargetContainer(target, leaf)`](Workspace1C/Views/MainWindow.Tree.cs:776-787) → [`MaterializeRevealTarget`](Workspace1C/Views/MainWindow.Tree.cs:503-531) (виртуализация off) при неудаче.

Гипотезы, почему «останавливается на закреплении» (проверить в коде — пункт «диагностика» ниже):
1. **Контейнер домашнего узла не реализован** при виртуализации (строка за вьюпортом в длинной свёрнутой/далёкой группе): `FindTreeViewItemForData(homeNode)` возвращает `null`, `FindTargetContainer` — `null`, попытки `RevealAndSelectAfterRebuild(attempt+1)` идут по `ApplicationIdle`; при исчерпании `MaxRevealAttempts` reveal **тихо выходит без выделения** — на экране остаётся выделение копии в «Закреплённых» (начальная позиция).
2. **Гонка восстановления по `TreeRebuilt`**: обычное восстановление (`RestoreTreeKeyboardFocus`, [MainWindow.Tree.cs:272-285](Workspace1C/Views/MainWindow.Tree.cs:272)) при пересборке внутри `NavigateToBookmark`/`ExecuteFindInList` может выбрать первую попавшуюся строку по данным (для закреплённой базы это копия в «Закреплённых», она идёт первой) раньше, чем «доедет» целевой reveal. Флаг `_revealFindInListPending`/`IsRevealNavigationInProgress` должен это блокировать — проверить порядок событий (TreeRebuilt → восстановление Render → RevealFindInListRequested → второй reveal).
3. **Семантика `FindTargetContainer`**: при `homeNode != null` поиск ограничен поддеревом домашнего узла, но **контейнер цели может оказаться под узлом «Закреплённые»**, если `homeNode` найден неверно (например, для базы в режиме «Все базы» с отключённой группировкой/отбором `_flatNode` против дубля дома). Нужен контроль принадлежности найденного контейнера поддереву `leaf` и отсутствию предка-«Закреплённые».
4. **Плоский режим**: в «Все базы» с отбором/поиском/без группировки вся «домашняя» строка — внутри `_flatNode` (дублей нет), но навигация должна идти туда, а не в узлы старого режима. Проверить, что `ExecuteFindInList` сбрасывает вкладку в «Все базы» и что цель достижима в плоском дереве.
5. **`Infobase.IsSelected`/`PinnedInfobaseItem`**: WPF-строка «Закреплённые» несёт обёртку `PinnedInfobaseItem` с тем же `Base` ([`GroupNodeViewModel.PopulateItems`](Workspace1C/ViewModels/GroupNodeViewModel.cs:404-409)) — при выборе домашней строки не должна светиться/удерживаться копия в закреплениях (односторонняя синхронизация уже есть — `OnMainTree_SelectedItemChanged`, [MainWindow.Tree.cs:793-823](Workspace1C/Views/MainWindow.Tree.cs:793)).

### 3.2. Правки (направление)

1. **Диагностика**: добавить флаг `CM_REVEAL` в [`TraceFlags.cs`](Workspace1C/Services/TraceFlags.cs) и лог в точках reveal (WPF и Avalonia): идентификатор/IsPinned цели, `revealToHome`, `leaf` (маркер/путь, или null), результат `FindTargetContainer` (найдено/в каком поддереве/принадлежность предков), `attempt`, исход попыток (выделено/исчерпано). Это даст точный корень по логам автора, не гадая.
2. **Гарантия «домашней» строки**:
   - В WPF после `FindTargetContainer(target, leaf)`: проверить, что контейнер находится в поддереве `leaf` и **не** имеет предка с DataContext узла «Закреплённые» (маркер `PinnedMarker`). При нарушении — трактовать как «не найдено» и идти в `MaterializeRevealTarget(target, leaf)`.
   - Если `FindTreeViewItemForData(homeNode)` не реализован при первом проходе — сразу переходить к материализации цели (включение виртуализации off), не полагаясь на повторные попытки с пустым результатом.
   - Запрет выбора копии «Закреплённые» при `revealToHome` на уровне `ApplySelection` (проверка контейнера), чтобы даже при сбое поиска не оставалось выделение на закреплении.
   - Плоский режим: явно проверить путь reveal при `GroupNodes == [_flatNode]` (цель внутри `_flatNode`, `leaf = _flatNode`).
   - Avalonia: аналогичные проверки в [`RevealFindInList`/`FindFindInListRow`](Workspace1C/Views/MainWindow.Avalonia.Scroll.cs:215-328) и [`LeveledTreeView.FindFindInListRow`](Workspace1C/Controls/LeveledTreeView.Avalonia.cs:743-810) (обход «домашней» строки уже есть — проверить, что он не падает в дубль «Закреплённые»).
3. **Тесты**: расширить [`GoToRowTests.cs`](Workspace1C.Tests/GoToRowTests.cs) — добавить чистые тесты на новый помощник планирования reveal (если введён): «закреплённая база → home = группа, а не узел Закреплённые» (есть), «плоский режим → home = flat node» (есть), «цель недостижима → plan rejected». Добавить тест на логику `IsRevealTargetReachable` с исключением узла «Закреплённые» из кандидатов при revealToHome.

### 3.3. Критерии готовности

- На Windows и Linux: Ctrl+T «Найти в списке» из закреплённой строки раскрывает цепочку групп во «Все базы» и выделяет **домашнюю** строку (в её группе/«Без группы»), а не копию в «Закреплённых»; Ctrl+цифра (закладка) — то же самое; переходы из поиска/тегов/«Избранное» корректно возвращаются в «Все базы»; свёрнутые группы раскрываются; курсор на строке.
- Плана «остановиться на закреплении» нет ни в одном сценарии матрицы (п. 7).
- Все тесты `GoToRowTests` + новые зелёные; лог CM_REVEAL показывает `leaf` = правильный домашний узел для закреплённых баз.

---

## 4. Issue #25 «Импорт из кластера 1С» — автовыбор единственного кластера и сразу список баз

### 4.1. Анализ причины (что проверять)

Текущее состояние VM (1.0.0.35) уже содержит:
- автовыбор кластера после подключения: приоритет «переданный из „Серверы 1С“» → **единственный** → подсказка при нескольких — [`ClusterImportViewModel.ConnectAsync`](Workspace1C/ViewModels/ClusterImportViewModel.cs:358-377);
- `SelectedClusterId` setter запускает `LoadBasesAsync` (fire-and-forget) — [ClusterImportViewModel.cs:180-196](Workspace1C/ViewModels/ClusterImportViewModel.cs:180);
- `InitializeFromServer(server, autoConnect, clusterId)` — [ClusterImportViewModel.cs:289-304](Workspace1C/ViewModels/ClusterImportViewModel.cs:289).

При этом автор просит: «после подключения автоматически выбирать кластер, если он один, и сразу показывать список баз — и при ручном запуске, и из „Серверы 1С“». В коде это уже написано, но по факту (по словам автора, на 1.0.0.33/34) список не появлялся сразу. Что проверить и доработать:

1. **Тест-сеam**: убедиться, что тест `Connect_SingleCluster_AutoSelectsIt_AndLoadsBases_Issue25` ([ClusterImportViewModelTests.cs:66](Workspace1C.Tests/ClusterImportViewModelTests.cs:66)) действительно дожидается завершения `LoadBasesAsync` и проверяет `Rows` (а не только `SelectedClusterId`). При необходимости добавить внутренний «marкер завершения» для тестов (например `internal Task? LastBasesLoadTask`, заполняемое в `LoadBasesAsync` по `finally`), чтобы тесты были детерминированными.
2. **WPF-окно** [`ClusterImportWindow.xaml.cs`](Workspace1C/Views/ClusterImportWindow.xaml.cs:132-149): в `OnVmPropertyChanged` **нет ветки `SelectedClusterId`** (в Avalonia-окне она есть, [ClusterImportWindow.Avalonia.cs:232](Workspace1C/Views/ClusterImportWindow.Avalonia.cs:232) — `SyncSelectedCluster()`). Добавить зеркальную ветку: синхронизация комбобокса (если требуется) и `UpdateCommandStates()` — чтобы автовыбор кластера из `ConnectAsync` сразу видел UI и не оставлял «зависшее» состояние кнопок/прогресса.
3. **Статусы**: `ConnectAsync` после успеха ставит `StatusText` «Подключено…», затем сеттер `SelectedClusterId` запускает `LoadBasesAsync`, который ставит «Загрузка баз…» и по завершении «Загружено N». Проверить доставку через `_dispatchToUi` (порядок на UI-потоке) — статусы должны сменяться без мигания ошибки.
4. **Проверить Avalonia-вход** из окна «Серверы 1С»: `InitializeFromServer(..., autoConnect:true, clusterId: ...)` → `ConnectAsync` → автовыбор переданного/единственного кластера → список баз появляется без нажатия «Получить базы» (в окне Avalonia метод `SyncSelectedCluster` уже вызывается по `SelectedClusterId`).

### 4.2. Правки

1. [`ClusterImportViewModel.cs`](Workspace1C/ViewModels/ClusterImportViewModel.cs) — минимально: тест-сеam для завершения загрузки (если потребуется) + при необходимости уточнение статуса после автовыбора (не показывать «выберите кластер», когда единственный кластер уже выбран).
2. [`ClusterImportWindow.xaml.cs`](Workspace1C/Views/ClusterImportWindow.xaml.cs:132) — добавить ветку `case nameof(ClusterImportViewModel.SelectedClusterId): UpdateCommandStates();`.
3. [`ClusterImportViewModelTests.cs`](Workspace1C.Tests/ClusterImportViewModelTests.cs) — дополнить/усилить:
   - `Connect_SingleCluster_AutoSelectsIt_AndLoadsBases_Issue25` → проверять `Rows.Count > 0` после завершения загрузки (детерминированно), `StatusText` = «Загружено…», `IsBusy == false`;
   - `InitializeFromServer_AutoConnectTrue_SingleCluster_LoadsBasesImmediately_Issue25` — новый: автоподключение из «Серверы 1С» без `clusterId` при единственном кластере → кластер выбран, базы загружены;
   - регресс: все существующие тесты `Connect_*`, `InitializeFromServer_*`, `SelectedServerChange_*` остаются зелёными.

### 4.3. Критерии готовности

- Ручной запуск «Утилиты → Операции → Импорт из кластера 1С»: указать сервер с одним кластером → Подключиться → кластер автоматически выбран, чеклист баз появляется сразу (без «Получить базы»); при нескольких кластерах — подсказка «выберите кластер», поведение не изменилось.
- Открытие из «Серверы 1С» (с выбранным кластером и без): список баз показывается сразу после автоподключения.
- «Импортировать» доступен при отмеченных новых базах; смена сервера/кластера сбрасывает состояние (регрессия 1.0.0.35 не нарушена).

---

## 5. Issue #21 «Долгое переключение вкладок „Все/Избранное“ и рост памяти» (итерация B, Windows)

### 5.1. Статус и выводы из логов

- 1.0.0.34 (эксперимент A): при малом пересечении составов `ReplaceItemWise` делает один `Reset` ([ObservableRangeCollection.cs:107-122](Workspace1C/Collections/ObservableRangeCollection.cs:107)); постоянный плоский узел/кэш дерева; `GroupNodeViewModel.Detach` при сбросе кэша.
- Лог автора (17:45, 1.0.0.34): `collAdd=0, collRemove=0, collReset=3, resetMode=reset`, **heapMb растёт (≈43→90+)**, **heapAfterGcMb тоже растёт** (48→33 → тенденция вверх между записями), время переключения деградирует. Проблемы на Linux нет.
- Вывод: **рост кучи не иллюзорный** (принудительный GC поколения 2 возвращает мало) → подозрение на удерживаемые объекты/подписки/кэши в WPF-слое, а не только на «шквал» событий коллекций (который уже устранён Reset'ом). Требуется новая гипотеза и точечный эксперимент.

### 5.2. Гипотезы для проверки и направление фикса

1. **Повторные подписки на события контейнеров/строк при пересборке** (WPF): каждый раз при замене `GroupNodes` через Reset WPF пересоздаёт контейнеры; если стиль контейнера/DataTemplate или code-behind навешивает обработчики (`Loaded`/`Unloaded`, `SizeChanged`, `PropertyChanged` на строку, `ContainerPrepared`-подписки в [`MainWindow.Avalonia.cs:1465`](Workspace1C/Views/MainWindow.Avalonia.cs:1465)) **без отписки при Recycling/Unloaded** — накапливаются живые объекты. Проверить `LeveledTreeView` (WPF) и контролы строк (`InfobaseListView`), места `ContainerPrepared`/`ContainerCleared`/`RowMaterialized`.
2. **Recycling-пул `ItemContainerGenerator` WPF**: после полного `Reset` старые контейнеры попадают в «recycled» пул и могут удерживаться до прокрутки; при росте диапазона (переключение вкладок) пул не освобождается. Эксперимент B: после смены вкладки принудительно «вычистить» пул (например, кратковременно выставить `VirtualizationMode=Standard`/сбросить `ItemsSource` на пустой+вернуть, либо позвать `ItemContainerGenerator` прямо) и измерить `heapAfterGcMb`; если память возвращается — это отложенное освобождение, а не утечка, и решение — управляемый сброс пула.
3. **Цикл стабилизации выравнивания заголовка**: `stabilize` растёт (счётчик [`MainWindow.Columns.cs:573`](Workspace1C/Views/MainWindow.Columns.cs:573)); проверить выход из `QueueHeaderAlign`/`AlignHeaderToData` при `single-pass` режиме — незавершённый цикл на `ApplicationIdle` удерживает захваченные элементы и крутит GC.
4. **Отложенные Dispatcher-вызовы**: `RestoreTreeKeyboardFocus`/`RevealAndSelectAfterRebuild`/`QueueHeaderAlign` планируют замыкания с захватом `MainTree`/строк; при быстрых переключениях очередь копится. Проверить флаги-демпферы (`_revealFindInListPending`, `_scrollQueued`, `_listScrollHooked`).
5. **Кэш/дубли в VM**: при `Reset` плоского узла каждый раз пересоздаются `Children`/`Infobases`? Нет — поддерживается постоянный `_flatNode`. Но проверить `WeakReference`-зонды на `GroupNodeViewModel`, `PinnedInfobaseItem`, контейнеры после переключения (диагностика ниже): если удерживаются — найти держателя.

### 5.3. План эксперимента (задача 4)

1. Расширить лог `CM_TREESWITCH` (WPF [`MainWindow.xaml.cs:355-398`](Workspace1C/Views/MainWindow.xaml.cs:355), Avalonia [`MainWindow.Avalonia.cs:300-334`](Workspace1C/Views/MainWindow.Avalonia.cs:300)) новыми полями:
   - `allocMb` — прирост `GC.GetTotalAllocatedBytes(precise: false)` за смену вкладки (мусор vs живой рост);
   - `gcGen0/gen1/gen2` — уже есть (`gc0/gc1/gc2`); оставить;
   - `containersCreated/containersCleared` за смену (WPF — по `ItemContainerGenerator.StatusChanged` или счётчикам контейнеров, по аналогии с Avalonia-версией; сейчас в WPF этих счётчиков нет);
   - `stabilize` — уже есть;
   - `revealOvershoot` — не нужно; вместо этого `dispatcherSkipped` (счётчик отброшенных/пропущенных отложенных вызовов при демпфере) при необходимости.
2. Подготовить диагностическую сборку (флаги `CM_TREESWITCH`, `CM_REVEAL`), попросить 7OH прислать логи 30 переключений «Все»↔«Избранное»/«Недавние» на Windows.
3. По данным выбрать гипотезу из п. 5.2 и реализовать точечный фикс (приоритет H1 отписки контейнеров → H2 управляемый сброс Recycling-пула → H3 стабилизация → H4 Dispatcher-демпферы). Для каждой — отдельный замер до/после.
4. Проверить отсутствие регрессий на Linux (там проблемы нет — Avalonia-путь не менять без необходимости).

### 5.4. Критерии готовности

- `heapAfterGcMb` и `heapMb` **не имеют устойчивого тренда роста** за 30 переключений на Windows (дисперсия ±несколько МБ, без 43→90).
- Время переключения вкладок не деградирует от переключения к переключению.
- `collReset/collAdd/collRemove` остаются в рамках Reset-режима, без генерации «шквала» поштучных событий.
- На Linux — без изменений поведения и производительности (или с улучшением).

---

## 6. Замена иконок приложения (задача 5)

### 6.1. Что сейчас используется (проверено по коду и csproj)

| Файл | Где используется |
|---|---|
| `Workspace1C/app.ico` | `ApplicationIcon` (Windows: [csproj:28](Workspace1C/Workspace1C.csproj:28); Linux: [csproj:45](Workspace1C/Workspace1C.csproj:45)); Resource+EmbeddedResource (Windows, [csproj:103-104](Workspace1C/Workspace1C.csproj:103)); заголовок окна WPF ([App.xaml.cs:414](Workspace1C/App.xaml.cs:414)), трей WPF ([MainWindow.Tray.cs:136](Workspace1C/Views/MainWindow.Tray.cs:136)); Avalonia через [`AppIconLoader.Avalonia.cs`](Workspace1C/Services/AppIconLoader.Avalonia.cs:81) |
| `Workspace1C/tray.ico` | Трей WPF (fallback после app.ico, [MainWindow.Tray.cs:136](Workspace1C/Views/MainWindow.Tray.cs:136)); Resource+EmbeddedResource Windows ([csproj:105-106](Workspace1C/Workspace1C.csproj:105)) |
| `Workspace1C/app_icon_preview.png`, `tray_icon_preview.png` | Linux/Avalonia: EmbeddedResource + запасной путь `AppIconLoader` ([csproj:183-184](Workspace1C/Workspace1C.csproj:183), [AppIconLoader.Avalonia.cs:86-97](Workspace1C/Services/AppIconLoader.Avalonia.cs:86)) |
| `donat.png` | Вкладка «О программе» (WPF pack URI, Avalonia resource) — **не иконка, не меняем** |
| `package/linux/app.png` | .desktop-иконка AppImage/DEB ([appimage.sh:64](package/linux/appimage.sh:64), `.DirIcon`) |

### 6.2. Проверка палитр (SVG palette_*) — сделана

Поиск по `Workspace1C/` показал: **палитры `svg/palette_amber|graphite|light|night.svg` в коде приложения не используются**. Актуальные палитры приложения заданы кодом/разметкой:
- [`Themes/LightTheme.xaml`](Workspace1C/Themes/LightTheme.xaml:6) / [`DarkTheme.xaml`](Workspace1C/Themes/DarkTheme.xaml:5) (и `.axaml`-аналоги): акценты `#FDBF00`/`#FFB300`;
- [`Models/ColorScheme.cs`](Workspace1C/Models/ColorScheme.cs:331-380): дефолтные палитры светлой/тёмной темы;
- скрипт [`design/icons/gen.py`](design/icons/gen.py:10-19) генерирует `palette_*.svg` как **дизайн-превью** вариантов иконки под палитры тем (для отбора дизайнером), в сборку они не входят.

**Вывод: код и csproj для палитр не трогаем.** Файлы `svg/palette_*.svg` остаются в `design/icons` как источник дизайна. В CHANGELOG/README можно упомянуть только новые иконки приложения.

### 6.3. Замена файлов

1. Скопировать из `design/icons` в `Workspace1C/` (имена сохраняются → csproj не меняется):
   - `app.ico` → `Workspace1C/app.ico`;
   - `tray.ico` → `Workspace1C/tray.ico`;
   - `app_icon_preview.png` → `Workspace1C/app_icon_preview.png`;
   - `tray_icon_preview.png` → `Workspace1C/tray_icon_preview.png`.
2. `package/linux/app.png` ← из нового набора: `design/icons/png/app_256.png` (размер DEB-иконки 256×256; пропорционально заменить, при необходимости указать в план проверки).
3. Проверить, что превью `app_icon_preview.png`/`tray_icon_preview.png` соответствуют новому дизайну (они используются Linux-треем/окном). Если расхождение — перегенерировать превью по `design/icons/gen.py` (требует python3 + Pillow + playwright/Chromium; выполняется вне релиза, не входит в сборку).
4. После замены — контрольная проверка:
   - Windows: иконка отображается в проводнике/exe, заголовке окна (`App.xaml.cs:277`), трее (app.ico и tray.ico), в `Процессы → свойство файла` версия и иконка не битые;
   - Linux: иконка трея и заголовка (грузится `AppIconLoader`: app.ico на диске → встроенные превью), AppImage/.desktop-иконка из `package/linux/app.png`.
5. Не забыть, что `workspace-1c.desktop` использует `Icon=workspace-1c` → файл кладётся под `workspace-1c.png` при упаковке — имя/скрипты не меняем.

---

## 7. Версионирование и документация (задача 6)

1. **Версия**: [`Workspace1C.csproj:62-65`](Workspace1C/Workspace1C.csproj:62) — `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion` → `1.0.0.36`. Проверить, не содержит ли `VersionInfo.cs` / `AssemblyInfo.cs` жёстко зашитых версий (если да — обновить).
2. **CHANGELOG.md**: новый раздел `## [1.0.0.36] — 2026-10-21` (в начало, после шапки) с блоками «Исправлено»:
   - #35 «правка свойств базы обновляет строку сразу, даже при активном отборе/поиске»;
   - #31 «переход к строке (Ctrl+T / закладки) всегда ведёт на домашнюю строку базы во „Все базы“, а не на копию в „Закреплённые“»;
   - #25 «после подключения при единственном кластере он выбирается автоматически и список баз показывается сразу (ручной запуск и „Серверы 1С“)»;
   - #21 «переключение вкладок: … (результат эксперимента B)» — текст заполняется по итогам задачи 4;
   - блок «Изменено»: «новые иконки приложения (app.ico/tray.ico) во всех размерах; Linux-превью (app_icon_preview/tray_icon_preview) обновлены».
3. **README.md**: [строка 3](README.md:3) (бейдж `Версия-1.0.0.35` → `1.0.0.36`) и [строка 11](README.md:11) («актуальная версия — 1.0.0.36»); при необходимости — короткая строка в истории изменений о новых иконках и исправлениях #35/#31/#25/#21.
4. **rel_notes_1.0.0.36.md**: создать по образцу [`rel_notes_1.0.0.35.md`](rel_notes_1.0.0.35.md) — текст релиза (будет вставлен в GitHub release).

---

## 8. Сборка и релиз (задачи 7–8)

1. **Тесты**: `dotnet test Workspace1C.Tests` (полный прогон; новый прогон после задач 1–4).
2. **Сборка Windows** (на Windows): [`Workspace1C/build-windows-single-file.ps1`](Workspace1C/build-windows-single-file.ps1) → `dist/win-x64/Workspace1C.exe`.
3. **Сборка Linux** (на Linux): [`Workspace1C/build-linux-single-file.sh`](Workspace1C/build-linux-single-file.sh) (или сборка на Windows через [`build-linux-single-file.ps1`](Workspace1C/build-linux-single-file.ps1) с `ForceLinux=true`) → single-file `publish/linux-x64/Workspace1C`; при необходимости переименование в `Workspace1C-linux-x64` по практике прошлых релизов (см. README «Скачать»).
4. **Ручная проверка** (обе платформы): сценарии из «Критериев готовности» задач 1–4 + внешний вид новых иконок (п. 6.3.4).
5. **Git**: коммиты по задачам (или по итогу всех), push в `main`. Комментирующие файлы с подписями (например `_issue35_comment.md` и т.п.) — по практике предыдущих версий; не обязательны.
6. **GitHub release** `v1.0.0.36`: два исполняемых файла (`Workspace1C.exe`, `Workspace1C-linux-x64`) + текст из `rel_notes_1.0.0.36.md`.
7. **Комментарии в issues** (после релиза, issues **не закрывать**):
   - #35: «Исправлено в версии 1.0.0.36 (Windows и Linux): правка свойств базы при активном отборе по тегу/поиске теперь сразу обновляет строку (ранее данные строки оставались прежними до сброса отбора)…»;
   - #31: «Исправлено в версии 1.0.0.36 (Windows и Linux): переход к строке (Ctrl+T и Ctrl+цифра) всегда раскрывает группу и выделяет домашнюю строку базы во „Все базы“, а не копию в „Закреплённых“…»;
   - #25: «Исправлено в версии 1.0.0.36 (Windows и Linux): после подключения при единственном кластере он выбирается автоматически и список баз показывается сразу — и при ручном запуске окна, и при открытии из „Серверы 1С“…»;
   - #21: «В 1.0.0.36 выполнен эксперимент B по переключению вкладок: …результат…» (по итогам задачи 4).

---

## 9. Сводка решений (таблица)

| Issue | Корень проблемы (по коду 1.0.0.35) | Главное изменение |
|---|---|---|
| #35 | Поля `Infobase` (Name/Group/Desc/… ) — auto-свойства без INPC; в плоском режиме (при отборе) `_flatNode` переиспользуется, `ReplaceGroupNodes` делает early-return без пересоздания контейнеров → привязки не перечитываются | После переноса полей в обоих путях правки (`MainViewModel.Commands.cs`, `MainViewModel.Avalonia.cs`) вызывать `Infobase.NotifyAllPropertiesChanged()`; при необходимости — чистый помощник `ApplyEdits` + тесты |
| #31 | Reveal-механика (WPF/Avalonia) может не найти контейнер домашнего узла (виртуализация/плоский режим), исчерпать попытки и оставить выделение на копии в «Закреплённых» | Диагностика `CM_REVEAL`; жёсткая проверка принадлежности найденного контейнера поддереву домашнего узла (не «Закреплённые»); переход к материализации при нереализованном home-контейнере; тесты GoToRowTests расширить |
| #25 | VM уже автовыбирает единственный кластер и запускает загрузку баз; WPF-окно не имеет ветки `SelectedClusterId` (Avalonia — имеет), статус/кнопки при автовыборе могут не «догонять» | Добавить синхронизацию/UpdateCommandStates по `SelectedClusterId` в WPF; усилить тесты (загрузка Rows до завершения, оба входа); проверить порядок статусов через `_dispatchToUi` |
| #21 | Reset-режим убрал «шквал» событий (collReset=3), но heapAfterGcMb растёт → удерживаемые объекты в WPF-слое (подписки контейнеров/строк, Recycling-пул ItemContainerGenerator, цикл стабилизации заголовка, Dispatcher-замыкания) | Эксперимент B: новые поля CM_TREESWITCH (allocMb, счётчики контейнеров), логи 30 переключений; точечный фикс по выбранной гипотезе (отписки → сброс Recycling-пула → стабилизация → демпферы) |
| Иконки | Старые assets в `Workspace1C/` и `package/linux/app.png`; csproj ссылается на прежние имена (менять не нужно) | Копирование новых `app.ico`, `tray.ico`, превью из `design/icons`; обновление `package/linux/app.png` из `png/app_256.png`; палитры SVG — только дизайн-артефакты, код не трогаем |

---

## 10. Критерии приёмки (итог)

- Версия в релизе — ровно `1.0.0.36` (увеличено только последнее число).
- Все юнит-тесты зелёные (включая новые по #35/#31/#25/#21); новые исполняемые файлы Windows и Linux — одиночные (single-file).
- #35: правка свойств базы обновляет строку при активном отборе на обеих платформах (окно свойств → сразу в списке).
- #31: любой переход к строке (Ctrl+T, закладки Ctrl+цифра, из закрепления, из поиска/тегов) выделяет домашнюю строку во «Все базы», а не закрепление.
- #25: при единственном кластере подключение → автовыбор → сразу список баз (ручной старт и вход из «Серверы 1С»); много кластеров — выбор пользователем, без регрессий 1.0.0.35.
- #21: на Windows за 30 переключений — нет тренда роста `heapAfterGcMb`/`heapMb`, время переключения не деградирует; на Linux — без регрессий.
- Иконки: новые в exe (Windows), трее (обе ОС), заголовке окна, в Linux AppImage/.desktop; превью обновлены; csproj не изменён помимо номера версии.
- CHANGELOG.md / README.md / rel_notes_1.0.0.36.md описывают все изменения с привязкой к issues; после релиза — комментарии в #35, #31, #25, #21 (issues остаются открытыми).
