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
/// Базовая строка списка «Управление службами» (issue #11, итерация 2 — группы служб,
/// релиз 1.0.0.12): общие свойства строки — отображаемое имя, цвет кружка статуса,
/// текст статуса и признак группы. Список окна смешанный: службы
/// (<see cref="ServiceInfoRow"/>) и группы (<see cref="ServiceGroupRow"/>).
/// Чистый .NET — обе платформы (WPF и Avalonia).
/// </summary>
public abstract class ServiceListRow : ViewModelBase
{
    /// <summary>Отображаемое имя (служба: DisplayName; группа: имя группы).</summary>
    public abstract string DisplayName { get; protected set; }

    /// <summary>Цвет кружка статуса #RRGGBB.</summary>
    public abstract string StatusColorHex { get; protected set; }

    /// <summary>Локализованный текст статуса.</summary>
    public abstract string StatusText { get; protected set; }

    /// <summary>Строка — группа служб (для DataTrigger в шаблоне строки).</summary>
    public abstract bool IsGroup { get; }

    /// <summary>Тег типа строки: "service" / "group" (вспомогательный для триггеров XAML).</summary>
    public abstract string GroupTag { get; }
}

/// <summary>
/// Строка списка «Управление службами» (issue #11): имя/отображаемое имя,
/// операционный статус с иконкой (цвет кружка) и возможностями (пауза/остановка).
/// Чистый .NET — обе платформы (WPF и Avalonia).
/// </summary>
public sealed class ServiceInfoRow : ServiceListRow
{
    private ServiceStatus _statusKind = ServiceStatus.Unknown;
    private string _statusText = string.Empty;
    private string _statusColorHex = ServiceStatusDisplay.ColorHex(ServiceStatus.Unknown);
    private bool _isBusy;

    public ServiceInfoRow(ServiceInfo info) => Update(info);

    /// <summary>Windows: ServiceName; Linux: имя unit'а (например postgresql.service).</summary>
    public string ServiceName { get; private set; } = string.Empty;

    /// <summary>Отображаемое имя (Fallback — имя службы).</summary>
    public override string DisplayName { get; protected set; } = string.Empty;

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
    public override string StatusText
    {
        get => _statusText;
        protected set => SetProperty(ref _statusText, value);
    }

    /// <summary>Цвет кружка статуса #RRGGBB (зелёный = запущена, см. ServiceStatusDisplay).</summary>
    public override string StatusColorHex
    {
        get => _statusColorHex;
        protected set => SetProperty(ref _statusColorHex, value);
    }

    /// <summary>Служба в фазе операции — команды для строки временно недоступны.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    /// <inheritdoc />
    public override bool IsGroup => false;

    /// <inheritdoc />
    public override string GroupTag => "service";

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
/// Строка-группа служб (issue #11, итерация 2 — группы служб, релиз 1.0.0.12):
/// имя группы, состав (имена служб в порядке пользователя) и агрегированный статус
/// (все запущены / частично / не запущены / неизвестно) с цветом кружка и текстом.
/// Статус пересчитывается по статусам членов (<see cref="ServiceGroupStatusResolver"/>).
/// </summary>
public sealed class ServiceGroupRow : ServiceListRow
{
    private GroupStatusKind _groupStatus = GroupStatusKind.Unknown;
    private string _statusText = string.Empty;
    private string _statusColorHex = ServiceGroupStatusDisplay.ColorHex(GroupStatusKind.Unknown);

    /// <param name="group">Группа из хранилища (имя + состав).</param>
    /// <param name="statuses">Статусы служб (по имени) для расчёта агрегированного статуса.</param>
    public ServiceGroupRow(ServiceGroup group, IReadOnlyDictionary<string, ServiceInfo> statuses)
    {
        Name = group.Name;
        ChildServices = group.Services.ToList();
        UpdateChildrenStatuses(statuses);
    }

    /// <summary>Имя группы (описание; уникально — используется как ключ сохранения).</summary>
    public string Name { get; }

    /// <summary>Имя группы для сохранения (совпадает с <see cref="Name"/>).</summary>
    public string GroupId => Name;

    /// <summary>Имена служб группы в порядке пользователя.</summary>
    public IReadOnlyList<string> ChildServices { get; }

    /// <summary>Агрегированный статус группы (все запущены / частично / не запущены / неизвестно).</summary>
    public GroupStatusKind GroupStatus
    {
        get => _groupStatus;
        private set => SetProperty(ref _groupStatus, value);
    }

    /// <inheritdoc />
    public override string DisplayName
    {
        get => Name;
        protected set { /* имя группы неизменяемо после создания строки */ }
    }

    /// <inheritdoc />
    public override string StatusText
    {
        get => _statusText;
        protected set => SetProperty(ref _statusText, value);
    }

    /// <inheritdoc />
    public override string StatusColorHex
    {
        get => _statusColorHex;
        protected set => SetProperty(ref _statusColorHex, value);
    }

    /// <inheritdoc />
    public override bool IsGroup => true;

    /// <inheritdoc />
    public override string GroupTag => "group";

    /// <summary>
    /// Пересчитывает статус группы по статусам членов (issue #11, итерация 2):
    /// все Running → AllRunning; хотя бы один Running → Partial; иначе None;
    /// пустой состав → Unknown. NotFound-член считается «не запущен» (см.
    /// <see cref="ServiceGroupStatusResolver"/>).
    /// </summary>
    public void UpdateChildrenStatuses(IReadOnlyDictionary<string, ServiceInfo> statuses)
    {
        var memberStatuses = ChildServices
            .Select(n => statuses is not null && statuses.TryGetValue(n, out var info) ? info.Status : ServiceStatus.Unknown)
            .ToList();
        GroupStatus = ServiceGroupStatusResolver.Resolve(memberStatuses);
        StatusText = LocalizationManager.T(ServiceGroupStatusDisplay.TextKey(GroupStatus));
        StatusColorHex = ServiceGroupStatusDisplay.ColorHex(GroupStatus);
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
/// Отображение статуса группы служб (issue #11, итерация 2): ключ локализации
/// текста и цвет кружка — зелёный #22C55E (все запущены), жёлтый #F59E0B
/// (запущена не вся группа, но хотя бы одна), серый #6B7280 (ни одна не запущена
/// или неизвестно). Красный для группы не используется (см. <see cref="ServiceGroupStatusResolver"/>).
/// </summary>
public static class ServiceGroupStatusDisplay
{
    public static string TextKey(GroupStatusKind kind) => kind switch
    {
        GroupStatusKind.AllRunning => "Services.Group.Status.AllRunning",
        GroupStatusKind.Partial => "Services.Group.Status.Partial",
        GroupStatusKind.None => "Services.Group.Status.None",
        _ => "Services.Status.Unknown"
    };

    public static string ColorHex(GroupStatusKind kind) => kind switch
    {
        GroupStatusKind.AllRunning => "#22C55E",
        GroupStatusKind.Partial => "#F59E0B",
        _ => "#6B7280"
    };
}

/// <summary>
/// ViewModel окна «Управление службами» (issue #11): список выбранных служб и групп
/// служб (services.json через <see cref="IServicesStore"/>), статусы и команды
/// Запустить/Остановить/Пауза/Восстановить/Перезапустить (в т.ч. «Все сервисы» и
/// команды группы), «Обновить», «Настроить» (чеклист), управление группами
/// (добавить/изменить/удалить через <see cref="GroupEditorProvider"/> — окно
/// редактирования появится в Задаче 3) и системный диалог (Windows). Итерация 2
/// (релиз 1.0.0.12): строки списка смешанные — службы и группы, порядок сохраняется
/// в <see cref="ServiceStoreData.Order"/>. Чистый .NET без платформенных зависимостей —
/// окна только привязываются; конструктор принимает <c>dispatchToUi</c> (WPF:
/// Dispatcher.BeginInvoke; Avalonia: Dispatcher.UIThread.Post; тесты: null).
/// </summary>
public sealed class ServicesWindowViewModel : ViewModelBase
{
    private readonly IServiceControllerService _services;
    private readonly IServicesStore _store;
    private readonly IDialogService _dialogs;
    private readonly Action<Action>? _dispatchToUi;
    private readonly ServicePlatform _platform;

    private ServiceListRow? _selectedRow;
    private bool _isBusy;
    private string _statusText = string.Empty;
    private int _reloadVersion;
    private Dictionary<string, ServiceInfo> _statusCache = new(StringComparer.OrdinalIgnoreCase);

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
    private ICommand? _moveUpCommand;
    private ICommand? _moveDownCommand;
    private ICommand? _grantRightsCommand;
    private ICommand? _startGroupCommand;
    private ICommand? _stopGroupCommand;
    private ICommand? _pauseGroupCommand;
    private ICommand? _continueGroupCommand;
    private ICommand? _restartGroupCommand;
    private ICommand? _addGroupCommand;
    private ICommand? _editGroupCommand;
    private ICommand? _deleteGroupCommand;

    private Func<ServiceGroup?, IReadOnlyList<string>, ServiceGroup?>? _groupEditorProvider;

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

    /// <summary>Строки списка: службы и группы (порядок = порядок в списке, смешанный).</summary>
    public ObservableCollection<ServiceListRow> Rows { get; } = new();

    /// <summary>Выбранная строка (служба или группа); при смене пересчитывается доступность команд.</summary>
    public ServiceListRow? SelectedRow
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

    /// <summary>
    /// Можно ли выдать права на управление службами одним запросом повышения прав
    /// (issue #11, 7OH 17.10): Windows — кнопка «Выдать права»; Linux — скрыта.
    /// </summary>
    public bool CanGrantRights => _services.CanGrantControlRights;

    /// <summary>Можно ли переместить выбранную строку выше (issue #11: порядок задаёт пользователь).</summary>
    public bool CanMoveUp => _selectedRow is not null && Rows.IndexOf(_selectedRow) > 0;

    /// <summary>Можно ли переместить выбранную строку ниже.</summary>
    public bool CanMoveDown => _selectedRow is not null && Rows.IndexOf(_selectedRow) is var i && i >= 0 && i < Rows.Count - 1;

    // ===================== Доступность команд (таблица 3.1 плана) =====================

    public bool CanStart { get; private set; }
    public bool CanStop { get; private set; }
    public bool CanPause { get; private set; }
    public bool CanContinue { get; private set; }
    public bool CanRestart { get; private set; }
    public bool CanStartAll { get; private set; }
    public bool CanStopAll { get; private set; }
    public bool CanRestartAll { get; private set; }

    // ===================== Доступность команд группы (issue #11, итерация 2) =====================

    /// <summary>«Запустить» доступно для выбранной группы (хотя бы один член применим).</summary>
    public bool CanStartGroup { get; private set; }

    /// <summary>«Остановить» доступно для выбранной группы.</summary>
    public bool CanStopGroup { get; private set; }

    /// <summary>«Пауза» доступна для выбранной группы.</summary>
    public bool CanPauseGroup { get; private set; }

    /// <summary>«Восстановить» доступно для выбранной группы.</summary>
    public bool CanContinueGroup { get; private set; }

    /// <summary>«Перезапустить» доступно для выбранной группы.</summary>
    public bool CanRestartGroup { get; private set; }

    /// <summary>Можно ли добавить группу (окно редактирования установлено — Задача 3).</summary>
    public bool CanAddGroup => GroupEditorProvider is not null;

    /// <summary>Можно ли изменить выбранную группу (провайдер установлен и выбрана группа).</summary>
    public bool CanEditGroup { get; private set; }

    /// <summary>Можно ли удалить выбранную группу.</summary>
    public bool CanDeleteGroup { get; private set; }

    /// <summary>
    /// Провайдер модального окна «Настроить» (чеклист служб): возвращает выбранные
    /// имена или null при отмене. Устанавливается окном «Управление службами».
    /// </summary>
    public Func<IReadOnlyList<string>?>? ServicesPickerProvider { get; set; }

    /// <summary>
    /// Провайдер окна редактирования группы (issue #11, итерация 2; окно — Задача 3):
    /// вход — редактируемая группа (null для новой) и список служб, доступных для
    /// выбора (общий список минус уже используемые в ДРУГИХ группах плюс свои члены);
    /// выход — готовая группа или null при отмене. Пока провайдер не установлен,
    /// команды «Добавить/Изменить группу» неактивны.
    /// </summary>
    public Func<ServiceGroup?, IReadOnlyList<string>, ServiceGroup?>? GroupEditorProvider
    {
        get => _groupEditorProvider;
        set
        {
            if (_groupEditorProvider == value)
                return;
            _groupEditorProvider = value;
            RefreshCommandStates();
        }
    }

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

    /// <summary>«Обновить» — перечитать статусы всех строк списка.</summary>
    public ICommand RefreshCommand => _refreshCommand ??= new RelayCommand(async () => await RefreshAsync());

    /// <summary>«Настроить» — модальный чеклист всех служб системы.</summary>
    public ICommand ConfigureCommand => _configureCommand ??= new RelayCommand(async () => await ConfigureAsync());

    /// <summary>«Переместить выше» — порядок списка сохраняется в services.json (работает для служб и групп).</summary>
    public ICommand MoveUpCommand => _moveUpCommand ??= new RelayCommand(() => Move(-1));

    /// <summary>«Переместить ниже».</summary>
    public ICommand MoveDownCommand => _moveDownCommand ??= new RelayCommand(() => Move(+1));

    /// <summary>«Выдать права» — права на управление всеми службами списка (один запрос UAC).</summary>
    public ICommand GrantRightsCommand => _grantRightsCommand ??=
        new RelayCommand(async () => await GrantRightsAsync(Rows.OfType<ServiceInfoRow>().Select(r => r.ServiceName).ToList(), confirm: false));

    /// <summary>«Системный диалог» — services.msc (кнопка скрыта на Linux).</summary>
    public ICommand OpenSystemDialogCommand =>
        _openSystemDialogCommand ??= new RelayCommand(_services.OpenSystemServicesDialog, () => _services.CanOpenSystemDialog);

    // ===================== Команды группы (issue #11, итерация 2) =====================

    /// <summary>«Запустить» — все члены выбранной группы (в порядке списка).</summary>
    public ICommand StartGroupCommand => _startGroupCommand ??= new RelayCommand(async () => await ExecuteGroupCommandAsync(ServiceAction.Start));

    /// <summary>«Остановить» — все члены выбранной группы.</summary>
    public ICommand StopGroupCommand => _stopGroupCommand ??= new RelayCommand(async () => await ExecuteGroupCommandAsync(ServiceAction.Stop));

    /// <summary>«Пауза» — все члены выбранной группы.</summary>
    public ICommand PauseGroupCommand => _pauseGroupCommand ??= new RelayCommand(async () => await ExecuteGroupCommandAsync(ServiceAction.Pause));

    /// <summary>«Восстановить» — все члены выбранной группы.</summary>
    public ICommand ContinueGroupCommand => _continueGroupCommand ??= new RelayCommand(async () => await ExecuteGroupCommandAsync(ServiceAction.Continue));

    /// <summary>«Перезапустить» — все члены выбранной группы.</summary>
    public ICommand RestartGroupCommand => _restartGroupCommand ??= new RelayCommand(async () => await ExecuteGroupCommandAsync(ServiceAction.Restart));

    /// <summary>«Добавить группу» — окно редактирования (Задача 3); новая группа — в начало списка.</summary>
    public ICommand AddGroupCommand => _addGroupCommand ??= new RelayCommand(async () => await AddGroupAsync());

    /// <summary>«Изменить группу» — выбранную группу.</summary>
    public ICommand EditGroupCommand => _editGroupCommand ??= new RelayCommand(async () => await EditGroupAsync());

    /// <summary>«Удалить группу» — выбранную группу (с подтверждением).</summary>
    public ICommand DeleteGroupCommand => _deleteGroupCommand ??= new RelayCommand(async () => await DeleteGroupAsync());

    // ===================== Загрузка =====================

    /// <summary>Загружает список служб и групп из хранилища и их статусы (при открытии окна).</summary>
    public async Task LoadAsync()
    {
        var data = _store.Load();
        await RebuildRowsAsync(data);
    }

    /// <summary>Перечитывает статусы всех строк списка (из хранилища — источник истины).</summary>
    public async Task RefreshAsync()
    {
        var data = _store.Load();
        await RebuildRowsAsync(data);
    }

    /// <summary>
    /// Применяет новый набор служб (после «Настроить»): сохраняет services.json
    /// (группы и их порядок сохраняются), перестраивает строки и перечитывает статусы.
    /// </summary>
    public async Task ApplyConfigurationAsync(IReadOnlyList<string> names)
    {
        // Порядок задаёт пользователь (issue #11): уже добавленные службы остаются на своих
        // местах, новые — в конец (в порядке чеклиста — по алфавиту). Группы и их позиции
        // в общем порядке сохраняются (итерация 2).
        var data = _store.Load();
        var current = Rows.OfType<ServiceInfoRow>().Select(r => r.ServiceName).ToList();
        if (current.Count == 0)
            current = data.Services.ToList();
        var merged = MergeOrder(current, names);
        var added = merged.Where(n => !current.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();

        var newData = new ServiceStoreData
        {
            Services = merged.ToList(),
            Groups = data.Groups.Select(g => new ServiceGroup { Name = g.Name, Services = g.Services.ToList() }).ToList(),
            Order = RebuildOrderForServices(data, merged)
        };
        _store.Save(newData);
        await RebuildRowsAsync(_store.Load());

        // Новым службам сразу предлагаем выдать права (7OH 17.10): один запрос повышения.
        if (added.Count > 0 && CanGrantRights)
            await GrantRightsAsync(added, confirm: true);
    }

    /// <summary>
    /// Новый состав списка с сохранением пользовательского порядка: службы из
    /// <paramref name="current"/>, оставшиеся выбранными, — в прежнем порядке; затем новые
    /// из <paramref name="selected"/> в их порядке. Сравнение имён без учёта регистра.
    /// </summary>
    public static List<string> MergeOrder(IReadOnlyList<string> current, IReadOnlyList<string> selected)
    {
        var selectedSet = new HashSet<string>(selected.Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
        var result = current.Select(n => n.Trim()).Where(selectedSet.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var present = new HashSet<string>(result, StringComparer.OrdinalIgnoreCase);
        foreach (var name in selected.Select(n => n.Trim()))
        {
            if (name.Length > 0 && present.Add(name))
                result.Add(name);
        }
        return result;
    }

    /// <summary>Перемещает выбранную строку на <paramref name="delta"/> позиций и сохраняет порядок.</summary>
    private void Move(int delta)
    {
        var row = _selectedRow;
        if (row is null)
            return;
        var index = Rows.IndexOf(row);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Rows.Count)
            return;
        Rows.Move(index, target);
        _store.Save(BuildStoreData());
        RefreshCommandStates();
    }

    /// <summary>
    /// Выдача прав на управление службами (issue #11, 7OH 17.10): один запрос повышения
    /// прав на все службы; итог — модальным сообщением. <paramref name="confirm"/> — сначала
    /// спросить (при добавлении служб через «Настроить»).
    /// </summary>
    public async Task GrantRightsAsync(IReadOnlyList<string> names, bool confirm)
    {
        if (!CanGrantRights || names.Count == 0)
            return;
        var title = LocalizationManager.T("Services.Title");
        if (confirm && !_dialogs.Confirm(
                Fmt("Services.GrantRights.ConfirmFormat", names.Count), title))
            return;

        IsBusy = true;
        ServiceRightsResult result;
        try
        {
            result = await Task.Run(() => _services.GrantControlRights(names));
        }
        finally
        {
            IsBusy = false;
        }

        if (result.Canceled)
        {
            StatusText = LocalizationManager.T("Services.GrantRights.Canceled");
            return;
        }
        var message = FormatRightsResult(result);
        if (result.Failed.Count > 0 || !string.IsNullOrEmpty(result.Error))
            _dialogs.ShowWarning(message, title);
        else
            _dialogs.ShowInfo(message, title);
        StatusText = message;
        await RefreshAsync();
    }

    /// <summary>Шаблон локализации с аргументами; без «{0}» в шаблоне аргументы дописываются.</summary>
    private static string Fmt(string key, params object[] args)
    {
        var template = LocalizationManager.T(key);
        return template.Contains("{0}", StringComparison.Ordinal)
            ? string.Format(template, args)
            : template + " " + string.Join(" ", args);
    }

    /// <summary>Текст итога выдачи прав (выдано / уже были / ошибки).</summary>
    public static string FormatRightsResult(ServiceRightsResult result)
    {
        var lines = new List<string>();
        if (result.Granted.Count > 0)
            lines.Add(Fmt("Services.GrantRights.GrantedFormat", string.Join(", ", result.Granted)));
        if (result.AlreadyGranted.Count > 0)
            lines.Add(Fmt("Services.GrantRights.AlreadyFormat", string.Join(", ", result.AlreadyGranted)));
        foreach (var pair in result.Failed)
            lines.Add(Fmt("Services.GrantRights.FailedFormat", pair.Key, pair.Value));
        if (!string.IsNullOrEmpty(result.Error))
            lines.Add(result.Error);
        return lines.Count == 0
            ? LocalizationManager.T("Services.GrantRights.Nothing")
            : string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Перестраивает строки по данным хранилища: статусы опрашиваются в фоне, строки
    /// обновляются в UI-потоке. Порядок строк — из <see cref="ServiceStoreData.Order"/>
    /// (смешанный: службы и группы).
    /// </summary>
    private async Task RebuildRowsAsync(ServiceStoreData data)
    {
        _reloadVersion++;
        var version = _reloadVersion;
        if (_services.IsAvailable)
            IsBusy = true;
        try
        {
            var order = ServiceStoreData.NormalizeOrder(data);
            var allNames = order
                .Where(k => !ServiceStoreData.IsGroupKey(k))
                .Concat(data.Groups.SelectMany(g => g.Services))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var statuses = new Dictionary<string, ServiceInfo>(StringComparer.OrdinalIgnoreCase);
            if (_services.IsAvailable)
            {
                await Task.Run(() =>
                {
                    foreach (var name in allNames)
                        statuses[name] = _services.GetStatus(name);
                });
            }

            ApplyOnUi(() =>
            {
                if (version != _reloadVersion)
                    return;
                _statusCache = statuses;
                var selectedKey = SelectedRowKey();
                Rows.Clear();
                foreach (var key in order)
                {
                    if (ServiceStoreData.IsGroupKey(key))
                    {
                        var groupName = key.Substring(ServiceStoreData.GroupKeyPrefix.Length);
                        var group = data.Groups.FirstOrDefault(g => string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
                        if (group is not null)
                            Rows.Add(new ServiceGroupRow(group, statuses));
                    }
                    else if (statuses.TryGetValue(key, out var info))
                    {
                        Rows.Add(new ServiceInfoRow(info));
                    }
                }
                OnPropertyChanged(nameof(HasRows));
                RestoreSelection(selectedKey);
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
            // Сообщение модальное (окно служб — владелец диалога, issue #11); на Windows
            // подсказываем кнопку «Выдать права» вместо запуска программы от администратора.
            _dialogs.ShowError(
                LocalizationManager.T(CanGrantRights ? "Services.NotAdminGrantHint" : "Services.NotAdmin")
                    + Environment.NewLine + string.Join(", ", failures),
                LocalizationManager.T("Services.Title"));
        }

        // После команды статусы перечитываются (требование issue #11).
        await RefreshAsync();
    }

    /// <summary>
    /// Выполняет операцию над членами выбранной группы (issue #11, итерация 2):
    /// действие применяется к применимым членам В ПОРЯДКЕ списка группы; доступность
    /// команды — <see cref="ServiceActionAvailability.CanExecuteAny"/> по членам.
    /// </summary>
    public async Task ExecuteGroupCommandAsync(ServiceAction action)
    {
        if (!_services.IsAvailable)
            return;
        if (SelectedRow is not ServiceGroupRow group)
            return;

        var targets = group.ChildServices
            .Select(n => (Name: n, Info: StatusOf(n)))
            .Where(t => ServiceActionAvailability.CanExecute(action, t.Info.Status, t.Info.CanPauseAndContinue, t.Info.CanStop, _platform))
            .Select(t => t.Name)
            .ToList();
        if (targets.Count == 0)
            return;

        IsBusy = true;
        var failures = new List<string>();
        try
        {
            var snapshot = targets.ToList();
            await Task.Run(() =>
            {
                foreach (var serviceName in snapshot)
                {
                    if (!ExecuteOnService(action, serviceName))
                        failures.Add(serviceName);
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
                LocalizationManager.T(CanGrantRights ? "Services.NotAdminGrantHint" : "Services.NotAdmin")
                    + Environment.NewLine + string.Join(", ", failures),
                LocalizationManager.T("Services.Title"));
        }

        await RefreshAsync();
    }

    /// <summary>Строки-цели операции: выбранная (если применима) или все применимые для «Все сервисы».</summary>
    private List<ServiceInfoRow> CollectTargets(ServiceAction action, bool all)
    {
        if (all)
        {
            return Rows.OfType<ServiceInfoRow>()
                .Where(r => ServiceActionAvailability.CanExecute(action, r.StatusKind, r.CanPauseAndContinue, r.CanStop, _platform))
                .ToList();
        }

        if (SelectedRow is not ServiceInfoRow row ||
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

    // ===================== Управление группами (issue #11, итерация 2) =====================

    /// <summary>
    /// «Добавить группу»: окно редактирования (Задача 3) через <see cref="GroupEditorProvider"/>;
    /// новая группа вставляется в НАЧАЛО списка (Rows.Insert(0), комментарий 7OH) и сохраняется.
    /// </summary>
    public async Task AddGroupAsync()
    {
        if (GroupEditorProvider is not { } provider)
            return;
        var available = AvailableServicesForGroup(null);
        var edited = provider.Invoke(null, available);
        if (edited is null)
            return; // отменено пользователем
        var normalized = ServiceStoreData.Normalize(new ServiceStoreData { Groups = new List<ServiceGroup> { edited } });
        if (normalized.Groups.Count == 0)
            return;
        var group = normalized.Groups[0];

        var data = _store.Load();
        data.Groups.Add(group);
        data.Order = InsertGroupAtStart(data, group.Name);
        _store.Save(data);
        await RebuildRowsAsync(_store.Load());
        SelectedRow = Rows.OfType<ServiceGroupRow>()
            .FirstOrDefault(g => string.Equals(g.Name, group.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// «Изменить группу»: окно редактирования (Задача 3) для выбранной группы; позиция
    /// группы в общем порядке сохраняется (при переименовании обновляется ключ Order).
    /// </summary>
    public async Task EditGroupAsync()
    {
        if (GroupEditorProvider is not { } provider)
            return;
        if (SelectedRow is not ServiceGroupRow row)
            return;
        var data = _store.Load();
        var group = data.Groups.FirstOrDefault(g => string.Equals(g.Name, row.Name, StringComparison.OrdinalIgnoreCase));
        if (group is null)
            return;
        var available = AvailableServicesForGroup(group);
        var edited = provider.Invoke(group, available);
        if (edited is null)
            return; // отменено пользователем
        var normalized = ServiceStoreData.Normalize(new ServiceStoreData { Groups = new List<ServiceGroup> { edited } });
        if (normalized.Groups.Count == 0)
            return;
        var newGroup = normalized.Groups[0];

        var index = data.Groups.FindIndex(g => string.Equals(g.Name, row.Name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return;
        data.Groups[index] = newGroup;
        if (!string.Equals(newGroup.Name, row.Name, StringComparison.OrdinalIgnoreCase))
        {
            var oldKey = ServiceStoreData.GroupKey(row.Name);
            var newKey = ServiceStoreData.GroupKey(newGroup.Name);
            data.Order = data.Order
                .Select(k => string.Equals(k, oldKey, StringComparison.OrdinalIgnoreCase) ? newKey : k)
                .ToList();
        }
        _store.Save(data);
        await RebuildRowsAsync(_store.Load());
        SelectedRow = Rows.OfType<ServiceGroupRow>()
            .FirstOrDefault(g => string.Equals(g.Name, newGroup.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// «Удалить группу»: подтверждение через <see cref="IDialogService.Confirm"/>, удаление
    /// группы из хранилища (и ключа из Order) и перестроение строк.
    /// </summary>
    public async Task DeleteGroupAsync()
    {
        if (SelectedRow is not ServiceGroupRow row)
            return;
        var title = LocalizationManager.T("Services.Title");
        if (!_dialogs.Confirm(Fmt("Services.Group.DeleteConfirm", row.Name), title))
            return;

        var data = _store.Load();
        data.Groups.RemoveAll(g => string.Equals(g.Name, row.Name, StringComparison.OrdinalIgnoreCase));
        var key = ServiceStoreData.GroupKey(row.Name);
        data.Order = data.Order.Where(k => !string.Equals(k, key, StringComparison.OrdinalIgnoreCase)).ToList();
        _store.Save(data);
        await RebuildRowsAsync(_store.Load());
    }

    /// <summary>
    /// Список служб, доступных для выбора в окне редактирования группы (Задача 3):
    /// общий список (службы вне групп + члены всех групп) минус службы, уже
    /// используемые в ДРУГИХ группах; свои члены остаются доступными.
    /// </summary>
    public IReadOnlyList<string> AvailableServicesForGroup(ServiceGroup? group)
    {
        var data = _store.Load();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in data.Groups)
        {
            if (group is not null && string.Equals(g.Name, group.Name, StringComparison.OrdinalIgnoreCase))
                continue; // свои члены доступны для выбора
            foreach (var serviceName in g.Services)
                used.Add(serviceName);
        }

        var all = new List<string>();
        all.AddRange(data.Services);
        foreach (var g in data.Groups)
            all.AddRange(g.Services);
        return all
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => !used.Contains(n))
            .ToList();
    }

    /// <summary>
    /// Вставляет ключ новой группы в начало общего порядка (комментарий 7OH).
    /// Идемпотентно: если ключ уже присутствует (например, группа добавлена в
    /// <see cref="ServiceStoreData.Groups"/> до вызова — как в <see cref="AddGroupAsync"/>),
    /// дубликат удаляется, затем ключ вставляется в начало (Задача 5, issue #11).
    /// </summary>
    private static List<string> InsertGroupAtStart(ServiceStoreData data, string groupName)
    {
        var order = ServiceStoreData.NormalizeOrder(data);
        var key = ServiceStoreData.GroupKey(groupName);
        order.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
        order.Insert(0, key);
        return order;
    }

    /// <summary>
    /// Перестраивает общий порядок после изменения списка служб через «Настроить»:
    /// группы остаются на своих местах, службы — в прежнем относительном порядке,
    /// новые службы дописываются в конец.
    /// </summary>
    private static List<string> RebuildOrderForServices(ServiceStoreData data, IReadOnlyList<string> merged)
    {
        var mergedSet = new HashSet<string>(merged, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in ServiceStoreData.NormalizeOrder(data))
        {
            if (ServiceStoreData.IsGroupKey(key))
            {
                if (seen.Add(key))
                    result.Add(key);
            }
            else if (mergedSet.Contains(key) && seen.Add(key))
            {
                result.Add(key);
            }
        }
        foreach (var name in merged)
        {
            if (seen.Add(name))
                result.Add(name);
        }
        foreach (var group in data.Groups)
        {
            var key = ServiceStoreData.GroupKey(group.Name);
            if (seen.Add(key))
                result.Add(key);
        }
        return result;
    }

    /// <summary>Собирает ServiceStoreData из текущих строк (службы, группы и общий порядок).</summary>
    private ServiceStoreData BuildStoreData()
    {
        var data = new ServiceStoreData();
        var order = new List<string>();
        foreach (var row in Rows)
        {
            switch (row)
            {
                case ServiceInfoRow service:
                    data.Services.Add(service.ServiceName);
                    order.Add(service.ServiceName);
                    break;
                case ServiceGroupRow group:
                    data.Groups.Add(new ServiceGroup { Name = group.Name, Services = group.ChildServices.ToList() });
                    order.Add(ServiceStoreData.GroupKey(group.Name));
                    break;
            }
        }
        data.Order = order;
        return data;
    }

    /// <summary>Статус службы из кэша последнего опроса (Unknown, если службы нет в кэше).</summary>
    private ServiceInfo StatusOf(string serviceName) =>
        _statusCache.TryGetValue(serviceName, out var info)
            ? info
            : new ServiceInfo { ServiceName = serviceName, Status = ServiceStatus.Unknown };

    /// <summary>Ключ выбранной строки для восстановления выделения после перестроения.</summary>
    private string? SelectedRowKey() => _selectedRow switch
    {
        ServiceInfoRow service => service.ServiceName,
        ServiceGroupRow group => ServiceStoreData.GroupKey(group.Name),
        _ => null
    };

    /// <summary>Восстанавливает выделение по ключу (служба или группа).</summary>
    private void RestoreSelection(string? key)
    {
        if (key is null)
        {
            SelectedRow = null;
            return;
        }
        if (ServiceStoreData.IsGroupKey(key))
        {
            var name = key.Substring(ServiceStoreData.GroupKeyPrefix.Length);
            SelectedRow = Rows.OfType<ServiceGroupRow>()
                .FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            SelectedRow = Rows.OfType<ServiceInfoRow>()
                .FirstOrDefault(r => string.Equals(r.ServiceName, key, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Пересчитывает доступность всех команд по текущему выделению и списку.</summary>
    private void RefreshCommandStates()
    {
        var row = SelectedRow;

        // Команды службы — только для строки-службы.
        CanStart = row is ServiceInfoRow s && ServiceActionAvailability.CanExecute(ServiceAction.Start, s.StatusKind, s.CanPauseAndContinue, s.CanStop, _platform);
        CanStop = row is ServiceInfoRow s2 && ServiceActionAvailability.CanExecute(ServiceAction.Stop, s2.StatusKind, s2.CanPauseAndContinue, s2.CanStop, _platform);
        CanPause = row is ServiceInfoRow s3 && ServiceActionAvailability.CanExecute(ServiceAction.Pause, s3.StatusKind, s3.CanPauseAndContinue, s3.CanStop, _platform);
        CanContinue = row is ServiceInfoRow s4 && ServiceActionAvailability.CanExecute(ServiceAction.Continue, s4.StatusKind, s4.CanPauseAndContinue, s4.CanStop, _platform);
        CanRestart = row is ServiceInfoRow s5 && ServiceActionAvailability.CanExecute(ServiceAction.Restart, s5.StatusKind, s5.CanPauseAndContinue, s5.CanStop, _platform);

        // Команды группы — только для строки-группы (доступность — CanExecuteAny по членам).
        if (row is ServiceGroupRow group)
        {
            var members = group.ChildServices.Select(StatusOf).ToList();
            CanStartGroup = ServiceActionAvailability.CanExecuteAny(members, ServiceAction.Start, _platform);
            CanStopGroup = ServiceActionAvailability.CanExecuteAny(members, ServiceAction.Stop, _platform);
            CanPauseGroup = ServiceActionAvailability.CanExecuteAny(members, ServiceAction.Pause, _platform);
            CanContinueGroup = ServiceActionAvailability.CanExecuteAny(members, ServiceAction.Continue, _platform);
            CanRestartGroup = ServiceActionAvailability.CanExecuteAny(members, ServiceAction.Restart, _platform);
        }
        else
        {
            CanStartGroup = CanStopGroup = CanPauseGroup = CanContinueGroup = CanRestartGroup = false;
        }

        // «Все сервисы» — по строкам-службам.
        var infos = Rows.OfType<ServiceInfoRow>().Select(r => new ServiceInfo
        {
            ServiceName = r.ServiceName,
            Status = r.StatusKind,
            CanPauseAndContinue = r.CanPauseAndContinue,
            CanStop = r.CanStop
        }).ToList();
        CanStartAll = ServiceActionAvailability.CanExecuteAny(infos, ServiceAction.Start, _platform);
        CanStopAll = ServiceActionAvailability.CanExecuteAny(infos, ServiceAction.Stop, _platform);
        CanRestartAll = ServiceActionAvailability.CanExecuteAny(infos, ServiceAction.Restart, _platform);

        // Управление группами.
        CanEditGroup = GroupEditorProvider is not null && row is ServiceGroupRow;
        CanDeleteGroup = row is ServiceGroupRow;

        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(CanRestart));
        OnPropertyChanged(nameof(CanStartGroup));
        OnPropertyChanged(nameof(CanStopGroup));
        OnPropertyChanged(nameof(CanPauseGroup));
        OnPropertyChanged(nameof(CanContinueGroup));
        OnPropertyChanged(nameof(CanRestartGroup));
        OnPropertyChanged(nameof(CanStartAll));
        OnPropertyChanged(nameof(CanStopAll));
        OnPropertyChanged(nameof(CanRestartAll));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(CanAddGroup));
        OnPropertyChanged(nameof(CanEditGroup));
        OnPropertyChanged(nameof(CanDeleteGroup));
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