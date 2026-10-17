using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Строка списка «Управление службами» (issue #11): имя/отображаемое имя,
/// операционный статус с иконкой (цвет кружка) и возможностями (пауза/остановка).
/// Чистый .NET — обе платформы (WPF и Avalonia).
/// </summary>
public sealed class ServiceInfoRow : ViewModelBase
{
    private ServiceStatus _statusKind = ServiceStatus.Unknown;
    private string _statusText = string.Empty;
    private string _statusColorHex = ServiceStatusDisplay.ColorHex(ServiceStatus.Unknown);
    private bool _isBusy;

    public ServiceInfoRow(ServiceInfo info) => Update(info);

    /// <summary>Windows: ServiceName; Linux: имя unit'а (например postgresql.service).</summary>
    public string ServiceName { get; private set; } = string.Empty;

    /// <summary>Отображаемое имя (Fallback — имя службы).</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Поддерживает ли служба паузу/продолжение (Windows: код 0x102; Linux: false).</summary>
    public bool CanPauseAndContinue { get; private set; }

    /// <summary>Можно ли остановить службу.</summary>
    public bool CanStop { get; private set; } = true;

    /// <summary>Операционный статус (используется таблицей доступности команд).</summary>
    public ServiceStatus StatusKind
    {
        get => _statusKind;
        private set => SetProperty(ref _statusKind, value);
    }

    /// <summary>Локализованный текст статуса («Работает» / «Остановлена» / …).</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Цвет кружка статуса #RRGGBB (зелёный = запущена, см. ServiceStatusDisplay).</summary>
    public string StatusColorHex
    {
        get => _statusColorHex;
        private set => SetProperty(ref _statusColorHex, value);
    }

    /// <summary>Служба в фазе операции — команды для строки временно недоступны.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    /// <summary>Обновляет строку по данным службы (статус, возможности, тексты).</summary>
    public void Update(ServiceInfo info)
    {
        if (info is null)
            return;
        ServiceName = info.ServiceName ?? string.Empty;
        DisplayName = string.IsNullOrWhiteSpace(info.DisplayName) ? ServiceName : info.DisplayName;
        StatusKind = info.Status;
        CanPauseAndContinue = info.CanPauseAndContinue;
        CanStop = info.CanStop;
        StatusText = LocalizationManager.T(ServiceStatusDisplay.TextKey(info.Status));
        StatusColorHex = ServiceStatusDisplay.ColorHex(info.Status);
        OnPropertyChanged(nameof(ServiceName));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(CanPauseAndContinue));
        OnPropertyChanged(nameof(CanStop));
    }
}

/// <summary>
/// Отображение статуса службы: ключ локализации текста и цвет кружка
/// (зелёный = запущена; оранжевый = переходные состояния; серый = остальные;
/// красный = служба не найдена).
/// </summary>
public static class ServiceStatusDisplay
{
    public static string TextKey(ServiceStatus status) => status switch
    {
        ServiceStatus.Running => "Services.Status.Running",
        ServiceStatus.Stopped => "Services.Status.Stopped",
        ServiceStatus.Paused => "Services.Status.Paused",
        ServiceStatus.Starting => "Services.Status.Starting",
        ServiceStatus.Stopping => "Services.Status.Stopping",
        ServiceStatus.NotFound => "Services.Status.NotFound",
        _ => "Services.Status.Unknown"
    };

    public static string ColorHex(ServiceStatus status) => status switch
    {
        ServiceStatus.Running => "#22C55E",
        ServiceStatus.Starting => "#F59E0B",
        ServiceStatus.Stopping => "#F59E0B",
        ServiceStatus.NotFound => "#EF4444",
        _ => "#6B7280"
    };
}

/// <summary>
/// ViewModel окна «Управление службами» (issue #11, первая итерация): список выбранных
/// служб (services.json через <see cref="IServicesStore"/>), статусы и команды
/// Запустить/Остановить/Пауза/Восстановить/Перезапустить (в т.ч. «Все сервисы»),
/// «Обновить», «Настроить» (чеклист) и системный диалог (Windows). Чистый .NET без
/// платформенных зависимостей — окна только привязываются; конструктор принимает
/// <c>dispatchToUi</c> (WPF: Dispatcher.BeginInvoke; Avalonia: Dispatcher.UIThread.Post;
/// тесты: null).
/// </summary>
public sealed class ServicesWindowViewModel : ViewModelBase
{
    private readonly IServiceControllerService _services;
    private readonly IServicesStore _store;
    private readonly IDialogService _dialogs;
    private readonly Action<Action>? _dispatchToUi;
    private readonly ServicePlatform _platform;

    private ServiceInfoRow? _selectedRow;
    private bool _isBusy;
    private string _statusText = string.Empty;
    private int _reloadVersion;

    private ICommand? _startCommand;
    private ICommand? _stopCommand;
    private ICommand? _pauseCommand;
    private ICommand? _continueCommand;
    private ICommand? _restartCommand;
    private ICommand? _startAllCommand;
    private ICommand? _stopAllCommand;
    private ICommand? _restartAllCommand;
    private ICommand? _refreshCommand;
    private ICommand? _configureCommand;
    private ICommand? _openSystemDialogCommand;

    /// <param name="services">Платформенный сервис управления службами.</param>
    /// <param name="store">Хранилище списка служб (services.json).</param>
    /// <param name="dialogs">Диалоги (сообщение об ошибках операции).</param>
    /// <param name="dispatchToUi">Доставка применения результатов в UI-поток; null — тесты.</param>
    /// <param name="platform">Платформа для таблицы доступности (Linux разрешает
    /// «Перезапустить» остановленной службы — systemctl restart).</param>
    public ServicesWindowViewModel(
        IServiceControllerService services,
        IServicesStore store,
        IDialogService dialogs,
        Action<Action>? dispatchToUi = null,
        ServicePlatform platform = ServicePlatform.Windows)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _dispatchToUi = dispatchToUi;
        _platform = platform;
        if (!_services.IsAvailable)
            _statusText = LocalizationManager.T("Services.NotAvailable");
    }

    /// <summary>Строки списка служб.</summary>
    public ObservableCollection<ServiceInfoRow> Rows { get; } = new();

    /// <summary>Выбранная строка; при смене пересчитывается доступность команд.</summary>
    public ServiceInfoRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
                RefreshCommandStates();
        }
    }

    /// <summary>Есть ли строки в списке (для подсказки «Ничего не добавлено»).</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>Идёт ли операция (спиннер/блокировка).</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>Статус-строка окна (например, «Управление недоступно»).</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Доступно ли управление службами на этой системе (systemd на Linux).</summary>
    public bool IsPlatformAvailable => _services.IsAvailable;

    /// <summary>Есть ли системный диалог служб (Windows: true; Linux: false — кнопка скрыта).</summary>
    public bool CanOpenSystemDialog => _services.CanOpenSystemDialog;

    // ===================== Доступность команд (таблица 3.1 плана) =====================

    public bool CanStart { get; private set; }
    public bool CanStop { get; private set; }
    public bool CanPause { get; private set; }
    public bool CanContinue { get; private set; }
    public bool CanRestart { get; private set; }
    public bool CanStartAll { get; private set; }
    public bool CanStopAll { get; private set; }
    public bool CanRestartAll { get; private set; }

    /// <summary>
    /// Провайдер модального окна «Настроить» (чеклист служб): возвращает выбранные
    /// имена или null при отмене. Устанавливается окном «Управление службами».
    /// </summary>
    public Func<IReadOnlyList<string>?>? ServicesPickerProvider { get; set; }

    // ===================== Команды =====================

    /// <summary>«Запустить» — выбранную службу.</summary>
    public ICommand StartCommand => _startCommand ??= new RelayCommand(async () => await ExecuteCommandAsync(ServiceAction.Start, all: false));

    /// <summary>«Остановить» — выбранную службу.</summary>
    public ICommand StopCommand => _stopCommand ??= new RelayCommand(async () => await ExecuteCommandAsync(ServiceAction.Stop, all: false));

    /// <summary>«Пауза» — выбранную службу (только если поддерживает и запущена).</summary>
    public ICommand PauseCommand => _pauseCommand ??= new RelayCommand(async () => await ExecuteCommandAsync(ServiceAction.Pause, all: false));

    /// <summary>«Восстановить» — выбранную службу после паузы.</summary>
    public ICommand ContinueCommand => _continueCommand ??= new RelayCommand(async () => await ExecuteCommandAsync(ServiceAction.Continue, all: false));

    /// <summary>«Перезапустить» — выбранную службу.</summary>
    public ICommand RestartCommand => _restartCommand ??= new RelayCommand(async () => await ExecuteCommandAsync(ServiceAction.Restart, all: false));

    /// <summary>Подменю «Все сервисы → Запустить».</summary>
    public ICommand StartAllCommand => _startAllCommand ??= new RelayCommand(async () => await ExecuteCommandAsync(ServiceAction.Start, all: true));

    /// <summary>Подменю «Все сервисы → Остановить».</summary>
    public ICommand StopAllCommand => _stopAllCommand ??= new RelayCommand(async () => await ExecuteCommandAsync(ServiceAction.Stop, all: true));

    /// <summary>Подменю «Все сервисы → Перезапустить».</summary>
    public ICommand RestartAllCommand => _restartAllCommand ??= new RelayCommand(async () => await ExecuteCommandAsync(ServiceAction.Restart, all: true));

    /// <summary>«Обновить» — перечитать статусы всех служб списка.</summary>
    public ICommand RefreshCommand => _refreshCommand ??= new RelayCommand(async () => await RefreshAsync());

    /// <summary>«Настроить» — модальный чеклист всех служб системы.</summary>
    public ICommand ConfigureCommand => _configureCommand ??= new RelayCommand(async () => await ConfigureAsync());

    /// <summary>«Системный диалог» — services.msc (кнопка скрыта на Linux).</summary>
    public ICommand OpenSystemDialogCommand =>
        _openSystemDialogCommand ??= new RelayCommand(_services.OpenSystemServicesDialog, () => _services.CanOpenSystemDialog);

    // ===================== Загрузка =====================

    /// <summary>Загружает список служб из хранилища и их статусы (при открытии окна).</summary>
    public async Task LoadAsync()
    {
        var names = _store.Load();
        await RebuildRowsAsync(names);
    }

    /// <summary>Перечитывает статусы всех строк списка.</summary>
    public async Task RefreshAsync()
    {
        var names = Rows.Select(r => r.ServiceName).ToList();
        await RebuildRowsAsync(names);
    }

    /// <summary>
    /// Применяет новый набор служб (после «Настроить»): сохраняет services.json,
    /// перестраивает строки и перечитывает статусы.
    /// </summary>
    public async Task ApplyConfigurationAsync(IReadOnlyList<string> names)
    {
        _store.Save(names);
        await RebuildRowsAsync(names);
    }

    /// <summary>Перестраивает строки: статусы опрашиваются в фоне, строки обновляются в UI-потоке.</summary>
    private async Task RebuildRowsAsync(IReadOnlyList<string> names)
    {
        _reloadVersion++;
        var version = _reloadVersion;
        if (_services.IsAvailable)
            IsBusy = true;
        try
        {
            var statuses = new List<ServiceInfo>();
            if (_services.IsAvailable)
            {
                await Task.Run(() =>
                {
                    foreach (var name in names)
                        statuses.Add(_services.GetStatus(name));
                });
            }

            ApplyOnUi(() =>
            {
                if (version != _reloadVersion)
                    return;
                var selectedName = _selectedRow?.ServiceName;
                Rows.Clear();
                foreach (var info in statuses)
                    Rows.Add(new ServiceInfoRow(info));
                OnPropertyChanged(nameof(HasRows));
                if (selectedName is not null)
                    SelectedRow = Rows.FirstOrDefault(r => r.ServiceName == selectedName);
                RefreshCommandStates();
            });
        }
        finally
        {
            ApplyOnUi(() =>
            {
                if (version == _reloadVersion)
                    IsBusy = false;
            });
        }
    }

    // ===================== Операции =====================

    /// <summary>
    /// Выполняет операцию над выбранной (или всеми подходящими) службами, затем
    /// перечитывает статусы. Публичный для обхода из окон и юнит-тестов.
    /// </summary>
    public async Task ExecuteCommandAsync(ServiceAction action, bool all)
    {
        if (!_services.IsAvailable)
            return;

        var targets = CollectTargets(action, all);
        if (targets.Count == 0)
            return;

        IsBusy = true;
        var failures = new List<string>();
        try
        {
            var snapshot = targets.Select(r => (Row: r, Name: r.ServiceName)).ToList();
            await Task.Run(() =>
            {
                foreach (var (row, serviceName) in snapshot)
                {
                    if (!ExecuteOnService(action, serviceName))
                        failures.Add(row.DisplayName);
                }
            });
        }
        finally
        {
            IsBusy = false;
        }

        if (failures.Count > 0)
        {
            _dialogs.ShowError(
                LocalizationManager.T("Services.NotAdmin"),
                LocalizationManager.T("Services.Title"));
        }

        // После команды статусы перечитываются (требование issue #11).
        await RefreshAsync();
    }

    /// <summary>Строки-цели операции: выбранная (если применима) или все применимые для «Все сервисы».</summary>
    private List<ServiceInfoRow> CollectTargets(ServiceAction action, bool all)
    {
        if (all)
        {
            return Rows
                .Where(r => ServiceActionAvailability.CanExecute(action, r.StatusKind, r.CanPauseAndContinue, r.CanStop, _platform))
                .ToList();
        }

        var row = SelectedRow;
        if (row is null ||
            !ServiceActionAvailability.CanExecute(action, row.StatusKind, row.CanPauseAndContinue, row.CanStop, _platform))
        {
            return new List<ServiceInfoRow>();
        }
        return new List<ServiceInfoRow> { row };
    }

    private bool ExecuteOnService(ServiceAction action, string serviceName) => action switch
    {
        ServiceAction.Start => _services.Start(serviceName),
        ServiceAction.Stop => _services.Stop(serviceName),
        ServiceAction.Pause => _services.Pause(serviceName),
        ServiceAction.Continue => _services.Continue(serviceName),
        ServiceAction.Restart => _services.Restart(serviceName),
        _ => false
    };

    /// <summary>«Настроить»: показывает чеклист через провайдер и применяет результат.</summary>
    private async Task ConfigureAsync()
    {
        if (ServicesPickerProvider is not { } provider)
            return;
        var names = provider.Invoke();
        if (names is null)
            return; // отменено пользователем
        await ApplyConfigurationAsync(names);
    }

    /// <summary>Пересчитывает доступность всех команд по текущему выделению и списку.</summary>
    private void RefreshCommandStates()
    {
        var row = SelectedRow;
        CanStart = row is not null && ServiceActionAvailability.CanExecute(ServiceAction.Start, row.StatusKind, row.CanPauseAndContinue, row.CanStop, _platform);
        CanStop = row is not null && ServiceActionAvailability.CanExecute(ServiceAction.Stop, row.StatusKind, row.CanPauseAndContinue, row.CanStop, _platform);
        CanPause = row is not null && ServiceActionAvailability.CanExecute(ServiceAction.Pause, row.StatusKind, row.CanPauseAndContinue, row.CanStop, _platform);
        CanContinue = row is not null && ServiceActionAvailability.CanExecute(ServiceAction.Continue, row.StatusKind, row.CanPauseAndContinue, row.CanStop, _platform);
        CanRestart = row is not null && ServiceActionAvailability.CanExecute(ServiceAction.Restart, row.StatusKind, row.CanPauseAndContinue, row.CanStop, _platform);

        var infos = Rows.Select(r => new ServiceInfo
        {
            ServiceName = r.ServiceName,
            Status = r.StatusKind,
            CanPauseAndContinue = r.CanPauseAndContinue,
            CanStop = r.CanStop
        }).ToList();
        CanStartAll = ServiceActionAvailability.CanExecuteAny(infos, ServiceAction.Start, _platform);
        CanStopAll = ServiceActionAvailability.CanExecuteAny(infos, ServiceAction.Stop, _platform);
        CanRestartAll = ServiceActionAvailability.CanExecuteAny(infos, ServiceAction.Restart, _platform);

        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(CanRestart));
        OnPropertyChanged(nameof(CanStartAll));
        OnPropertyChanged(nameof(CanStopAll));
        OnPropertyChanged(nameof(CanRestartAll));
    }

    /// <summary>Применяет действие в UI-потоке (dispatch) или сразу (тесты).</summary>
    private void ApplyOnUi(Action action)
    {
        if (_dispatchToUi is null)
            action();
        else
            _dispatchToUi(action);
    }
}