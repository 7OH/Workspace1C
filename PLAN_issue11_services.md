# План: issue #11 «Управление службами» — версия 1.0.0.10

> Репозиторий: https://github.com/sivatorov/Workspace1C (рабочая копия `g:/Yandex.Disk/h/Workspace1C`).
> Дата планирования: 2026-10-17. Версия-цель: **1.0.0.10** (увеличиваем ТОЛЬКО последнее число, см. [Workspace1C.csproj](Workspace1C/Workspace1C.csproj:62)).
> Объём первой итерации — по последнему комментарию 7OH от 2026-10-17: «текущие задачи», без групп и без управления из трея.

---

## 1. Цель и объём

### 1.1. Что реализуем (первая итерация)

1. Пункт меню «Управление службами» **под «Серверы 1С»** с хоткеем **Ctrl+Shift+S** по умолчанию (настраивается в «Настройки → Клавиши»).
2. Окно «Управление службами»:
   - первая строка — две кнопки с иконками: **«Настроить»** и **«Системный диалог»**;
   - «Системный диалог» открывает `services.msc` **только на Windows**; на Linux кнопка скрывается;
   - ниже — список с заголовком **«Службы»** высотой ~15 строк (по числу выводимых строк), при пустом списке — подсказка «Ничего не добавлено» вместо элементов (в сам ListBox ничего не добавляем);
   - над списком — строка кнопок-иконок (с разделителями): добавляемые/изменяемые/удаляемые группы и перемещение НЕ реализуем (см. 1.2), «Обновить», затем «Запустить», «Остановить», «Пауза», «Восстановить», «Перезапустить» и подменю «Все сервисы» (Запустить / Остановить / Перезапустить);
   - в строке — иконка статуса: **зелёный кружок = запущено**; другие состояния — серый/оранжевый кружок + текст статуса;
   - контекстное меню строки — тот же набор команд (Запустить/Остановить/Пауза/Восстановить/Перезапустить);
   - доступность кнопок Запустить/Остановить/Пауза/Восстановить зависит от текущего статуса выбранной службы и её возможностей (не все службы поддерживают паузу и восстановление);
   - после выполнения команды статусы обновляются; кнопка «Обновить» перечитывает статусы всех служб списка.
3. «Настроить» — модальное окно со списком «все доступные службы» и галочками (те, что уже в списке — отмечены), ОК переносит изменения в окно «Управление службами».
4. Список служб сохраняется в **`services.json`** (каталог данных профиля, по образцу [ServerListStore.cs](Workspace1C/Services/ServerListStore.cs:30)).
5. Windows: получение списка/статуса/управление — `System.ServiceProcess.ServiceController`.
   Linux: `systemctl` (список `list-units --type=service --all`, статус из вывода; команды `start/stop/pause/continue/restart`); при недоступности `systemctl`/`systemd` — скрываем кнопки действий, список служб недоступен, показывает вежливое сообщение/подсказку.

### 1.2. Что НЕ реализуем (по комментарию 7OH)

- «Добавить группу / Изменить группу / Удалить группу», «Переместить выше / Переместить ниже» — кнопки **скрываем** (`IsVisible=false` в WPF, `IsVisible=false` в Avalonia), чтобы окно соответствовало скриншоту без нерабочих элементов. Резервируем место в строке кнопок комментарием `// Группы (issue #11, итерация 2)`; без групп правый блок «действий» просто начинается с разделителя и кнопки «Обновить».
- Управление службами из трея — не делаем вообще (зафиксировано в «Позже»).

> Решение по «скрыть или отключить»: **скрываем**. Мёртвые кнопки сбивают с толку, а скрытие не ломает структуру — на второй итерации кнопки вернутся на свои места. Группировка и перемещение не влияют на модель данных первой итерации (простое плоское хранилище имён служб), поэтому сокрытие безопасно.

---

## 2. Архитектурные находки проекта, используемые в плане

### 2.1. Двухплатформенная модель окна

Каждое окно имеет WPF-реализацию (`Views/*Window.xaml` + `*.xaml.cs`) и Avalonia-реализацию (`Views/*Window.Avalonia.cs`, обёрнута `#if LINUX`). Файлы `*.Avalonia.cs` исключаются из Windows-сборки в csproj (см. [Workspace1C.csproj](Workspace1C/Workspace1C.csproj:90)) — у Avalonia-версий может не быть XAML, UI строится кодом. Общая логика — в чистом ViewModel без платформенных зависимостей (см. [ServerMonitorViewModel.cs](Workspace1C/ViewModels/ServerMonitorViewModel.cs:21)), в конструктор передаётся `Action<Action>? dispatchToUi`:
- WPF: `action => Application.Current?.Dispatcher.BeginInvoke(action)`;
- Avalonia: `action => Dispatcher.UIThread.Post(action)`;
- тесты: `null`.

Сервисы одной платформы: одиночный класс с двумя файлами, реализация выбирается `#if WINDOWS` / `#if LINUX` — образцы: `RunningInfobasesService.Windows.cs` / `.Linux.cs`, `SystemNotificationChannel.Windows.cs` / `.Linux.cs`.

### 2.2. Меню «Утилиты»

Меню строится динамически по трём источникам:
- каталог пунктов [UtilitiesMenuCatalog.cs](Workspace1C/Services/UtilitiesMenuCatalog.cs:186) (`BuildAll()`, строки 356–442; пункт «Серверы 1С» — строки 369–370);
- стандартная компоновка [UtilitiesMenuLayouts.Default()](Workspace1C/Services/UtilitiesMenuCatalog.cs:455) (пункт `ServerMonitor` — строка 465);
- построители меню: WPF [MainWindow.UtilitiesMenu.cs](Workspace1C/Views/MainWindow.UtilitiesMenu.cs:31) (`ApplyCommand` — строки 231–250, `HotkeyFor` — строки 212–228) и Avalonia [MainWindow.Avalonia.Tree.cs](Workspace1C/Views/MainWindow.Avalonia.Tree.cs:1707) (команды — строки 1886–1891, хоткеи — 1865–1867).

### 2.3. Хоткеи

- Поле в `AppSettings` с дефолтом: [AppSettings.cs](Workspace1C/Models/AppSettings.cs:747) (`HotkeyServerConsole = "Ctrl+Alt+S"`);
- свойство + поле в MainViewModel: WPF [MainViewModel.Admin.cs](Workspace1C/ViewModels/MainViewModel.Admin.cs:19) (setter со `ScheduleSaveSettings` и `NormalizeHotkey`), Avalonia [MainViewModel.Avalonia.Admin.cs](Workspace1C/ViewModels/MainViewModel.Avalonia.Admin.cs:24);
- загрузка из настроек: [MainViewModel.cs](Workspace1C/ViewModels/MainViewModel.cs:461); сохранение: [MainViewModel.Launch.cs](Workspace1C/ViewModels/MainViewModel.Launch.cs:717); сброс/применение из окна настроек: [MainViewModel.Tools.cs](Workspace1C/ViewModels/MainViewModel.Tools.cs:2479) и [MainViewModel.Avalonia.Display.cs](Workspace1C/ViewModels/MainViewModel.Avalonia.Display.cs:497);
- регистрация нажатий: [MainWindow.Hotkeys.cs](Workspace1C/Views/MainWindow.Hotkeys.cs:121) и [MainWindow.Avalonia.Hotkeys.cs](Workspace1C/Views/MainWindow.Avalonia.Hotkeys.cs:184);
- форма «Настройки → Клавиши»: XAML-сетка [SettingsWindow.xaml](Workspace1C/Views/SettingsWindow.xaml:1403) (строки Grid.Row 17–19; при добавлении строки нумерация последующих сдвигается), инициализация [SettingsWindow.xaml.cs](Workspace1C/Views/SettingsWindow.xaml.cs:132), чтение [SettingsWindow.xaml.cs](Workspace1C/Views/SettingsWindow.xaml.cs:498), Avalonia: [SettingsWindow.Avalonia.cs](Workspace1C/Views/SettingsWindow.Avalonia.cs:2841) (`HotkeyRow`), применение в настройках: [MainViewModel.Avalonia.Display.cs](Workspace1C/ViewModels/MainViewModel.Avalonia.Display.cs:497) и `SettingsWindow.xaml.cs` (`Save`).
- Каталог меню связывает пункт с хоткеем строкой имени свойства (например `"HotkeyServerConsole"`), WPF/Avalonia построители резолвят её через switch.

### 2.4. Открытие немодального окна из главного меню

Образец — команда «Серверы 1С»: WPF [MainViewModel.Tools.cs](Workspace1C/ViewModels/MainViewModel.Tools.cs:2679) (`ServerMonitorCommand` + `ExecuteServerMonitor` с single-static-экземпляром окна и активацией существующего), Avalonia [MainViewModel.Avalonia.Tools.cs](Workspace1C/ViewModels/MainViewModel.Avalonia.Tools.cs:1621).

### 2.5. Хранилища JSON

Единый образец — [ServerListStore.cs](Workspace1C/Services/ServerListStore.cs:30): интерфейс + реализация, файл рядом с настройками (`IProfileService.CurrentProfileDataDirectory ?? PlatformPaths.AppDataDirectory`), атомарная запись (`.tmp` + `File.Move(overwrite:true)`), `WriteIndented = true`, `UnsafeRelaxedJsonEscaping`, битый файл → пустой список; регистрация singleton в [AppServices.cs](Workspace1C/AppServices.cs:104).

### 2.6. Чеклист (галочки)

Образец — окно «Импорт из кластера»: строка-модель с `IsChecked` (VM), `DataTemplate` с `CheckBox {Binding IsChecked, UpdateSourceTrigger=PropertyChanged}` ([ClusterImportWindow.xaml](Workspace1C/Views/ClusterImportWindow.xaml:127)); Avalonia — `CheckBox` с биндингом `IsChecked` в коде. VM чистая, окно — тонкая обёртка.

### 2.7. Локализация

JSON-словари [ru.json](Workspace1C/Localization/Languages/ru.json) / `en.json`, ключи — `Section.Key`, доступ через `LocalizationManager.T("...")` и `{loc:Loc ...}`. Группа ключей «Серверы 1С» — строки ~699–701 (настройки) и 1946+ (окно). **Новые ключи добавляем в ОБА файла (ru + en)**.

### 2.8. Тесты

xUnit ([Workspace1C.Tests/Workspace1C.Tests.csproj](Workspace1C.Tests/Workspace1C.Tests.csproj:17)), TargetFramework выбирается по ОС (`net10.0-windows` на Windows / `net10.0` на Linux), символ `LINUX` прокидывается при `BuildLinux=true`. Тесты VM используют `dispatchToUi: null`, хранилища — `directoryOverride: <temp>`.

### 2.9. Сборка и релиз

- Windows single-file: [build-windows-single-file.ps1](Workspace1C/build-windows-single-file.ps1:1) → `Workspace1C/dist/win-x64/Workspace1C.exe`.
- Linux single-file: `build-linux-single-file.ps1` / `.sh` (ForceLinux на Windows) → `Workspace1C/dist/linux-x64/Workspace1C`.
- AppImage: [package/linux/appimage.sh](package/linux/appimage.sh:1) → `package/linux/out/Workspace1C-<версия>-x86_64.AppImage`; deb: [package/linux/deb/build-deb.sh](package/linux/deb/build-deb.sh:1).
- Релиз: тег `v1.0.0.10` активирует [.github/workflows/release.yml](.github/workflows/release.yml:1): Windows exe + Linux `Workspace1C-linux-x64` прикрепляются к GitHub Release, текст из `rel_notes_1.0.0.10.md`, имена asset'ов согласованы с автообновлением `GitHubReleaseService.cs`.

---

## 3. Проектирование

### 3.1. Домен: модель службы и статусы

Модель (`Services/ServiceModels.cs`, чистый .NET) — сериализуемое представление + view-слой:

```csharp
/// Лёгкое представление службы (то, что показываем в списке).
public sealed class ServiceInfo
{
    public string ServiceName { get; set; } = string.Empty;   // Windows: ServiceName; Linux: имя unit'а (.service)
    public string DisplayName { get; set; } = string.Empty;    // Windows: DisplayName; Linux: Description/имя
    public ServiceStatus Status { get; set; }                 // операционный статус
    public bool CanPauseAndContinue { get; set; }             // код 0x102 (Windows); Linux — true (systemd умеет pause? нет — см. ниже)
    public bool CanStop { get; set; } = true;                 // Windows: CanStop; Linux: true
}
```

Правила операционного статуса (вычислитель `ServiceActionAspect`/`ServiceCommandState`):

| Состояние | Запустить | Остановить | Пауза | Восстановить | Перезапустить |
|---|---|---|---|---|---|
| Stopped | ✅ | ⛔ | ⛔ | ⛔ | ⛔ (некоторые платформы умеют restart только у запущенных) |
| Running, пауза поддерживается | ⛔ | ✅ | ✅ | ⛔ | ✅ |
| Running, пауза НЕ поддерживается | ⛔ | ✅ | ⛔ | ⛔ | ✅ |
| Paused (после паузы) | ⛔ | ✅ | ⛔ | ✅ | ⛔ |
| Starting/Stopping/Paused-переход | всё ⛔ | всё ⛔ | всё ⛔ | всё ⛔ | ⛔ |
| Нет выделения | всё ⛔ | всё ⛔ | всё ⛔ | всё ⛔ | всё ⛔ |

> Замечание по Linux: `systemd` НЕ поддерживает pause/continue для обычных сервисов (нет системного аналога `SERVICE_PAUSE_CONTINUE`). Для первой итерации на Linux `CanPauseAndContinue = false` для всех служб — кнопки «Пауза»/«Восстановить» будут неактивны. «Перезапустить» на Linux — `systemctl restart`, допустим для запущенных и остановленных (systemd сам стартует), поэтому разрешаем и для Running, и для Stopped; на Windows `ServiceController.Restart()` требует Running — правило в таблице выше для WPF, для Linux — своя мапа. Маппинг «платформа → таблица доступности» инкапсулируем в **чистый статический вычислитель**, тестируемый по таблице истинности (см. 5.1).

**Иконка статуса:** зелёный кружок «●» (#22C55E) = Running; оранжевый (#F59E0B) = Starting/Stopping; серый/красный (#EF4444 для Stopped? нет — серый #6B7280) = остальное; рядом текст статуса (`Работает / Остановлена / Пауза / Запускается…`). `StatusColorHex` — свойство строки, пересчитывается при обновлении.

### 3.2. Сервис управления службами (платформенный)

Один интерфейс, два файла реализации (паттерн `RunningInfobasesService`):

```csharp
public interface IServiceControllerService
{
    bool IsAvailable { get; }                    // Windows: true; Linux: systemctl найден и systemd отвечает
    IReadOnlyList<ServiceInfo> GetAllServices(); // Windows: ServiceController.GetServices() (+ .GetDevices()? НЕ надо — только сервисы);
                                                 // Linux: systemctl list-units --type=service --all --no-legend --no-pager
    ServiceInfo GetStatus(string serviceName);   // отдельная служба
    bool CanOpenSystemDialog { get; }            // Windows: true; Linux: false
    void OpenSystemServicesDialog();             // Windows: Process.Start(new ProcessStartInfo("services.msc"){UseShellExecute=true});
                                                 // Linux: ничего (кнопка скрыта)
    // Команды. Возвращают true при успехе. Блокирующие обёртки → вызываются из async-метода в фоне.
    bool Start(string serviceName);
    bool Stop(string serviceName);
    bool Pause(string serviceName);
    bool Continue(string serviceName);
    bool Restart(string serviceName);
}
```

Файлы:
- `Services/IServiceControllerService.cs` — интерфейс + enum/модель (или в `ServiceModels.cs`);
- `Services/ServiceControllerService.Windows.cs` — `namespace Workspace1C.Services; #if WINDOWS`; реализация на `System.ServiceProcess.ServiceController`:
  - `GetAllServices()`: `ServiceController.GetServices()` → `new ServiceController(name)`, читаем `DisplayName`, `Status` (`ServiceControllerStatus`), `CanPauseAndContinue` (try/catch — `Win32Exception`/`InvalidOperationException` → false), `CanStop` (логика: Running/Paused → `sc.CanStop`, иначе false);
  - `Pause`/`Continue`: `service.Pause()`/`service.Continue()`; `Restart`: при Status==Running → `service.Stop(); service.Start()` с таймаутами `WaitForStatus` (аналог `ServiceController.Restart()` без него не полагаемся — используем `Stop/Start` последовательно, чтобы контролировать таймауты);
  - `OpenSystemServicesDialog()`: как в [InfobaseAdminService.cs](Workspace1C/Services/InfobaseAdminService.cs:216) — `Process.Start(new ProcessStartInfo { FileName = "services.msc", UseShellExecute = true })`;
  - `IsAvailable = true`.
- `Services/ServiceControllerService.Linux.cs` — `#if LINUX`; реализация поверх `systemctl`:
  - запуск процесса: `Process.Start("systemctl", args)` с `RedirectStandardOutput/Error`, `CreateNoWindow=true`, таймаут ~10–15 c (готовый `ExternalCommandRunner` не подходит — он гоняет через shell с ожиданием без захвата вывода; нужен свой запуск с захватом, как у `RacClient`/`LinuxProcess` — реализовать аккуратным `Process` + `WaitForExitAsync` + чтением stdout);
  - `GetAllServices()`: `systemctl list-units --type=service --all --no-legend --no-pager --plain` → парсинг колонок (UNIT, LOAD, ACTIVE, SUB, DESCRIPTION); единица `*.service`; статус по колонке ACTIVE/SUB: `active` + `running` → Running; `activating` → Starting; `deactivating` → Stopping; `inactive`/`failed` → Stopped; masked → Stopped + `IsAvailable`-подобный флаг не заводим;
  - `IsAvailable`: `systemctl is-system-running --no-pager` возвращает 0 (или `running`/`degraded`) — иначе false;
  - команды: `systemctl start|stop|restart <unit>.service`; Pause/Continue — `RuntimeNotSupportedException`/просто false (кнопки неактивны через `CanPauseAndContinue=false`);
  - `OpenSystemServicesDialog` — `CanOpenSystemDialog = false`.
- Регистрация в DI: `services.AddSingleton<IServiceControllerService, ServiceControllerService>();` — общий (как `RunningInfobasesService` и `ExplorerIntegrationService`, тип один, а реализация выбирается `#if`).

**Права:** на Windows остановка/запуск чужих служб требует прав администратора → команды оборачиваются `try/catch (UnauthorizedAccessException/Win32Exception)`, ошибка показывается в статус-строке окна («Недостаточно прав. Запустите приложение от имени администратора»). На Linux команды также могут требовать root — та же обработка.

### 3.3. Хранилище `services.json`

`Services/ServicesStore.cs` по образцу `ServerListStore`:

```csharp
public interface IServicesStore
{
    IReadOnlyList<string> Load();                    // имена служб (пустой список при отсутствии/битости файла)
    void Save(IReadOnlyCollection<string> serviceNames); // атомарная запись, JSON с отступами
}
```

- `FileName = "services.json"`, каталог: `IProfileService?.CurrentProfileDataDirectory ?? PlatformPaths.AppDataDirectory`, `directoryOverride` для тестов.
- В `AppServices.cs` — `services.AddSingleton<IServicesStore, ServicesStore>();` (рядом с `IServerListStore`, строка ~104).

### 3.4. ViewModel окна «Управление службами»

`ViewModels/ServicesWindowViewModel.cs` — чистый .NET (образец `ServerMonitorViewModel`):

- Конструктор: `(IServiceControllerService services, IServicesStore store, IDialogService dialogs, Action<Action>? dispatchToUi = null)`.
- Свойства:
  - `ObservableCollection<ServiceInfoRow> Rows` — строки списка (модель `ServiceInfoRow : ViewModelBase`: `ServiceName`, `DisplayName`, `StatusText`, `StatusColorHex`, `StatusKind`, `IsBusy`).
  - `SelectedRow` (setter → `RefreshCommandStates()`), `HasRows`, `IsBusy`.
  - Команды: `StartCommand`, `StopCommand`, `PauseCommand`, `ContinueCommand`, `RestartCommand`, `StartAllCommand`, `StopAllCommand`, `RestartAllCommand` (подменю «Все сервисы»), `RefreshCommand`, `ConfigureCommand`, `OpenSystemDialogCommand` (доступность = `services.CanOpenSystemDialog`).
  - Флаги доступности: `CanStart`, `CanStop`, `CanPause`, `CanContinue`, `CanRestart` (вычислитель из 3.1; для «Все сервисы» — true, если в списке есть хоть одна служба с соответствующей операцией).
- Загрузка: при открытии окна — `LoadAsync()`: читаем `store.Load()`, для каждого имени — статус из `GetStatus` (фоновый поток + `dispatchToUi`), параллельно. Пустой список → подсказка «Ничего не добавлено…» (в сам ListBox ничего не добавляем).
- `RefreshAsync()` — пересчитывает статусы всех строк.
- `ExecuteCommandAsync(ServiceAction action, bool all)`: для выбранной (или всех) строк вызываем соответствующий метод сервиса, ошибки → `dialogs.ShowError` или статус-строка, затем `RefreshAsync()` (по требованию issue — «после выполнения команды обновлять статус»).
- `Configure(...)` — открывает окно выбора (см. 3.5); после ОК — обновляет `Rows` из нового набора имён, сохраняет store, обновляет статусы.
- `IsPlatformAvailable` = `services.IsAvailable`; если false — статус-строка с объяснением и блокировка действий.
- Пароль/секреты отсутствуют — окно простое.

### 3.5. ViewModel окна «Настроить» (чеклист)

`ViewModels/ServicesPickerViewModel.cs` (образец `ClusterImportViewModel`):

- Конструктор: `(IServiceControllerService services, IServicesStore store, Action<Action>? dispatchToUi = null)`.
- `ObservableCollection<ServicePickRow> AllServices` — строки `{ Name, DisplayName, IsChecked }`; при инициализации `store.Load()` → `HashSet`, галочка проставлена для уже выбранных.
- `CheckedCount`, команды `SelectAll` / `SelectNone`, `OkCommand` → результат `IReadOnlyList<string> SelectedNames` (только отмеченные, порядок как в AllServices).
- Фильтр для длинного списка (Windows: сотни служб): `SearchText` + `FilteredServices` (только отмеченные + по вхождению в имя — иначе список не пробрать).
- Окно модальное, на ОК VM возвращает результат.

### 3.6. Окна (WPF + Avalonia)

**WPF** (файлы `Views/ServicesWindow.xaml` + `Views/ServicesWindow.xaml.cs`):
- контейнер Grid, заголовок «Службы»; первая строка — кнопки «Настроить» (иконка ⚙/✎ + подсказка) и «Системный диалог» (🪟/🗔, `IsEnabled = CanOpenSystemDialog`; на Linux прячем через привязку `Visibility`); вторая строка — панель кнопок-иконок над списком (стиль `IconOnlyButton` из [ServerMonitorWindow.xaml](Workspace1C/Views/ServerMonitorWindow.xaml:76)); далее `ListBox` (высота ≈ 15 строк ≈ 15×~26px + заголовок — задать `Height="~400"` / `MinHeight=~360`, чтобы строк ~15) с `ItemTemplate`: `Ellipse` (цвет из `StatusColorHex`) + `TextBlock` (DisplayName) + `TextBlock` (StatusText); `ContextMenu` на строке — тот же набор команд; вспомогательный `TextBlock`-подсказка при `!HasRows` (как `serversEmptyHint` в [ServerMonitorWindow.xaml](Workspace1C/Views/ServerMonitorWindow.xaml:128)); внизу статус-строка.
- code-behind: конструктор собирает сервисы из `AppServices`, создаёт VM с `dispatchToUi`, обработчики кликов кнопок → `vm.xxxCommand.Execute(null)`, `Esc` закрывает (паттерн [ServerMonitorWindow.xaml.cs](Workspace1C/Views/ServerMonitorWindow.xaml.cs:104)), контекстное меню — команды VM через `DataContext`.

**Avalonia** (`Views/ServicesWindow.Avalonia.cs`, `#if LINUX`): UI-дерево в коде (паттерн [ServerMonitorWindow.Avalonia.cs](Workspace1C/Views/ServerMonitorWindow.Avalonia.cs:56)); кнопки-иконки — `BuildIconButton`/`BuildActionButton` (как в мониторе); список — `ListBox` с кастомным `ListBoxItem` (Ellipse + тексты); контекстное меню — `ContextMenu` с `MenuItem`s по тем же командам.

Окно выбора для **WPF**: `Views/ServicesPickerWindow.xaml` + `.xaml.cs`; **Avalonia**: `Views/ServicesPickerWindow.Avalonia.cs` (модальное, как `ClusterImportWindow`).

### 3.7. Встраивание в меню, хоткеи, настройки

**Меню (пункт под «Серверы 1С»):**
1. `UtilitiesMenuCommandKind.Services` — новое значение enum ([UtilitiesMenuCatalog.cs](Workspace1C/Services/UtilitiesMenuCatalog.cs:10)).
2. Каталог: пункт `new("Services", "Services.MenuItem", GroupTopLevel, "Cog", "IconSettings", "#06B6D4", UtilitiesMenuCommandKind.Services, "HotkeyServices", isNew: true)` — разместить СРАЗУ ПОСЛЕ `ServerMonitor` (строка 370), до `ServerConsoleExternal`. `isNew:true` → при пользовательской компоновке попадёт в «Новинки программы», пока пользователь не разместит его вручную (стандартное поведение новых пунктов).
3. Стандартная компоновка: в [Default()](Workspace1C/Services/UtilitiesMenuCatalog.cs:465) после `Item("ServerMonitor")` добавить `Item("Services")`.
4. WPF построитель: в `HotkeyFor` — `case "HotkeyServices": return _viewModel.HotkeyServices;`, в `ApplyCommand` — `case UtilitiesMenuCommandKind.Services: item.Command = _viewModel.ShowServicesCommand; break;` ([MainWindow.UtilitiesMenu.cs](Workspace1C/Views/MainWindow.UtilitiesMenu.cs:212)).
5. Avalonia построитель: то же в [MainWindow.Avalonia.Tree.cs](Workspace1C/Views/MainWindow.Avalonia.Tree.cs:1865) (хоткей) и ~1886 (команда).

**MainViewModel (команда и хоткей):** новый partial-файл `ViewModels/MainViewModel.Services.cs`:
- поля: `private ICommand? _showServicesCommand; private string _hotkeyServices = "Ctrl+Shift+S"; private static ServicesWindow? _servicesWindow;`
- `public string HotkeyServices { get/set }` — WPF-сигнатура со `SetProperty`+`NormalizeHotkey(value,"Ctrl+Shift+S")`+`ScheduleSaveSettings()`; Avalonia-сигнатура `=> _settings.HotkeyServices` в `ViewModels/MainViewModel.Avalonia.Services.cs` (или `#if` секции внутри).
- `ShowServicesCommand` → `ExecuteShowServices()` — немодальное открытие с single-static-экземпляром и активацией (копия [ExecuteServerMonitor](Workspace1C/ViewModels/MainViewModel.Tools.cs:2687)).
- Избежать WPF/Avalonia-дублей: минимальные `#if WINDOWS`/`#if LINUX` блоки внутри одного файла, как в существующей паре файлов `MainViewModel.Admin.cs`/`MainViewModel.Avalonia.Admin.cs`. **Рекомендация:** сделать оба файла (`MainViewModel.Services.cs` и `MainViewModel.Avalonia.Services.cs`) по уже устоявшемуся образцу пары Admin/Tools.

**AppSettings:** поле `HotkeyServices { get; set; } = "Ctrl+Shift+S";` ([AppSettings.cs](Workspace1C/Models/AppSettings.cs:747) рядом).
**Загрузка/сохранение:** [MainViewModel.cs](Workspace1C/ViewModels/MainViewModel.cs:461) — строка чтения; [MainViewModel.Launch.cs](Workspace1C/ViewModels/MainViewModel.Launch.cs:717) — запись при сохранении; [MainViewModel.Tools.cs](Workspace1C/ViewModels/MainViewModel.Tools.cs:2479) — сброс из окна настроек (WPF) и [MainViewModel.Avalonia.Display.cs](Workspace1C/ViewModels/MainViewModel.Avalonia.Display.cs:497) (Avalonia) — добавить `_hotkeyServices`/`HotkeyServices` в параметры и `OnPropertyChanged`. См. также `CustomActionEditViewModel.cs:187` — список свойств настроек.

**настройки окна «Настройки → Клавиши»:** новая строка под `HotkeyServerConsoleBox`:
- XAML: [SettingsWindow.xaml](Workspace1C/Views/SettingsWindow.xaml:1411) — добавить строку `Grid.Row` после 18 (сдвинуть нумерацию последующих строк, включая строки 19–21 и те, что дальше); текст `{loc:Loc Settings.Hotkeys.Services}`, поле `HotkeyServicesBox`.
- code-behind: [SettingsWindow.xaml.cs](Workspace1C/Views/SettingsWindow.xaml.cs:132) — `if (HotkeyServicesBox != null) HotkeyServicesBox.Value = viewModel.HotkeyServices;`; [SettingsWindow.xaml.cs](Workspace1C/Views/SettingsWindow.xaml.cs:498) — `var hkServices = ReadHotkeyBox(HotkeyServicesBox);` и передача в `viewModel.ApplyHotkeys(...)`.
- Avalonia: [SettingsWindow.Avalonia.cs](Workspace1C/Views/SettingsWindow.Avalonia.cs:2841) — строка `HotkeyRow(hotkeys, T("Settings.Hotkeys.Services"), _viewModel.HotkeyServices)`.
- **Регистрация хоткея:** [MainWindow.Hotkeys.cs](Workspace1C/Views/MainWindow.Hotkeys.cs:121) — `Add(_viewModel.HotkeyServices, _viewModel.ShowServicesCommand);`; [MainWindow.Avalonia.Hotkeys.cs](Workspace1C/Views/MainWindow.Avalonia.Hotkeys.cs:184) — то же.

**Локализация [ru.json](Workspace1C/Localization/Languages/ru.json) / en.json:**
```
Services.MenuItem        : "Управление службами" / "Service management"
Services.Title           : "Управление службами" / "..."
Services.Configure       : "Настроить"
Services.SystemDialog    : "Системный диалог"
Services.ListTitle       : "Службы"
Services.Empty           : "Ничего не добавлено. Нажмите «Настроить», чтобы выбрать службы."
Services.Refresh         : "Обновить"
Services.Start / Stop / Pause / Continue / Restart
Services.StartAll / StopAll / RestartAll        (подменю «Все сервисы»)
Services.AllServices     : "Все сервисы"
Services.Status.Running / Stopped / Paused / Starting / Stopping / Other / Unknown
Services.NotAvailable    : "Управление службами недоступно на этой системе."
Services.NotAdmin        : "Недостаточно прав для операции со службой."
Settings.Hotkeys.Services: "Управление службами:"
```

---

## 4. Список файлов

### 4.1. Новые файлы

| Файл | Назначение |
|---|---|
| `Workspace1C/Services/ServiceModels.cs` | Модели: `ServiceInfo`, `ServiceStatus` (enum), `ServiceAction` (enum операций), таблицы доступности (чистые, тестируемые) |
| `Workspace1C/Services/IServiceControllerService.cs` | Интерфейс управления службами (общий) |
| `Workspace1C/Services/ServiceControllerService.Windows.cs` | Реализация Win: `ServiceController` + `services.msc` |
| `Workspace1C/Services/ServiceControllerService.Linux.cs` | Реализация Linux: `systemctl` + `IsAvailable` |
| `Workspace1C/Services/ServicesStore.cs` | Хранилище `services.json` (`IServicesStore` + реализация) |
| `Workspace1C/ViewModels/ServicesWindowViewModel.cs` | VM главного окна «Управление службами» |
| `Workspace1C/ViewModels/ServicesPickerViewModel.cs` | VM окна «Настроить» (чеклист) |
| `Workspace1C/Views/ServicesWindow.xaml` | WPF-разметка окна «Управление службами» |
| `Workspace1C/Views/ServicesWindow.xaml.cs` | WPF code-behind |
| `Workspace1C/Views/ServicesWindow.Avalonia.cs` | Avalonia-версия окна (изолирована `#if LINUX`) |
| `Workspace1C/Views/ServicesPickerWindow.xaml` | WPF-разметка окна «Настроить» |
| `Workspace1C/Views/ServicesPickerWindow.xaml.cs` | WPF code-behind |
| `Workspace1C/Views/ServicesPickerWindow.Avalonia.cs` | Avalonia-версия окна выбора |
| `Workspace1C/ViewModels/MainViewModel.Services.cs` | Команда `ShowServicesCommand`, `HotkeyServices`, `ExecuteShowServices` (WPF-блок) |
| `Workspace1C/ViewModels/MainViewModel.Avalonia.Services.cs` | Avalonia-блок команды/хоткея |
| `Workspace1C.Tests/ServicesStoreTests.cs` | Тесты хранилища |
| `Workspace1C.Tests/ServiceActionAvailabilityTests.cs` | Тесты таблицы доступности команд |
| `Workspace1C.Tests/ServicesWindowViewModelTests.cs` | Тесты VM главного окна (fake `IServiceControllerService`) |
| `Workspace1C.Tests/ServicesPickerViewModelTests.cs` | Тесты VM чеклиста |
| `rel_notes_1.0.0.10.md` | Текст релиза (ассет для `release.yml`) |

### 4.2. Изменяемые файлы

| Файл | Что меняем |
|---|---|
| `Workspace1C/Workspace1C.csproj` | `<Version>1.0.0.10</Version>` и 3 соседних тега (строки 62–65) |
| `Workspace1C/AppServices.cs` | `AddSingleton<IServicesStore, ServicesStore>()` и `AddSingleton<IServiceControllerService, ServiceControllerService>()` |
| `Workspace1C/Models/AppSettings.cs` | `HotkeyServices = "Ctrl+Shift+S"` (рядом со строкой 747) |
| `Workspace1C/Services/UtilitiesMenuCatalog.cs` | `UtilitiesMenuCommandKind.Services`; пункт каталога после `ServerMonitor` (строка 370); `Item("Services")` в `Default()` (строка 465) |
| `Workspace1C/Views/MainWindow.UtilitiesMenu.cs` | `HotkeyFor` + `ApplyCommand` — ветки для `Services` |
| `Workspace1C/Views/MainWindow.Avalonia.Tree.cs` | те же ветки для Avalonia |
| `Workspace1C/Views/MainWindow.Hotkeys.cs` | `Add(_viewModel.HotkeyServices, _viewModel.ShowServicesCommand)` |
| `Workspace1C/Views/MainWindow.Avalonia.Hotkeys.cs` | то же |
| `Workspace1C/ViewModels/MainViewModel.cs` | чтение `HotkeyServices` из настроек (строка ~462) |
| `Workspace1C/ViewModels/MainViewModel.Launch.cs` | сохранение `_hotkeyServices` в AppSettings (строка ~718) |
| `Workspace1C/ViewModels/MainViewModel.Tools.cs` | параметр `hotkeyServices` и `OnPropertyChanged(nameof(HotkeyServices))` (строки ~2480, ~2518) |
| `Workspace1C/ViewModels/MainViewModel.Avalonia.Display.cs` | то же (строки ~498, ~531) |
| `Workspace1C/Views/SettingsWindow.xaml` | строка «Управление службами» + `HotkeyServicesBox`; сдвиг нумерации Grid.Row ниже |
| `Workspace1C/Views/SettingsWindow.xaml.cs` | инициализация и чтение `HotkeyServicesBox` (строки ~132, ~498) |
| `Workspace1C/Views/SettingsWindow.Avalonia.cs` | строка `HotkeyRow` для `Settings.Hotkeys.Services` |
| `Workspace1C/Localization/Languages/ru.json` | новые ключи `Services.*`, `Settings.Hotkeys.Services` |
| `Workspace1C/Localization/Languages/en.json` | те же ключи (перевод) |
| `CHANGELOG.md` | новый раздел `[1.0.0.10]` (над `[1.0.0.9]`, строка 13) |
| `README.md` | упоминание функции «Управление службами» в списке возможностей |
| `.github/workflows/build.yml` / `release.yml` | НЕ трогаем, если не потребуется (ничего не сломали) — проверяем только что новые файлы проходят `dotnet build` обеих платформ |

---

## 5. Тесты

Фреймворк — xUnit (см. 2.8). Тестируем чистую логику; платформенные реализации НЕ тестируем напрямую (как `RunningInfobasesService`), вместо них — fake `IServiceControllerService` в тестах VM.

1. **`ServicesStoreTests`** (по образцу `ServerListStoreTests`): пустой файл → пустой список; запись/чтение round-trip; битый JSON → пустой список без исключений; `directoryOverride` изолирует от профиля; атомарность (`.tmp` не остаётся).
2. **`ServiceActionAvailabilityTests`**: исчерпывающая таблица из 3.1 (каждый статус × каждая операция): ожидание `true/false`; проверка влияния `CanPauseAndContinue`; «нет выделения» → всё недоступно; «Все сервисы» (all-варианты) — true, если хоть одна служба применима.
3. **`ServicesWindowViewModelTests`** (fake-сервис с управляемыми статусами): загрузка списка из store и статусов; после `Start/Stop/Pause/Continue/Restart` — вызовы fake-сервиса и обновление статуса строки; `Refresh` перечитывает статусы; `CanStart/CanStop/...` меняются при смене выделения; пустой список → `HasRows=false`; сбой команды (fake возвращает false) → сообщение через fake `IDialogService`, статус не «зависает»; команды «Все сервисы» вызывают сервис для каждой строки.
4. **`ServicesPickerViewModelTests`**: проставленные галочки для уже сохранённых служб; `SelectedNames` возвращает отмеченные в порядке списка; `SelectAll/SelectNone`; фильтр поиска (отмеченные не скрываются).
5. **`ServicesMenuIntegrationTests`** (по образцу `BookmarksMenuHotkeyTests`/`IconSetCoverageTests`): пункт каталога `Services` существует, стоит после `ServerMonitor`, имеет `CommandKind=Services`, `HotkeyKey="HotkeyServices"`; `Default()` содержит `Services` сразу после `ServerMonitor`.
6. **`AppSettingsThemeMigrationTests`-подобные**: не требуется — дефолт `HotkeyServices` проверяется конструктором `AppSettings`.

---

## 6. Порядок реализации (шаги)

1. **Версия:** поднять `1.0.0.9 → 1.0.0.10` в [Workspace1C.csproj](Workspace1C/Workspace1C.csproj:62) (4 тега).
2. **Модели и чистые таблицы:** `ServiceModels.cs` + `ServiceActionAvailability` (или статический класс внутри) + тест `ServiceActionAvailabilityTests` — зелёный.
3. **Хранилище:** `ServicesStore.cs` + `IServicesStore` + регистрация в `AppServices.cs` + `ServicesStoreTests`.
4. **Сервис управления:** `IServiceControllerService` + `.Windows.cs` + `.Linux.cs` + регистрация в DI.
5. **VM окна:** `ServicesWindowViewModel` + тесты (fake-сервис).
6. **VM чеклиста:** `ServicesPickerViewModel` + тесты.
7. **Окно WPF:** `ServicesWindow.xaml/.xaml.cs` (+ picker), проверка на Windows.
8. **Окно Avalonia:** `ServicesWindow.Avalonia.cs` (+ picker); сборка Linux (`./build.sh` или `build-linux-single-file.ps1` на Windows).
9. **Меню и хоткей:**
   - `UtilitiesMenuCatalog.cs` (enum + пункт + компоновка);
   - `MainViewModel.Services.cs` / `MainViewModel.Avalonia.Services.cs` (`ShowServicesCommand`, `HotkeyServices`, `ExecuteShowServices`);
   - `MainViewModel.cs` / `MainViewModel.Launch.cs` / `MainViewModel.Tools.cs` / `MainViewModel.Avalonia.Display.cs` — чтение/сохранение/сброс;
   - `MainWindow.UtilitiesMenu.cs` / `MainWindow.Avalonia.Tree.cs` — ветки меню;
   - `MainWindow.Hotkeys.cs` / `MainWindow.Avalonia.Hotkeys.cs` — регистрация Ctrl+Shift+S.
10. **Настройки клавиш:** `SettingsWindow.xaml` (новая строка + сдвиг Grid.Row), `SettingsWindow.xaml.cs`, `SettingsWindow.Avalonia.cs`.
11. **Локализация:** ключи в `ru.json`/`en.json`.
12. **Контекстное меню строки** и подменю «Все сервисы» — в рамках шагов 7–8 (вёрстка обоих окон).
13. **Документация:** `CHANGELOG.md` (раздел [1.0.0.10]), `README.md`, `rel_notes_1.0.0.10.md`.
14. **Прогон тестов:** `dotnet test` на Windows (WPF) и Linux (Avalonia) — обе конфигурации из CI [build.yml](.github/workflows/build.yml:33).

---

## 7. Сборка и публикация релиза v1.0.0.10

### 7.1. Локальная сборка

| Цель | Команда | Результат |
|---|---|---|
| Windows single-file | `cd Workspace1C && .\build-windows-single-file.ps1 -Configuration Release` | `Workspace1C/dist/win-x64/Workspace1C.exe` |
| Linux single-file (с Windows) | `cd Workspace1C && .\build-linux-single-file.ps1 -Configuration Release` | `Workspace1C/dist/linux-x64/Workspace1C` (ForceLinux) |
| Linux single-file (на Linux) | `cd Workspace1C && ./build-linux-single-file.sh Release` | то же |
| AppImage | `package/linux/appimage.sh` (после Linux single-file) | `package/linux/out/Workspace1C-1.0.0.10-x86_64.AppImage` |
| deb | `package/linux/deb/build-deb.sh` (после Linux single-file) | `package/linux/deb/out/…deb` |

Тесты перед релизом: `dotnet test Workspace1C.Tests/Workspace1C.Tests.csproj -c Release -p:RuntimeIdentifier= -p:SelfContained=false -p:PublishSingleFile=false`.

### 7.2. Публикация на GitHub

1. Коммит и push в `main` (CI [build.yml](.github/workflows/build.yml:1) проверит обе платформы).
2. Создать **тег `v1.0.0.10`** на коммите с изменениями и запушить: `git tag v1.0.0.10 && git push origin v1.0.0.10` (или GitHub Release → «Choose a tag» → опубликовать; тег заводится автоматически).
3. Workflow [release.yml](.github/workflows/release.yml:1) соберёт Windows exe и Linux single-file и создаст/обновит GitHub Release **`Workspace 1C 1.0.0.10`** с ассетами:
   - `Workspace1C.exe`;
   - `Workspace1C-linux-x64`;
   - текст — из `rel_notes_1.0.0.10.md` (создать в корне; иначе — заглушка «Подробности — в CHANGELOG.md»).
4. **Не закрывать issue #11.** Добавить в issue #11 комментарий:
   > «Исправлено в версии 1.0.0.10 (Windows и Linux)» + краткое описание изменений (окно «Управление службами», чеклист «Настроить», services.json, кнопки/контекстное меню, подменю «Все сервисы», индикация статуса, системный диалог на Windows) + ссылка на релиз `https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.10`.
5. Опционально: прикрепить AppImage/deb ассетами вручную (workflow грузит только два single-file; если нужен AppImage — скачать из артефактов выпуска или загрузить файлы `package/linux/out/*.AppImage` и `*.deb` через UI релиза).

---

## 8. Риски и решения

| Риск | Решение |
|---|---|
| WPF: `System.ServiceProcess` требует `net10.0-windows` | TFM уже `net10.0-windows` — пакет фреймворка доступен; добавить `<UseWindowsForms>` не нужно (не используется). Проверить сборку в шаге 7. |
| Linux: `systemctl` недоступен (не systemd, контейнер) | `IsAvailable=false` → окно открывается, список пуст, кнопки действий скрыты, статус-строка: «Управление службами недоступно на этой системе». |
| Linux: нет аналога паузы | `CanPauseAndContinue=false` у всех Linux-служб; кнопки «Пауза/Восстановить» неактивны (таблица доступности под это заточена). |
| Права администратора (Win)/root (Linux) | Ошибки `UnauthorizedAccessException`/`Win32Exception`, ненулевой exit code systemctl → сообщение «Недостаточно прав…» в статус-строке/диалог; приложение не падает. |
| Нет выделенной строки | Все кнопки действий неактивны (см. таблицу 3.1). |
| Список из `services.json` содержит службу, которая исчезла на машине | `GetStatus` для неизвестной службы → статус «Не найдена», строка остаётся, команды неактивны; окно «Настроить» позволяет снять галочку. |
| Дубликаты имён | Store-нормализация: `Distinct`, `Trim`, пропуск пустых (по образцу `ServerListStore.Normalize`). |
| Огромный список в чеклисте (сотни служб) | Фильтр поиска + «Выбрать все/Сбросить» в окне «Настроить». |
| Утечка контролов при закрытии немодального окна | `Closed` → `vm.Dispose()` (нет таймеров — только подписки на коллекции, освобождать нечего; тем не менее придерживаемся паттерна монитора). |
| Новая строка в XAML «Клавиши» сдвигает Grid.Row | Внимательно пронумеровать строки ниже (17–21+); проверить Avalonia-таб целиком (там вёрстка кодом, сдвиг не нужен). |
| `isNew:true` прячет пункт в «Новинки» при пользовательской компоновке | Ожидаемое поведение системы новых пунктов; в стандартной компоновке пункт сразу на месте под «Серверы 1С». |

---

## 9. Приёмка (checklist по тексту issue)

- [ ] Пункт «Управление службами» под «Серверы 1С», хоткей Ctrl+Shift+S виден в меню и работает; в «Настройки → Клавиши» хоткей меняется.
- [ ] Окно открывается; первая строка: «Настроить» и «Системный диалог» (последняя только на Windows, открывает services.msc).
- [ ] Список с заголовком «Службы» высотой ~15 строк; пустой список — подсказка, ничего в ListBox не добавляется.
- [ ] Над списком: кнопки (без групп): Обновить · Запустить · Остановить · Пауза · Восстановить · Перезапустить · подменю «Все сервисы» (Запустить/Остановить/Перезапустить).
- [ ] «Настроить» → чеклист доступных служб с уже отмеченными; ОК переносит изменения и сохраняет `services.json`.
- [ ] Кнопки Запустить/Остановить/Пауза/Восстановить меняют доступность по выделенной службе и её возможностям.
- [ ] Зелёный кружок «●» = запущена; статусы обновляются после команд и по «Обновить».
- [ ] Контекстное меню строки — тот же набор команд.
- [ ] Версия 1.0.0.10; CHANGELOG, README, релиз-ноты обновлены; issue #11 НЕ закрыт, комментарий добавлен; релиз v1.0.0.10 с ассетами опубликован (Windows + Linux).