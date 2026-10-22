# План исправлений 1.0.0.37—1.0.0.44 — issues #36, #37, #38, #39, #21 (финальный фикс памяти), #25 (остаточная ошибка), #32, #33

Версии: **1.0.0.36 → 1.0.0.44** (увеличивается ТОЛЬКО последнее число; **по одной микро-версии на каждый issue**, порядок по номерам: #36→#37→#38→#39→#21→#25→#32→#33, финальная версия **1.0.0.44** — см. [`Workspace1C.csproj`](Workspace1C/Workspace1C.csproj:62)).
Issues: **#36, #37, #38, #39, #21, #25, #32, #33** — после релиза в каждый добавляется комментарий «что исправлено и в какой версии», issues **НЕ закрываются**. Пропускаемые (последний комментарий от владельца): **#26, #29, #30 — в план не входят**.
Links с open issues: [#36](https://github.com/sivatorov/Workspace1C/issues/36), [#37](https://github.com/sivatorov/Workspace1C/issues/37), [#38](https://github.com/sivatorov/Workspace1C/issues/38), [#39](https://github.com/sivatorov/Workspace1C/issues/39), [#21](https://github.com/sivatorov/Workspace1C/issues/21), [#25](https://github.com/sivatorov/Workspace1C/issues/25), [#32](https://github.com/sivatorov/Workspace1C/issues/32), [#33](https://github.com/sivatorov/Workspace1C/issues/33).

Каждая задача — **отдельный `new_task` (mode `code`)**. Задачи идут **строго последовательно** (общий файл версии/CHANGELOG/README — серийный bump, иначе микро-версии «разъедутся»), по образцу практики `PLAN_1.0.0.36`.

**Feasibility:** восемь issues покрывают разные зоны кода:
- #36 — заголовок окна свойств базы [`ConnectionSettingsWindow.xaml`](Workspace1C/Views/ConnectionSettingsWindow.xaml:11) / `.Avalonia.cs`;
- #37 — модель и меню служб ([`ServiceModels.cs`](Workspace1C/Services/ServiceModels.cs:229), [`ServiceTrayMenuBuilder.cs`](Workspace1C/Services/ServiceTrayMenuBuilder.cs:145)) + окно «Управление службами»;
- #38 — горячая клавиша «Окно обновления платформы» ([`SettingsWindow.xaml`](Workspace1C/Views/SettingsWindow.xaml:1646), [`MainViewModel.PlatformUpdate.cs`](Workspace1C/ViewModels/MainViewModel.PlatformUpdate.cs:18));
- #39 — кэш [`RacJobListFormatStore`](Workspace1C/Services/RacJobListFormatStore.cs:19) и его использование в [`RacClient.cs`](Workspace1C/Services/RacClient.cs:62);
- #21 — переключение вкладок и рост кучи WPF ([`ObservableRangeCollection.cs`](Workspace1C/Collections/ObservableRangeCollection.cs:75), [`MainViewModel.Tools.cs`](Workspace1C/ViewModels/MainViewModel.Tools.cs:91), [`MainWindow.xaml.cs`](Workspace1C/Views/MainWindow.xaml.cs:381), [`TraceFlags.cs`](Workspace1C/Services/TraceFlags.cs:56));
- #25 — импорт из кластера ([`ClusterImportViewModel.cs`](Workspace1C/ViewModels/ClusterImportViewModel.cs:311), окна WPF/Avalonia, [`RacClient.cs`](Workspace1C/Services/RacClient.cs:84));
- #32 — матчинг горячих клавиш Shift+клавиша ([`MainWindow.Hotkeys.cs`](Workspace1C/Views/MainWindow.Hotkeys.cs:149), [`MainWindow.Avalonia.Hotkeys.cs`](Workspace1C/Views/MainWindow.Avalonia.Hotkeys.cs:36));
- #33 — отображение «(тонкий клиент)» в списках платформ ([`PlatformVersionPickerWindow.xaml`](Workspace1C/Views/PlatformVersionPickerWindow.xaml:247), [`SettingsWindow.Platforms.cs`](Workspace1C/Views/SettingsWindow.Platforms.cs:127)).

Зоны не пересекаются по файлам; общая часть (bump версии, CHANGELOG, README, rel_notes, сборка, релиз) выполняется по шагам в каждой задаче 1–8 и как завершающие задачи 9–10.

---

## 1. Декомпозиция на задачи (порядок и зависимости)

Каждая задача — **отдельный `new_task` (mode `code`)** и **отдельная микро-версия**. Порядок строго серийный (задачи 1–8): каждая последующая начинается после `push`/фиксации предыдущей (общий файл `Workspace1C.csproj` и история версий).

| № | Mode | Версия | Issue | Суть | Зависит от |
|---|---|---|---|---|---|
| 1 | code | 1.0.0.37 | #36 | Заголовок диалога «Настройка информационной базы» содержит имя базы и обновляется при правке поля «Наименование» (WPF + Avalonia) + тесты | — |
| 2 | code | 1.0.0.38 | #37 | Колонка «Временно отключить» в «Управление службами» (службы и группы); отключённые не попадают в подменю служб (трей/контекстное меню/«Серверы 1С») + тесты | 1 |
| 3 | code | 1.0.0.39 | #38 | Найти и исправить баг сохранения хоткея «Окно обновления платформы» (Settings/Keys) + тесты | 2 |
| 4 | code | 1.0.0.40 | #39 | Убрать создание/чтение `rac_joblist_formats.json` (кэш оставить в памяти), удалить старый файл при старте + тесты | 3 |
| 5 | code | 1.0.0.41 | #21 | Диагностика и финальный фикс роста памяти при переключении вкладок Все/Избранное/Недавние (Windows; без регрессий Linux) + тесты | 4 |
| 6 | code | 1.0.0.42 | #25 | «Ошибка импорта из кластера серверов»: устранить остаточную ошибку после «Подключиться», автовыбор единственного кластера, повтор при сбое формата rac + тесты | 5 |
| 7 | code | 1.0.0.43 | #32 | Исправить обработку горячих клавиш «Shift+клавиша» (WPF и Avalonia) + тесты | 6 |
| 8 | code | 1.0.0.44 | #33 | Отображать суффикс «(тонкий клиент)» и оранжевую подсветку во всех списках платформ (диалог выбора, настройки; WPF + Avalonia) + тесты; создать `rel_notes_1.0.0.44.md` | 7 |
| 9 | code | — | Сборка | Полный прогон `dotnet test`; сборка Windows (`build-windows-single-file.ps1`); Linux-сборка — в CI; ручная проверка сценариев #36–#33 | 8 |
| 10 | code | — | Релиз | git add/commit/push, тег `v1.0.0.44`, GitHub Actions `release.yml` (2 single-file exe), GitHub Release с текстом из `rel_notes_1.0.0.44.md`, комментарии в issues «исправлено в 1.0.0.X» (issues НЕ закрывать), подтверждения по Telegram | 9 |

```mermaid
graph LR
  T1[Задача 1: 1.0.0.37 issue 36] --> T2[Задача 2: 1.0.0.38 issue 37]
  T2 --> T3[Задача 3: 1.0.0.39 issue 38]
  T3 --> T4[Задача 4: 1.0.0.40 issue 39]
  T4 --> T5[Задача 5: 1.0.0.41 issue 21]
  T5 --> T6[Задача 6: 1.0.0.42 issue 25]
  T6 --> T7[Задача 7: 1.0.0.43 issue 32]
  T7 --> T8[Задача 8: 1.0.0.44 issue 33]
  T8 --> T9[Задача 9: Сборка и тесты]
  T9 --> T10[Задача 10: Релиз v1.0.0.44]
```

---

## 2. Issue #36 «Название базы при правке» (задача 1, версия 1.0.0.37)

Автор 7OH, 0 комментариев → работаем по описанию (+ скриншот, приложенный к issue).

### 2.1. Анализ (где искать код)

- Окно правки свойств базы — `ConnectionSettingsWindow`:
  - WPF: [`ConnectionSettingsWindow.xaml`](Workspace1C/Views/ConnectionSettingsWindow.xaml:11) (`Title="{loc:Loc ConnectionSettings.Title}"`), code-behind [`ConnectionSettingsWindow.xaml.cs`](Workspace1C/Views/ConnectionSettingsWindow.xaml.cs) (конструктор получает `Infobase` и `Groups`).
  - Avalonia: [`ConnectionSettingsWindow.Avalonia.cs`](Workspace1C/Views/ConnectionSettingsWindow.Avalonia.cs:74) (`Title = LocalizationManager.T("ConnectionSettings.Title")`).
- Локализация: `ConnectionSettings.Title` = «Настройка информационной базы» ([`ru.json`](Workspace1C/Localization/Languages/ru.json:1596), [`en.json`](Workspace1C/Localization/Languages/en.json:1596) — «Infobase settings»).
- Поле «Наименование» — на вкладке «Основное» окна свойств; точное имя поля/бокса уточнить при реализации (поиск по вкладке и DataContext; возможные кандидаты: поле с привязкой на `Name` модели/VM окна).
- Вызов окна: [`MainViewModel.Commands.cs:272-276`](Workspace1C/ViewModels/MainViewModel.Commands.cs:272) (WPF), [`MainViewModel.Avalonia.cs:1405-1457`](Workspace1C/ViewModels/MainViewModel.Avalonia.cs:1405) (Avalonia).

### 2.2. Что менять

1. Новая строка локализации (ru/en): `ConnectionSettings.TitleFormat` = «Настройка информационной базы: {0}» / «Infobase settings: {0}».
2. WPF (`ConnectionSettingsWindow.xaml.cs`):
   - При инициализации (после конструктора базы) — `Title = string.Format(T("ConnectionSettings.TitleFormat"), ib.Name)`.
   - Подписка на изменение текста поля «Наименование» (TextChanged/PropertyChanged) → пересчёт заголовка `Title` с новым значением; отписка в `Closed` (не держать окно/базу).
3. Avalonia (`ConnectionSettingsWindow.Avalonia.cs`) — то же самое: стартовый заголовок (около строки 74) + обработчик изменения поля имени.
4. Поведение при смене имени без нажатия «ОК» — заголовок-«превью» обновляется сразу (это и просит автор); при закрытии «Отмена» возвращается прежний заголовок главного окна (уже существующая логика — не трогаем).

### 2.3. Тесты

- Правка UI-окна, юнит-тестов на окно обычно не пишем; добавляем лёгкую проверку локализации: новый ключ `ConnectionSettings.TitleFormat` присутствует в ru/en (покрытие локализационных ключей, если такой тест-скелет существует — дополнить).
- Регресс: полный прогон `Workspace1C.Tests` (правки заголовка не влияют на логику).

**Ручная проверка (обе платформы):** «Правка свойств базы» → в заголовке окна видно «…: Имя базы»; меняем «Наименование» → заголовок обновляется сразу; при отмене ничего не сохранятся, заголовок главного окна прежний.

---

## 3. Issue #37 «Временное отключение управления службой» (задача 2, версия 1.0.0.38)

Автор 7OH, 0 комментариев → работаем по описанию: колонка-галочка «Временно отключить» в «Управление службами», работает и для службы, и для папки; при включённой галочке служба/папка не попадает во все подменю управления службами.

### 3.1. Анализ (где искать код)

- Модель данных служб: [`ServiceModels.cs`](Workspace1C/Services/ServiceModels.cs:229) — `ServiceStoreData` (`Services`, `Groups`, `Order`), `Normalize`; [`ServicesStore.cs`](Workspace1C/Services/ServicesStore.cs:35) — файл `services.json` (каталог профиля, атомарная запись).
- Окно «Управление службами»: WPF [`ServicesWindow.xaml.cs`](Workspace1C/Views/ServicesWindow.xaml.cs:44) (TreeView со строками служб и групп), Avalonia [`ServicesWindow.Avalonia.cs`](Workspace1C/Views/ServicesWindow.Avalonia.cs:29); VM [`ServicesWindowViewModel.cs`](Workspace1C/ViewModels/ServicesWindowViewModel.cs:311).
- Подменю служб (все точки сборки): [`ServiceTrayMenuBuilder.cs`](Workspace1C/Services/ServiceTrayMenuBuilder.cs:145) (`Build` — единая модель: «Настроить…», строки списка, «Все сервисы»), адаптеры: [`MainWindow.Tray.cs`](Workspace1C/Views/MainWindow.Tray.cs:698) (WinForms), [`MainWindow.Avalonia.Tray.cs`](Workspace1C/Views/MainWindow.Avalonia.Tray.cs:237), [`MainWindow.Hotkeys.cs:445-540`](Workspace1C/Views/MainWindow.Hotkeys.cs:445) (WPF контекстное меню Ctrl+Shift+Alt+S), [`MainWindow.Avalonia.Hotkeys.cs:578`](Workspace1C/Views/MainWindow.Avalonia.Hotkeys.cs:578), [`ServerMonitorWindow.xaml.cs:348-515`](Workspace1C/Views/ServerMonitorWindow.xaml.cs:348) и [`ServerMonitorWindow.Avalonia.cs:1204`](Workspace1C/Views/ServerMonitorWindow.Avalonia.cs:1204).
- Существующий прецедент «персистентности флагов» — `AppSettings.CollapsedServiceGroups` (передаётся в VM окна, см. `ServicesWindowViewModel.cs:388`).

### 3.2. Что менять

1. **Модель** (`ServiceModels.cs` + сериализация в `ServicesStore`):
   - В `ServiceStoreData` добавить `List<string> TemporarilyDisabledServices` и `List<string> TemporarilyDisabledGroups` (имя — уточнить единым стилем; желательно хранить ключи группы в том же виде, что в `Order`, например `group:Имя`).
   - `Normalize` — сохранять/чистить флаги (удалять отключённые имена, отсутствующие в `Services`/`Groups`).
   - Обратная совместимость: старый `services.json` без новых полей читается как «ничего не отключено».
2. **Окно «Управление службами»**:
   - Колонка «Временно отключить» (CheckBox) для строк-служб и строк-групп: WPF — шаблон строки TreeView/DataGrid; Avalonia — строка-карточка (code-behind), по образцу существующих колонок.
   - Переключение → вызов VM-команды/свойства → `ServicesStore.Save` (по образцу сохранения порядка/групп).
3. **Подменю управления службами** (`ServiceTrayMenuBuilder.Build` + адаптеры):
   - Отключённые службы не попадают ни в одну строку верхнего списка;
   - отключённая группа — скрывается целиком (и её члены не показываются нигде, включая «Все сервисы»);
   - «Все сервисы» строится только из НЕ отключённых служб;
   - Сам слой «Настроить…» остаётся (иначе нельзя вернуть настройку).
   - Т.к. все меню строятся через `ServiceTrayMenuBuilder`, правка в одном месте покрывает трей, контекстное меню и «Серверы 1С»; проверить, что адаптеры не дублируют построение списка мимо билдера.
4. **Статусы**: в подменю, где висит статус-обновление (`RefreshServicesStatusesInBackground` и т.п.), отключённые просто не создаются как пункты — опроса на них нет (опционально).

### 3.3. Тесты

- `ServiceStoreDataTests`/`ServicesStoreTests`: round-trip JSON с новыми полями; `Normalize` сохраняет флаги и чистит «мёртвые» имена; старый формат без полей → пустые флаги.
- Тесты билдера (в `Workspace1C.Tests`, файл по образцу существующих тестов служб): отключённая служба не даёт пунктов; отключённая группа скрыта целиком; «Все сервисы» без отключённых.
- При выносе логики фильтрации в чистый класс (например, расширение `ServiceTrayMenuBuilder`) — тест-таблица с комбинациями.

**Ручная проверка (обе платформы):** галочка у службы → в трей-подменю, контекстном меню служб и окне «Серверы 1С» её нет; галочка у папки → папка и её службы исчезают из подменю; перезапуск — флаги сохранены; снятие галочки возвращает пункты.

---

## 4. Issue #38 «Не сохраняет хоткей» (задача 3, версия 1.0.0.39)

Автор 7OH, 0 комментариев: не сохраняется горячая клавиша действия «Окно обновления платформы» (Настройки → Клавиши).

### 4.1. Анализ (где искать код; цепочка хоткея)

1. Поле ввода: [`SettingsWindow.xaml:1646`](Workspace1C/Views/SettingsWindow.xaml:1646) `HotkeyPlatformUpdateBox` (контрол `Controls/HotkeyBox`); Avalonia: [`SettingsWindow.Avalonia.cs:2941`](Workspace1C/Views/SettingsWindow.Avalonia.cs:2941) `HotkeyRow(..., _viewModel.HotkeyPlatformUpdate)`.
2. Чтение при «ОК»: [`SettingsWindow.xaml.cs:505`](Workspace1C/Views/SettingsWindow.xaml.cs:505) `ReadHotkeyBox(HotkeyPlatformUpdateBox)`, запись в VM: [`SettingsWindow.xaml.cs:641`](Workspace1C/Views/SettingsWindow.xaml.cs:641) `_viewModel.HotkeyPlatformUpdate = hkPlatformUpdate;` (до `SaveSettings()` на строке 652 — порядок корректный, проверить ещё раз).
3. VM-свойство: [`MainViewModel.PlatformUpdate.cs:18`](Workspace1C/ViewModels/MainViewModel.PlatformUpdate.cs:18); инициализация из настроек: [`MainViewModel.cs:492`](Workspace1C/ViewModels/MainViewModel.cs:492); персист в `AppSettings`: [`MainViewModel.Launch.cs:717`](Workspace1C/ViewModels/MainViewModel.Launch.cs:717) `s.HotkeyPlatformUpdate = _hotkeyPlatformUpdate;`; дефолт: [`AppSettings.cs:715`](Workspace1C/Models/AppSettings.cs:715) `= "Ctrl+F9"`.
4. Регистрация: [`MainWindow.Hotkeys.cs:99`](Workspace1C/Views/MainWindow.Hotkeys.cs:99) и [`MainWindow.Avalonia.Hotkeys.cs:169`](Workspace1C/Views/MainWindow.Avalonia.Hotkeys.cs:169) `Add(vm.HotkeyPlatformUpdate, ShowPlatformUpdateCommand)`.
5. ВАЖНОЕ отличие от соседей: `ApplyAppBehaviorSettings(...)` на [`SettingsWindow.xaml.cs:599-638`](Workspace1C/Views/SettingsWindow.xaml.cs:599) **не принимает** `hotkeyPlatformUpdate` (в отличие от большинства прочих хоткеев) — обновление идёт отдельной строкой 641. Проверить: (а) обновляет ли setter `HotkeyPlatformUpdate` сразу `_settings.HotkeyPlatformUpdate` или только VM-поле `_hotkeyPlatformUpdate`, после чего значение «теряется» при повторном сохранении/перезапуске; (б) заполняется ли поле `HotkeyPlatformUpdateBox` при открытии окна настроек (populate по аналогии с другими полями) — если пропущено, при «ОК» сохраняется пустое/старое значение.

### 4.2. Правки (направление)

1. Найти место «несохранения» в цепочке 4.1:
   - если сеттер VM не обновляет `_settings` — добавить (единый механизм с другими хоткеями);
   - если поле не заполняется при открытии настроек — добавить в populate-код `HotkeyPlatformUpdateBox` (WPF) и HotkeyRow (Avalonia) наравне с соседями;
   - если проблема в регистрации/чтении после перезапуска — проверить `MainViewModel.cs:492` и парсинг `TryParseKeyGesture` ([`MainWindow.Hotkeys.cs:149`](Workspace1C/Views/MainWindow.Hotkeys.cs:149)); для Avalonia — `Controls/HotkeyBox.Avalonia.cs:104` `TryParse`.
2. Повторить диагностику для Avalonia-ветки (там хоткей хранится в том же `AppSettings`, пути WPF/Avalonia общие на уровне VM).
3. Убедиться, что новое значение немедленно применяется (перерегистрация `RegisterLaunchHotkeys`/`RegisterHotkeys`), не только после перезапуска.

### 4.3. Тесты

- `AppSettings` round-trip: `HotkeyPlatformUpdate` сохраняется/восстанавливается (если такого теста нет — добавить).
- Парсинг: `TryParseKeyGesture("Ctrl+F9")` и `HotkeyBox.TryParse` (Avalonia) — валидный жест; пустое значение — «Нет».
- Если введён общий помощник (например, перенос всех хоткеев в единый метод применения) — тест «смена хоткея платформ не затирается соседними хоткеями» (по образцу `BookmarksMenuHotkeyTests`).

**Ручная проверка:** Настройки → Клавиши → «Окно обновления платформы»: назначить `Ctrl+F9` (и отличное от дефолта, например `Shift+F9`) → ОК → перезапуск → назначенное работает и отображается в настройках; соседние хоткеи не тронуты.

---

## 5. Issue #39 «Лишний файл» (задача 4, версия 1.0.0.40)

Автор 7OH, 0 комментариев: приложение создаёт `rac_joblist_formats.json` рядом с `profiles.json`, хотя вкладка с заданиями rac удалена.

### 5.1. Анализ (код найден)

- Класс-кэш целиком: [`RacJobListFormatStore.cs`](Workspace1C/Services/RacJobListFormatStore.cs:19) — `Load()`/`Save()` файла `rac_joblist_formats.json` в `PlatformPaths.AppDataDirectory`.
- Единственный потребитель: [`RacClient.cs`](Workspace1C/Services/RacClient.cs:62) — поле `_clusterFormats` и операции:
  - загрузка с диска в конструкторе ([`RacClient.cs:80`](Workspace1C/Services/RacClient.cs:80));
  - перебор форматов `--cluster=<uuid>` / `--cluster <uuid>` / позиционный `<uuid>` перед вызовом rac ([`RacClient.cs:143-145`](Workspace1C/Services/RacClient.cs:143), [`RacClient.cs:258-260`](Workspace1C/Services/RacClient.cs:258));
  - сброс формата при сбое и `Save` ([`RacClient.cs:298-300`](Workspace1C/Services/RacClient.cs:298));
  - `Save` после успешного запуска rac ([`RacClient.cs:531-533`](Workspace1C/Services/RacClient.cs:531)).
- Проверить остатки удалённой вкладки: `ViewModels/RacJobRow.cs`, `RacJobFilterRow.cs`, методы/команды с «Job» в `RacClient`/VM — если мертвый код, удалить попутно (или оставить, если используется где-то ещё — проверить `search` по «RacJob»).

### 5.2. Правки

1. **Удалить файл-кэш**:
   - удалить класс `RacJobListFormatStore.cs` (или, если оставляем перебор форматов в памяти — удалить только диск-операции `Load`/`Save`/`ResolvePath`);
   - в `RacClient`: убрать `Load()` из конструктора (80), убрать `Save` из 299 и 531; при сохранении перебора форматов «в памяти» логика повторного вызова с другим форматом и запоминание в `_clusterFormats` остаётся (это функциональность, нужная для 8.5.x, issue #324/#25 — из кэша на диск просто уходит);
   - если решение «совсем убрать перебор» (сильный вариант) — зафиксировать формат 0 (`--cluster=<uuid>`) для всех list-команд и удалить цикл перебора; НЕ рекомендую: 8.5.x отклоняет формат 0, вызовы начнут падать.
2. **Чистка при старте**: при инициализации приложения (место миграций — например, возле `TraceFlags.EnsureExists`/`OldVersionCleaner` или в точке входа WPF/Avalonia) удалить существующий файл `rac_joblist_formats.json` (одноразово; ошибки игнорировать — по образцу миграций trace-журнала). Сам файл больше не создаётся.
3. Проверить, что после правки нигде нет ссылок на удалённые символы (сборка обеих платформ).

### 5.3. Тесты

- `RacClientTests`: (а) конструктор/выполнение rac-команд НЕ создаёт файл `rac_joblist_formats.json` (изолированный каталог теста), (б) перебор форматов работает без дискового кэша (в памяти), (в) повторный запуск после перезапуска приложения — поведение по формату не сломалось (модель «в памяти» — при смене команды всё равно пробует дефолт).
- Тест чистки: если удаление файла вынесено в чистый метод — «старый файл удаляется, отсутствующий не мешает».
- Регресс: `ClusterImportViewModelTests` (загрузка баз через `RacClient` без записи кэша), `RacOutputParser`-тесты.

**Ручная проверка:** запуск → работа с кластером (импорт/монитор) → файл `rac_joblist_formats.json` не создаётся; старый файл удалён при первом старте новой версии.

---

## 6. Issue #21 «Долгое переключение между вкладками Все/Избранное/Недавние» (задача 5, версия 1.0.0.41)

24 комментария, последний от 7OH `2026-10-21T20:13:07Z` (лог CM_TREESWITCH+CM_TREESWITCH_FLUSH). **Самая рискованная задача** — допускает несколько итераций внутри версии.

### 6.1. Выводы из лога автора (готовый факт)

- 30 переключений «Все»↔«Избранное» с флагами `CM_TREESWITCH`+`CM_TREESWITCH_FLUSH`: `heapMb` растёт линейно 43→327 МБ за ~2 мин, `heapAfterGcMb` 28→298, каждый переход `collReset=1`, `resetMode=reset` (т.е. `ReplaceItemWise` уходит в полный `ReplaceAll`/Reset), `gc2` растёт. `sizeChanged=1` (коллекция меняется одной заменой), но строки полностью пересоздаются.
- Вывод автора: память НЕ возвращается после GC2 → старые объекты удерживаются; Reset выполняется каждый переход. Эксперимент B (Flash Recycling-пула, `CM_TREESWITCH_FLUSH`, см. [`TraceFlags.cs:58-64`](Workspace1C/Services/TraceFlags.cs:58)) проблему НЕ решил.

### 6.2. Места кода

- Замена коллекций при переключении вкладок: [`ObservableRangeCollection.ReplaceItemWise`](Workspace1C/Collections/ObservableRangeCollection.cs:75) (порог Reset `resetIfKeptTimesLessThanTotal=4`, т.е. 0.25 — о чём пишет автор), [`ReplaceAll`](Workspace1C/Collections/ObservableRangeCollection.cs:33).
- Точка пересборки: [`ReplaceGroupNodes`](Workspace1C/ViewModels/MainViewModel.Tools.cs:91) (early-return при том же составе корней), построение дерева `RebuildGroupTreeCore` ([`MainViewModel.Theme.cs:892`](Workspace1C/ViewModels/MainViewModel.Theme.cs:892)), плоский узел [`GroupNodeViewModel.ResetFlatContent`](Workspace1C/ViewModels/GroupNodeViewModel.cs:438) и [`PopulateItems`](Workspace1C/ViewModels/GroupNodeViewModel.cs:389), `GroupNodeViewModel.Detach` (отписки при сбросе кэша, 1.0.0.34).
- Диагностика/замеры WPF: [`MainWindow.xaml.cs:331-438`](Workspace1C/Views/MainWindow.xaml.cs:331) (TreeRebuilding/TreeRebuilt, `CM_TREESWITCH`: sizeChanged/stabilize/allocMb/rowsRealized/rowsCleared/heapMb/heapAfterGcMb/coll*/resetMode/gc*), счётчики реализованных/отсоединённых строк; степень recycling — [`MainWindow.xaml.cs`](Workspace1C/Views/MainWindow.xaml.cs) (грифы VirtualizingPanel/Recycling) и `LeveledTreeView`/контролы в [`Controls/`](Workspace1C/Controls/).
- Avalonia-аналоги: [`MainWindow.Avalonia.cs:300-334`](Workspace1C/Views/MainWindow.Avalonia.cs:300), `MainWindow.Avalonia.Tree.cs` — на Linux проблемы нет, путь не менять без необходимости.
- Стабилизация выравнивания заголовка: [`MainWindow.Columns.cs`](Workspace1C/Views/MainWindow.Columns.cs:573) (stabilize), `QueueHeaderAlign`.
- Инструкции по gcdump уже есть в репо: [`_issue21_gcdump_instructions.md`](_issue21_gcdump_instructions.md), [`_issue21_comment_v100029.md`](_issue21_comment_v100029.md).

### 6.3. Анализ-гипотезы и направление фикса

1. **Держатель старых объектов (главный подозреваемый)**. `heapAfterGcMb` растёт после GC2 → живые ссылки на: (а) старые контейнеры строк WPF (Recycling-пул/pending requests), (б) `GroupNodeViewModel`/`_flatNode` без `Detach` (подписки `PropertyChanged` на `Infobase`/коллекции `Children`/`Items`), (в) Dispatcher-замыкания `RestoreTreeKeyboardFocus`/`RevealAndSelectAfterRebuild`/`QueueHeaderAlign`, (г) обработчики `ContainerPrepared`/`ContainerCleared`/`RowMaterialized` с захватом строк. GCDump (по инструкциям в репо) с замером после 30 переключений покажет держателя по типам — **первый шаг задачи — профилирование** (weakref-зонды на `_flatNode`, группы, `Infobase`, контейнеры после смены вкладки + dotnet-dump/gcdump на Windows).
2. **Reset на каждый переход**. `Reset` для плоского узла при непересекающихся составах («Избранное» и «Все») — ожидаем «один Reset», но нужно проверить, что при этом НЕ пересоздаётся корневой узел/не сбрасывается его контейнер (иначе теряется смысл постоянного `_flatNode`). Альтернатива по контексту автора: не делать Reset при переходе Все↔Избранное — переиспользовать узлы/строки (например, две постоянные плоские коллекции с переключением `ItemsSource` без полной пересборки, либо поддержка «diff-подобного» обновления для непересекающихся составов).
3. **Подписки строк**: проверить все места подписки на события контейнера/строки при `ContainerPrepared` и симметричные отписки при `ContainerCleared`/Recycling (WPF), `Detach` в `GroupNodeViewModel` при смене режима вкладок — вызывается ли для `_flatNode.Children`/старых групп при `RebuildGroupTreeCore` в ветке плоского режима.
4. **Эксперименты**: (а) Freeze `rowsRealized`/`rowsCleared` (сколько контейнеров реально создаётся за переход); (б) повтор замеров с отключённым восстановлением выделения (`RestoreTreeKeyboardFocus` при `IsListModeSwitchInProgress` уже пропускается — проверить, что не планируются лишние Dispatcher-операции); (в) сравнение с `CM_TREESWITCH_FLUSH` выключен (по логу — флаг уже не помог).

### 6.4. Правки (по итогам диагностики; приоритет)

1. По gcdump/зондам найти конкретного держателя и устранить (отписки, сброс кэшей, избавление от замыканий).
2. Переход Все↔Избранное без полного Reset: переиспользование строк/узлов (реализация зависит от найденного корня; предпочтителен вариант «меньше пересозданий контейнеров»).
3. Проверить `GroupNodeViewModel.Detach` и symmetric отписки во всех ветках `RebuildGroupTreeCore`, включая плоский режим и `ResetFlatContent`.
4. Диагностику `CM_TREESWITCH` дополнить полем `detachCount` (сколько узлов вызвало `Detach` за переход) — чтобы по логу автора подтвердить «отписки работают».
5. Не менять Linux-путь (Avalonia) без необходимости; убедиться в отсутствии регрессий.

### 6.5. Тесты

- `ListSwitchPerformanceTests` (существует в `Workspace1C.Tests`) — дополнить чистыми проверками: `ReplaceItemWise` с непересекающимся составом уходит в Reset (порог 0.25), а с пересечением — в поштучные; повторный переход «тем же составом» — 0 событий.
- `ObservableRangeCollectionTests` — порог/режим (дополнить, если нет).
- `GroupNodeViewModelTests` — `Detach` отписывает обработчики (нет ссылок после `Detach`), `ResetFlatContent` переиспользует узел (ReferenceEquals).
- При введении «кэша узлов между вкладками» — тест: переход Все→Избранное→Все возвращает те же экземпляры строк/узлов (по ReferenceEquals) для неизменных баз.

### 6.6. Критерии готовности

- На Windows за 30 переключений «Все»↔«Избранное»: `heapMb`/`heapAfterGcMb` без устойчивого роста (дисперсия в пределах единиц–нескольких МБ), время переключения не деградирует.
- `collReset` — не более 1 на переход, без «шквала» поштучных событий; `rowsRealized` не растёт с каждым переходом.
- GCDump после 30 переключений не показывает живых «старых» строк/контейнеров от прошлых вкладок.
- Linux: поведение и производительность не изменились (или улучшились).
- Комментарий 7OH в issue с просьбой подтвердить (попутные логи CM_TREESWITCH).

---

## 7. Issue #25 «Ошибка импорта списка баз из кластера серверов» (задача 6, версия 1.0.0.42)

13 комментариев, последний от 7OH `2026-10-21T20:03:25Z`: «при попытке подключения опять ошибка (скриншот), после этого прогресс не крутит, но кнопка всё ещё называется «Подключиться»; кластер приходится выбирать вручную, но после выбора список баз показывает».

### 7.1. Анализ (что проверять)

Текущее состояние VM уже содержит автовыбор единственного кластера и загрузку баз по `SelectedClusterId` ([`ClusterImportViewModel.ConnectAsync`](Workspace1C/ViewModels/ClusterImportViewModel.cs:311-378), [`LoadBasesAsync`](Workspace1C/ViewModels/ClusterImportViewModel.cs:393-469)), подавление сбоя «cluster info» ([`ClusterImportViewModel.cs:410-426`](Workspace1C/ViewModels/ClusterImportViewModel.cs:410)), WPF-синхронизацию по `SelectedClusterId` ([`ClusterImportWindow.xaml.cs:143-149`](Workspace1C/Views/ClusterImportWindow.xaml.cs:143)).
Ключевые факты из описания: (а) ошибка показывается, (б) прогресс останавливается, кнопка снова «Подключиться» — это НЕ «зависшее» состояние 1.0.0.35, а штатный catch (обработка `RacClientException` на [`ClusterImportViewModel.cs:336-340`](Workspace1C/ViewModels/ClusterImportViewModel.cs:336) или [`443-447`](Workspace1C/ViewModels/ClusterImportViewModel.cs:443)); (в) кластеры при этом доступны для ручного выбора и ручной выбор успешно грузит базы.

Гипотезы (проверить в порядке приоритета):
1. **Ошибка происходит на первой попытке `LoadBasesAsync`** сразу после автовыбора (первый запуск rac «infobase summary list» падает из-за формата `--cluster` на rac 8.5.1; перебор форматов в `RacClient` ([`RacClient.cs:258-260`](Workspace1C/Services/RacClient.cs:258)) фиксирует рабочий формат только ПОСЛЕ падения; повторный выбор кластера пользователем с тем же форматом в памяти — успешен). Ошибка показывается пользователю, хотя могла бы быть невидимым «прогревом».
2. **Автовыбор единственного кластера не сработал при ручном входе** (ветка `SelectedClusterId is null` → `Clusters.Count == 1` на [`ClusterImportViewModel.cs:369-372`](Workspace1C/ViewModels/ClusterImportViewModel.cs:369)); проверить, не «занят» ли `SelectedClusterId` значением из `InitializeFromServer` (302) при входе из «Серверы 1С» и не сбивается ли он обработкой смены сервера.
3. **Остаточный `ErrorMessage`** от предыдущей операции не чистится при успешном подключении в отдельных путях (проверить все успешные ветки `ConnectAsync`/`LoadBasesAsync` — по коду `ErrorMessage = string.Empty` стоит в начале обеих (325, 401), но проверить Avalonia и порядок `StatusText` vs `ErrorMessage` через `_dispatchToUi`).
4. **Формат вывода rac 8.5.1** в `GetClustersAsync`/`RacOutputParser.ToClusters` — кластеры распознались, но одна из команд (например «cluster info») падает по новой причине — проверить журнал `RAC:` (уже логируются предупреждения, [`RacClient.cs:95-99`](Workspace1C/Services/RacClient.cs:95)).

### 7.2. Правки (направление)

1. **Диагностика**: добавить одноразовый/флаговый лог фаз окна импорта (флаг типа `CM_CLUSTERIMPORT` в [`TraceFlags.cs`](Workspace1C/Services/TraceFlags.cs): фазы Connect/LoadBases, `ErrorMessage`/`StatusText`, автовыбор, исключения) и попросить 7OH повторить подключение к 8.5.1 с логом — точный корень по логам вместо гаданий.
2. **Повтор при первом сбое формата**: в `LoadBasesAsync` при одиночном `RacClientException` первой попытки (формат/первый запуск rac) — повторить вызов один раз без выставления `ErrorMessage` (по образцу логики перебора форматов в `RacClient`); после успеха принудительно чистить `ErrorMessage`.
3. **Автовыбор**: убедиться, что при ручном входе с единственным кластером выбор и загрузка происходят бесшовно; при входе из «Серверы 1С» — приоритет переданного `clusterId` над единственным (уже есть, проверить тестами).
4. Проверить WPF/Avalonia статусы и доступность кнопок в каждой фазе (без «зависаний» и без «ошибки при успехе»).

### 7.3. Тесты

- `ClusterImportViewModelTests` (существует):
  - «первая попытка LoadBases падает (RacClientException формата), вторая успешна → ErrorMessage пуст, Rows > 0, StatusText „Загружено“» (детерминированно через тест-seam на `IRacClient`, как в существующих тестах);
  - «успешный ConnectAsync после прежней ошибки очищает ErrorMessage в обоих входах»;
  - «ручной вход при единственном кластере — автовыбор и загрузка без вмешательства» (есть — дополнить проверкой Rows);
  - «вход из „Серверы 1С“ с переданным clusterId — приоритет переданного».
- `RacClientTests`: повторная попытка с другим форматом не бросает наружу первую внутреннюю ошибку (при выносе retry в `RacClient`).

**Ручная проверка (Windows + Linux, реальный 8.5.1):** «Утилиты → Импорт из кластера 1С» → сервер с одним кластером → «Подключиться» → без промежуточной ошибки сразу: кластер выбран, чеклист баз; при нескольких кластерах — подсказка «выберите кластер», повторный выбор работает.

---

## 8. Issue #32 «Уведомлять о том, что комбинация горячих клавиш уже занята» (задача 7, версия 1.0.0.43)

4 комментария, последний от 7OH `2026-10-21T19:11:43Z`: «горячие клавиши с Shift+любая кнопка не работают вообще; с Shift+ещё один модификатор работают; просто Shift+кнопка — нет» (проверено на диалогах, которые точно вызываются).

### 8.1. Анализ (места кода)

- WPF: регистрация `KeyBinding` в [`RegisterLaunchHotkeys`](Workspace1C/Views/MainWindow.Hotkeys.cs:33); парсинг жеста [`TryParseKeyGesture`](Workspace1C/Views/MainWindow.Hotkeys.cs:149); запись жеста — контрол [`Controls/HotkeyBox.cs`](Workspace1C/Controls/HotkeyBox.cs:16); возможные перехватчики — [`MainWindow.Events.cs`](Workspace1C/Views/MainWindow.Events.cs) (обработчики PreviewKeyDown/KeyDown, поиск/фокус).
- Avalonia: [`RegisterHotkeys`](Workspace1C/Views/MainWindow.Avalonia.Hotkeys.cs:36), `AddHotkey`, `HotkeyBox.Avalonia.cs:104` `TryParse`.
- Кандидатные причины:
  1. **Парсинг/формат записи**: `HotkeyBox` при вводе «Shift+латинская клавиша» на русской раскладке может сохранить отображаемый символ (например «Shift+П») вместо `Key` — `Enum.TryParse<Key>("П")` падает → hotkey не регистрируется; с двухмодификаторными сочетаниями (Ctrl+Shift+X — клавиша X латиницей из раскладки) запись корректна. Проверить `KeyToDisplay` и `TryParseKeyGesture` для символов/букв различных раскладок.
  2. **WPF-матчинг KeyBinding с одиночным Shift** для «печатных» клавиш (буквы/цифры): при удержании Shift символьная клавиша может уходить как «upper case»/Ime-символ, и `KeyBinding` окна не срабатывает, тогда как Ctrl+Shift/Alt+Shift матчатся корректно (известная особенность WPF на ряде систем/раскладок) — проверить на реальном экземпляре автора.
  3. **Перехват на уровне окна**: глобальный `Window_PreviewKeyDown`/`KeyDown` в `MainWindow.Events.cs` (обработка поиска, ввода, спец-клавиш) — нет ли раннего `e.Handled = true` для Shift-комбинаций.
  4. **Конфликт с системными сочетаниями** (например, Shift+буква заняты книжными закладками? нет — закладки Ctrl+Shift) — проверить `BookmarkSlotHelper.FindReservedHotkeyConflicts` ([`SettingsWindow.xaml.cs:567-579`](Workspace1C/Views/SettingsWindow.xaml.cs:567)).

### 8.2. Правки (направление)

1. Сначала воспроизведение + мини-лог: зарегистрировать `Shift+?` и проверить, доходит ли KeyDown до окна (добавить временную диагностику/флаг `CM_HOTKEY`).
2. Надёжное исправление независимо от корня: **ручной матчинг горячих клавиш в `PreviewKeyDown`** (WPF) и `KeyDown` (Avalonia) через общий чистый класс `HotkeyMatcher` (в `Services/` или `Controls/`): на входе `e.Key` (+ модификаторы, + текущие настройки хоткеев VM) — сравнение физической клавиши и набора модификаторов без «волшебства» WPF `KeyBinding`; при совпадении — выполнить команду (`e.Handled = true`). Это покрывает и Shift+клавиша, и не зависит от раскладки/символьной трансформации.
3. Сохранить существующие `KeyBinding` для неShift-сочетаний (или перевести на ручной матчинг все — единообразно; приоритет: минимальные изменения, но стабильное поведение для Shift).
4. `HotkeyBox`: для буквенных клавиш при вводе с Shift — записывать физическую клавишу (латиницу/`Key`-имя), а не символ текущей раскладки (обе платформы).
5. Duplicate-предупреждение (заголовок issue) уже есть — проверить, что оно срабатывает и для Shift-сочетаний (после фикса матчинга).

### 8.3. Тесты

- Чистый `HotkeyMatcher`: «Shift+F5», «Shift+A», «Shift+9 (D9)», «Ctrl+Shift+A», «Alt+Shift+F», «Shift+Insert», регистро/раскладкозависимость (парсинг «Shift+П» → невалидно ИЛИ нормализация к латинице), несовпадение «Ctrl» против «Shift+Ctrl».
- `TryParseKeyGesture`: строки «Shift+F9», «Shift+A», «Ctrl+F9» (WPF) и `HotkeyBox.TryParse` (Avalonia) — валидны.
- Регресс: `BookmarksMenuHotkeyTests` и остальные хоткей-тесты.

**Ручная проверка (обе платформы):** назначить «Shift+O» (диалог), «Shift+5» (своё действие) → работают без второго модификатора; Ctrl+Shift+… по-прежнему работают; дубликаты предупреждаются.

---

## 9. Issue #33 «Не отображается версия платформы (тонкий клиент)» (задача 8, версия 1.0.0.44)

4 комментария, последний от 7OH `2026-10-21T19:06:49Z`: суффикс «(тонкий клиент)» и оранжевая подсветка, заявленные в 1.0.0.32, не видны ни значком, ни текстом, ни в настройках, ни при выборе платформы для базы. ArtyoZ подтвердил, что сам тонкий клиент в списке есть.

### 9.1. Анализ (код есть, UI не показывает — где искать)

- Распознавание: [`PlatformVersionInfo.IsThinClient`](Workspace1C/Models/PlatformVersionInfo.cs:18), [`PlatformVersionService.cs`](Workspace1C/Services/PlatformVersionService.cs:42-94) (`FindInstalledVersionInfos`, `IsThinClientBin` на стр. 639, вызовы 534/577/624), [`PlatformVersionService.Linux.cs:415-421`](Workspace1C/Services/PlatformVersionService.Linux.cs:415) (`IsThinClientDir`).
- Дерево выбора: `PlatformVersionGroup.IsThinClient` ([`PlatformVersionGroup.cs:57`](Workspace1C/Models/PlatformVersionGroup.cs:57)), простановка в `BuildGroupedTree` ([`PlatformVersionService.cs:901-903`](Workspace1C/Services/PlatformVersionService.cs:901)).
- Диалог выбора версии для базы:
  - WPF: шаблон уже содержит суффикс и DataTrigger ([`PlatformVersionPickerWindow.xaml:247-269`](Workspace1C/Views/PlatformVersionPickerWindow.xaml:247)) — проверить, почему не работает привязка (Order вложенных `TextBlock.Style` с Setter `Foreground` — возможное перекл. стиля; свойство `IsThinClient` на узлах-листьях), code-behind [`PlatformVersionPickerWindow.xaml.cs`](Workspace1C/Views/PlatformVersionPickerWindow.xaml.cs:23);
  - Avalonia: [`PlatformVersionPickerWindow.Avalonia.cs:305-320`](Workspace1C/Views/PlatformVersionPickerWindow.Avalonia.cs:305) (суффикс есть в коде).
  - Точки вызова окна: [`ConnectionSettingsWindow.xaml.cs:434`](Workspace1C/Views/ConnectionSettingsWindow.xaml.cs:434), [`MainWindow.Events.cs:628`](Workspace1C/Views/MainWindow.Events.cs:628), [`MainViewModel.Avalonia.Tools.cs:1005`](Workspace1C/ViewModels/MainViewModel.Avalonia.Tools.cs:1005), [`CreateInfobaseWindow`](Workspace1C/Views/CreateInfobaseWindow.xaml.cs:284).
- Настройки → «Платформа»/«Платформы»: [`SettingsWindow.Platforms.cs`](Workspace1C/Views/SettingsWindow.Platforms.cs:127) `UpdatePlatformsDisplay` — строит список УСТАНОВЛЕННЫХ платформ (линия → разрядность → сборка с путём) **своим кодом, отличным от PlatformVersionPicker**; проверить, что там суффикс/подсветка вообще отсутствуют (вероятная причина «не видно в настройках»). Avalonia-аналог — `SettingsWindow.Avalonia.Platforms.cs`.

### 9.2. Правки (направление)

1. **Диагностика**: определить оба пути, которые видит автор (настройки и выбор для базы); добавить лог-флаг `CM_PLATFORMS` (в `TraceFlags.cs`) — построение узлов с `IsThinClient` и источник (bin/dir), чтобы по логу автора понять, где теряется флаг.
2. **Единый формат отображения**: ввести чистый помощник (например, `PlatformDisplayNameFormatter` или метод в `PlatformVersionGroup`): `DisplayName/build` = «версия (x64)» + суффикс «(тонкий клиент)» и статус «thin» для подсветки; использовать в обоих местах (WPF и Avalonia).
3. **WPF `PlatformVersionPickerWindow.xaml`**: вычинить шаблон узла — текст узла строить через конвертер/якорь так, чтобы суффикс и оранжевая заливка стабильно появлялись на листьях-тонких клиентах (проверить `DataTrigger` против порядка Setter'ов; при необходимости — перейти на `TextBlock` c `Run`/конвертер вместо двух `TextBlock`).
4. **Настройки (`SettingsWindow.Platforms.cs`/Avalonia)**: добавить суффикс/подсветку/значок «тонкий клиент» в строки списка установленных платформ.
5. Проверить путь через `SettingsWindow.Platforms` (там может открываться `PlatformVersionPickerWindow` для выбора — сверить оба входа).

### 9.3. Тесты

- `PlatformVersionPickerTests`/`PlatformVersionTreeBuilderTests` (в `Workspace1C.Tests` есть `PlatformVersionTreeBuilderTests.cs`, `PlatformVersionPickerTests.cs`): узлы-листья с `IsThinClient` в смешанном наборе (полный + тонкий) проносятся в `BuildGroupedTree`; группы/линии `IsThinClient == false`.
- Чистый форматтер: «8.3.27.2214 (x64) (тонкий клиент)» для thin, без суффикса для полного (тест по образцу `InfobaseDisplayTests`).
- Регресс: `OneCPlatformCatalogParserTests`, `ConfigTypes*` — не затрагиваются.

**Ручная проверка (обе платформы):** в настройках и в диалоге выбора версии для базы тонкий клиент отображается с суффиксом и оранжевой подсветкой/значком; полный клиент — без изменений.

---

## 10. Версии, CHANGELOG, README (последовательность из 8 микро-версий)

Каждая задача 1–8 выполняет шаги «версия + документация» для СВОЕЙ микро-версии (порядок строго последовательный):

| Задача | Версия | Issue | Что пишем |
|---|---|---|---|
| 1 | 1.0.0.37 | #36 | «Окно свойств базы показывает имя редактируемой базы в заголовке и обновляет его при правке поля „Наименование" (Windows и Linux)» |
| 2 | 1.0.0.38 | #37 | «„Управление службами": колонка „Временно отключить" для служб и групп; отключённые не попадают в подменю служб (трей, контекстное меню, „Серверы 1С")» |
| 3 | 1.0.0.39 | #38 | «Исправлено сохранение горячей клавиши „Окно обновления платформы" (Настройки → Клавиши)» |
| 4 | 1.0.0.40 | #39 | «Удалено создание лишнего файла rac_joblist_formats.json (кэш форматов rac переведён в память); старый файл удаляется при первом старте» |
| 5 | 1.0.0.41 | #21 | «Переключение вкладок Все/Избранное/Недавние: устранён рост памяти на Windows (…по итогам диагностики задачи 5…)» |
| 6 | 1.0.0.42 | #25 | «Импорт из кластера 1С: убрана остаточная ошибка после „Подключиться", автовыбор единственного кластера и повтор при первом сбое формата rac» |
| 7 | 1.0.0.43 | #32 | «Горячие клавиши с одним Shift (Shift+клавиша) снова работают (Windows и Linux)» |
| 8 | 1.0.0.44 | #33 | «Версия платформы-тонкого клиента отображается с суффиксом и подсветкой в настройках и при выборе версии (Windows и Linux)» |

Механика (для каждой задачи 1–8):
1. **Версия**: [`Workspace1C.csproj:62-65`](Workspace1C/Workspace1C.csproj:62) — `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion` → соответствующая микро-версия. Проверено: [`VersionInfo.cs`](Workspace1C/VersionInfo.cs:17) и [`AssemblyInfo.cs`](Workspace1C/AssemblyInfo.cs) жёстко зашитых версий **не содержат** (обновлять не нужно; повторить проверку при каждой задаче — не появились ли новые константы версии).
2. **CHANGELOG.md**: новый раздел `## [1.0.0.3X] — 2026-10-21` в начало (после шапки), блок «Исправлено» по тексту из таблицы выше (с привязкой к issue).
3. **README.md**: [строка 3](README.md:3) — бейдж `Версия-1.0.0.3X`; [строка 11](README.md:11) — «актуальная версия — 1.0.0.3X».
4. **rel_notes**: создаётся **один раз, для 1.0.0.44** (задача 8) по образцу [`rel_notes_1.0.0.36.md`](rel_notes_1.0.0.36.md) — текст содержит все 8 исправлений (сводно), дата 2026-10-21, раздел «Исправлено» + «Примечания» (issues остаются открытыми).

---

## 11. Сборка (задача 9)

1. **Тесты**: `dotnet test Workspace1C.Tests` (проект `Workspace1C.Tests`, xunit, net10.0) — полный прогон после всех задач 1–8 (и точечные прогоны после каждой задачи).
2. **Сборка Windows** (локальная машина — Windows): `.\Workspace1C\build-windows-single-file.ps1 -Configuration Release` → `dist/win-x64/Workspace1C.exe` (self-contained single-file; скрипт сам удаляет лишние файлы из папки — [`build-windows-single-file.ps1`](Workspace1C/build-windows-single-file.ps1:60)).
3. **Сборка Linux**: на локальной Windows-машине **не собирается** (нужен реальный Linux-хост; кросс-скрипт `build-linux-single-file.ps1 -ForceLinux` по надобности только для проверки компиляции). Настоящая Linux-сборка выполняется в CI при push тега — `build-linux-single-file.sh Release` → `dist/linux-x64/Workspace1C` (см. [`release.yml`](.github/workflows/release.yml:95)).
4. **Ручная проверка** (обе платформы): матрица сценариев из «Критериев готовности» задач 1–8.
5. При необходимости — контрольный `.gitignore`-прогон: `dist/`, `publish/` не коммитим.

---

## 12. Релиз (задача 10)

1. **Git**: `git add` изменений (csproj, CHANGELOG, README, rel_notes_1.0.0.44.md, код, тесты) → `git commit` → `git push origin main`.
2. **Тег**: `git tag v1.0.0.44` → `git push origin v1.0.0.44`.
3. **GitHub Actions**: workflow [`release.yml`](.github/workflows/release.yml:19) реагирует на push тега `v*` — соберёт **обе** платформы (windows: `Workspace1C.exe`; linux: `Workspace1C-linux-x64`) и создаст (обновит) GitHub Release по тегу с текстом из `rel_notes_1.0.0.44.md` ([`release.yml`](.github/workflows/release.yml:10)).
4. **Проверка release**: оба single-file файла прикреплены, имена совпадают с ожидаемыми автообновлением (`GitHubReleaseService`).
5. **Комментарии в issues** (после выхода релиза; issues **не закрывать**):
   - #36 → «Исправлено в версии 1.0.0.37 (Windows и Linux): заголовок окна свойств базы показывает имя базы и обновляется при правке поля „Наименование“…»;
   - #37 → «Исправлено в версии 1.0.0.38 (Windows и Linux): колонка „Временно отключить“…»;
   - #38 → «Исправлено в версии 1.0.0.39 (Windows и Linux): горячая клавиша „Окно обновления платформы“ теперь сохраняется и применяется…»;
   - #39 → «Исправлено в версии 1.0.0.40 (Windows и Linux): файл rac_joblist_formats.json больше не создаётся; старый удаляется при первом старте…»;
   - #21 → «В 1.0.0.41 выполнено исправление роста памяти при переключении вкладок (…) — просим подтвердить замер на вашем стенде…»;
   - #25 → «Исправлено в версии 1.0.0.42 (Windows и Linux): устранена остаточная ошибка после „Подключиться“, автовыбор единственного кластера, повтор при первом сбое формата rac…»;
   - #32 → «Исправлено в версии 1.0.0.43 (Windows и Linux): горячие клавиши с одним Shift работают…»;
   - #33 → «Исправлено в версии 1.0.0.44 (Windows и Linux): суффикс „(тонкий клиент)“ и подсветка видны в настройках и при выборе версии платформы…».
6. **Подтверждения по Telegram** (канал @Workspace1C): анонс релиза и запрос подтверждений по #21/#25/#33 (требуют стенда автора).

---

## 13. Риски и контрольные точки

| Риск | Митигация / контрольная точка |
|---|---|
| **#21 — самый рискованный**: корень утечки может быть не один; без стенда автора замеры на локали невозможны; возможно >1 итерации внутри 1.0.0.41 | Задача 5 начинается с gcdump/зондов (инструкции в [`_issue21_gcdump_instructions.md`](_issue21_gcdump_instructions.md)); промежуточная контрольная точка — «лог CM_TREESWITCH от 7OH после выпуска диагностической сборки». Если фикс не подтверждается — выпускаем 1.0.0.41 диагностическим и продолжаем в 1.0.0.42+ (сдвиг последующих задач), но по контексту заказчика — финальный фикс именно в 1.0.0.41. |
| **#25 — воспроизводимость**: нужен реальный кластер 8.5.1 (RAS/агент) и логи автора | Перед фиксом — запросить у 7OH лог с флагом `CM_CLUSTERIMPORT`; контрольная точка — «ошибка воспроизведена и подтверждена логом». |
| **#32 — особенность окружения автора** (раскладка/система), воспроизведение может отличаться | Ручной матчинг `HotkeyMatcher` + тесты инвариантны к раскладке; контрольная точка — «воспроизведён баг на Windows» и «фикс подтверждён автором». |
| **#33 — несколько путей отображения** (настройки vs диалог выбора; WPF vs Avalonia) | Отдельная проработка каждого пути (п. 9.2), единый форматтер; контрольная точка — скриншоты автора с обоих мест. |
| **Строго серийные 8 микро-версий**: баг в поздней задаче не блокирует предыдущие релизы, но сдвигает нумерацию | Коммит/фича в релиз сразу после задачи 9; если задача 5 (или иная) затягивается — версии не «перепрыгиваются» (каждая задача в своей микро-версии; при необходимости — временный диагностический релиз). |
| Регресс Avalonia на Linux по задачам с общим кодом (#37, #32, #33, #36) | Парные правки WPF/Avalonia в одной задаче; полный прогон тестов (чистая логика платформа-независима); ручная проверка на Linux до релиза (в CI — только сборка, ручные сценарии по возможности на Linux-машине). |
| **#39 — удаление кэша форматов rac**: риск падения «подключения» на 8.5.x (первый вызов падает без кэша) | Кэш переводим в память (перебор форматов остаётся), файл удаляется только один раз при старте; регресс-тесты `ClusterImportViewModelTests`/`RacClientTests`. |
| **#38 — «не сохраняет хоткей»**: корень может быть в одном из нескольких звеньев цепочки (populate/применение/персист/регистрация) | Пошаговая проверка цепочки (п. 4.1) до написания фикса; точечные тесты на каждое звено. |

---

## 14. Сводка решений (таблица)

| Issue | Корень (по коду 1.0.0.36) | Главное изменение |
|---|---|---|
| #36 | Заголовок окна свойств базы всегда `ConnectionSettings.Title` без имени базы (WPF XAML:11; Avalonia:74), поле «Наименование» не пересобирает заголовок | Новый ключ `TitleFormat`; стартовый заголовок с именем базы + пересчёт при правке имени (WPF и Avalonia) |
| #37 | В `ServiceStoreData`/`services.json` нет флага «временно отключено»; `ServiceTrayMenuBuilder.Build` строит все строки безусловно; окно не имеет колонки | Новое поле флагов в модели (+Normalize/сериализация); колонка CheckBox в окне; фильтрация в билдере (все адаптеры меню) |
| #38 | Цепочка сохранения `HotkeyPlatformUpdateBox → VM → AppSettings → регистрация` — звено теряет значение (проверить populate/применение/персист, см. п. 4.1) | Починка конкретного звена; тесты round-trip и парсинга |
| #39 | `RacJobListFormatStore` пишет/читает `rac_joblist_formats.json`; вызывается из `RacClient` (80, 299, 532) | Удаление диск-кэша (перебор форматов — в память), одноразовая чистка старого файла при старте |
| #21 | `Reset` каждый переход (непересекающиеся составы → `ReplaceAll`), `heapAfterGcMb` растёт после GC2 → удерживаемые объекты (подписки/контейнеры/замыкания WPF) | Диагностика (gcdump/зонды) → точный держатель; переиспользование узлов/строк между вкладками; контроль `Detach`/отписок; замеры 30 переключений |
| #25 | Ошибка/остаточный `ErrorMessage` на первой попытке загрузки (формат rac 8.5.1), автовыбор может не срабатывать для части входов | Лог `CM_CLUSTERIMPORT`, повтор первой упавшей попытки без ошибки, чистка ошибки при успехе, проверка автовыбора в обоих входах |
| #32 | `Shift+клавиша` не матчится (парсинг/раскладка или WPF KeyBinding с одиночным Shift) | Общий `HotkeyMatcher` + ручной матчинг в PreviewKeyDown/KeyDown; исправление записи в HotkeyBox |
| #33 | Суффикс/подсветка частично есть в `PlatformVersionPicker` (WPF XAML:247-269, Avalonia:305-320), но отсутствует в настройках (`SettingsWindow.Platforms`) и/или привязка не срабатывает | Единый форматтер имени платформы; починка шаблона узла; добавление суффикса/подсветки в список настроек (WPF/Avalonia) |

---

## 15. Критерии приёмки (итог)

- В релизе — ровно **v1.0.0.44** (микро-версии накапливаются 1.0.0.37…1.0.0.44; увеличено только последнее число).
- Все юнит-тесты зелёные (включая новые по #36–#33); новые исполняемые файлы Windows и Linux — single-file (сборка в CI по тегу).
- #36: заголовок окна свойств базы содержит имя и обновляется при правке «Наименования» (WPF + Avalonia).
- #37: «Временно отключить» работает для служб и групп; отключённые исчезают из всех подменю служб; флаги сохраняются в `services.json`.
- #38: хоткей «Окно обновления платформы» сохраняется, применяется и виден после перезапуска.
- #39: `rac_joblist_formats.json` не создаётся; старый файл удалён при первом старте; импорт из кластера работает.
- #21: на Windows 30 переключений — без роста `heapMb`/`heapAfterGcMb` и без деградации времени; Linux без регрессий; подтверждение автора.
- #25: «Подключиться» на 8.5.1 не показывает ложную ошибку; единственный кластер выбирается автоматически; список баз показывается сразу (оба входа).
- #32: Shift+клавиша работает без второго модификатора; дубликаты по-прежнему предупреждаются.
- #33: суффикс «(тонкий клиент)» и подсветка видны в настройках и при выборе платформы (WPF + Avalonia).
- CHANGELOG.md / README.md / rel_notes_1.0.0.44.md описывают все изменения с привязкой к issues; после релиза — комментарии во все 8 issues (issues **остаются открытыми**), подтверждения по Telegram.