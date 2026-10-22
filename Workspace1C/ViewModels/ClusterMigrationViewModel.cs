using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel диалога «Миграция баз» (issue #41): перенос информационных баз между
/// кластерами серверов 1С. Окно импорта передаёт отмеченные строки с исходными
/// записями rac (<see cref="ClusterImportRow.Source"/>) и параметры подключения к
/// серверу-источнику; здесь выбирается сервер назначения из справочника «Серверы 1С»
/// (БЕЗ сервера-источника), его кластер, вводятся учётные данные СУБД (для регистрации
/// базы в новом кластере) и галочка «Не удалять в старом» (копия без удаления).
/// По «Перенести» для КАЖДОЙ базы: сначала <c>infobase create</c> в новом кластере
/// (те же данные, что в старом), затем — если галочка не отмечена — <c>infobase drop</c>
/// без <c>--drop-database</c> (БД на сервере СУБД остаётся). Фоном показывается прогресс
/// «N из M: база». При успехе формируется <see cref="Result"/> — окно спрашивает
/// «Хотите заменить строку подключения?» и закрывается (MainViewModel заменит сервер/порт
/// в списке приложения). Чистый .NET без платформенных зависимостей — обе платформы.
/// </summary>
public sealed class ClusterMigrationViewModel : ViewModelBase
{
    private readonly IRacClient _rac;
    private readonly RacConnectionParams _sourceParams;
    private readonly Guid _sourceClusterId;
    private readonly IReadOnlyList<ClusterImportRow> _rows;
    private readonly Action<Action>? _dispatchToUi;
    private readonly IAppLogger? _logger;

    private int _busy;
    private IReadOnlyList<ManagementServer> _availableServers = Array.Empty<ManagementServer>();
    private ManagementServer? _selectedServer;
    private IReadOnlyList<RacCluster> _clusters = Array.Empty<RacCluster>();
    private Guid? _selectedClusterId;
    private string _dbUser = string.Empty;
    private string _dbPassword = string.Empty;
    private bool _keepInSource;
    private string _statusText = string.Empty;
    private string _errorMessage = string.Empty;
    private int _progressIndex;
    private int _progressTotal;
    private string _currentBaseName = string.Empty;
    private bool _migrationCompleted;
    private ClusterMigrationResult? _result;

    private ICommand? _connectCommand;
    private ICommand? _migrateCommand;

    /// <param name="rac">Клиент rac (кластеры назначения, «infobase create/drop»).</param>
    /// <param name="sourceParams">Подключение к серверу-источнику (для удаления баз из старого кластера).</param>
    /// <param name="sourceClusterId">Идентификатор кластера-источника (откуда переносятся базы).</param>
    /// <param name="rows">Отмеченные строки импорта с исходными записями rac (<see cref="ClusterImportRow.Source"/>); строки без источника отбрасываются.</param>
    /// <param name="availableServers">Справочник «Серверы 1С» — сервер-источник исключается по Id.</param>
    /// <param name="sourceServer">Запись справочника сервера-источника (для исключения из списка назначения); null — список целиком.</param>
    /// <param name="dispatchToUi">Доставка применения результатов в UI-поток; null — сразу (тесты).</param>
    public ClusterMigrationViewModel(
        IRacClient rac,
        RacConnectionParams sourceParams,
        Guid sourceClusterId,
        IReadOnlyList<ClusterImportRow> rows,
        IReadOnlyList<ManagementServer> availableServers,
        ManagementServer? sourceServer,
        Action<Action>? dispatchToUi = null)
    {
        _rac = rac ?? throw new ArgumentNullException(nameof(rac));
        _sourceParams = sourceParams ?? new RacConnectionParams();
        _sourceClusterId = sourceClusterId;
        _rows = rows?.Where(r => r?.Source is not null).ToList() ?? new List<ClusterImportRow>();
        _dispatchToUi = dispatchToUi;
        AvailableServers = ClusterMigrationService.ExcludeSourceServer(availableServers, sourceServer);
        try
        {
            _logger = AppServices.GetRequiredService<IAppLogger>();
        }
        catch
        {
            _logger = null; // тестовый контекст без DI
        }
    }

    // ===================== Справочник «Серверы 1С» (цель) =====================

    /// <summary>
    /// Записи справочника «Серверы 1С» для выбора сервера назначения: сервер-источник
    /// исключён по Id (issue #41 — «желательно НЕ показывать сервер формы импорта»).
    /// </summary>
    public IReadOnlyList<ManagementServer> AvailableServers
    {
        get => _availableServers;
        private set
        {
            if (SetProperty(ref _availableServers, value ?? Array.Empty<ManagementServer>()))
                OnPropertyChanged(nameof(HasAvailableServers));
        }
    }

    /// <summary>Есть ли сервер назначения в списке (иначе «Перенести» недоступно).</summary>
    public bool HasAvailableServers => AvailableServers.Count > 0;

    /// <summary>
    /// Выбранный сервер назначения. При установке значения запускается загрузка его
    /// кластеров («cluster list») — повторное использование состояния сбрасывается.
    /// </summary>
    public ManagementServer? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (ReferenceEquals(_selectedServer, value))
                return;
            _selectedServer = value;
            OnPropertyChanged(nameof(SelectedServer));
            if (value is not null)
                StartConnect();
        }
    }

    // ===================== Кластеры назначения =====================

    /// <summary>Кластеры выбранного сервера назначения (модели rac «cluster list»).</summary>
    public IReadOnlyList<RacCluster> Clusters
    {
        get => _clusters;
        private set
        {
            if (SetProperty(ref _clusters, value ?? Array.Empty<RacCluster>()))
                OnPropertyChanged(nameof(Clusters));
        }
    }

    /// <summary>
    /// Выбранный кластер назначения. Единственный кластер сервера выбирается
    /// автоматически после загрузки (как в окне импорта, issue #25).
    /// </summary>
    public Guid? SelectedClusterId
    {
        get => _selectedClusterId;
        set => SetProperty(ref _selectedClusterId, value);
    }

    // ===================== Учётные данные СУБД =====================

    /// <summary>Пользователь СУБД для регистрации баз в новом кластере (rac «--db-user»).</summary>
    public string DbUser
    {
        get => _dbUser;
        set => SetProperty(ref _dbUser, value ?? string.Empty);
    }

    /// <summary>
    /// Пароль СУБД (rac «--db-pwd»). rac НЕ возвращает пароль СУБД — его вводит
    /// пользователь для подключения нового кластера к уже существующей БД.
    /// Живёт только в памяти окна; в журнале маскируется RacClient.
    /// </summary>
    public string DbPassword
    {
        get => _dbPassword;
        set => SetProperty(ref _dbPassword, value ?? string.Empty);
    }

    /// <summary>
    /// «Не удалять в старом» (issue #41): true — база копируется в новый кластер
    /// БЕЗ удаления из старого; false — после успешного добавления база удаляется
    /// из старого кластера командой rac «infobase drop» БЕЗ --drop-database (файл/
    /// БД на сервере СУБД остаётся).
    /// </summary>
    public bool KeepInSource
    {
        get => _keepInSource;
        set => SetProperty(ref _keepInSource, value);
    }

    // ===================== Сводка и прогресс =====================

    /// <summary>Число баз к переносу (напоминание в диалоге).</summary>
    public int BaseCount => _rows.Count;

    /// <summary>Текущая обрабатываемая база («какая база обрабатывается», issue #41).</summary>
    public string CurrentBaseName
    {
        get => _currentBaseName;
        private set => SetProperty(ref _currentBaseName, value);
    }

    /// <summary>Номер обрабатываемой базы (счётчик «1 из 10», 1-based).</summary>
    public int ProgressIndex
    {
        get => _progressIndex;
        private set
        {
            if (SetProperty(ref _progressIndex, value))
                OnPropertyChanged(nameof(ProgressText));
        }
    }

    /// <summary>Общее число баз к переносу.</summary>
    public int ProgressTotal
    {
        get => _progressTotal;
        private set
        {
            if (SetProperty(ref _progressTotal, value))
                OnPropertyChanged(nameof(ProgressText));
        }
    }

    /// <summary>Строка прогресса «N из M: база» (issue #41, фоном в диалоге).</summary>
    public string ProgressText =>
        ProgressTotal <= 0
            ? string.Empty
            : string.Format(
                LocalizationManager.T("ClusterMigration.Progress.Format"), ProgressIndex, ProgressTotal, CurrentBaseName);

    // ===================== Результат =====================

    /// <summary>Флаг успешного завершения переноса (окно спрашивает «заменить строку подключения?»).</summary>
    public bool MigrationCompleted
    {
        get => _migrationCompleted;
        private set => SetProperty(ref _migrationCompleted, value);
    }

    /// <summary>Результат миграции (валиден при <see cref="MigrationCompleted"/>).</summary>
    public ClusterMigrationResult? Result
    {
        get => _result;
        private set => SetProperty(ref _result, value);
    }

    /// <summary>
    /// Выставляет ответ пользователя на вопрос «Хотите заменить строку подключения?»
    /// (окно показывает его после <see cref="MigrationCompleted"/>): true — MainViewModel
    /// заменит сервер/порт в отмеченных базах списка приложения.
    /// </summary>
    public void SetReplaceConnectionStrings(bool value)
    {
        if (Result is not null)
            Result = Result with { ReplaceConnectionStrings = value };
    }

    // ===================== Команды =====================

    /// <summary>Загрузить кластеры выбранного сервера назначения.</summary>
    public ICommand ConnectCommand =>
        _connectCommand ??= new RelayCommand(() => _ = ConnectAsync());

    /// <summary>«Перенести»: добавить базы в новый кластер и удалить из старого (по настройке).</summary>
    public ICommand MigrateCommand =>
        _migrateCommand ??= new RelayCommand(() => _ = MigrateAsync());

    // ===================== Статус =====================

    /// <summary>Строка состояния («Загрузка кластеров…», прогресс, итог).</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Подробное сообщение последней ошибки (текст rac/stderr как есть).</summary>
    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>Выполняется ли сейчас запрос (флаг занятости исключает наложение).</summary>
    public bool IsBusy => _busy == 1;

    // ===================== Действия =====================

    /// <summary>
    /// Загружает кластеры выбранного сервера назначения («cluster list», фазовый паттерн
    /// окна импорта, issue #25): результат применяется в UI-потоке единым блоком,
    /// автовыбор единственного кластера выполняется там же. Ошибки не роняют окно —
    /// пишутся в <see cref="ErrorMessage"/>/<see cref="StatusText"/>.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (!TryEnterBusy())
            return;

        IReadOnlyList<RacCluster> clusters;
        try
        {
            ErrorMessage = string.Empty;
            StatusText = LocalizationManager.T("ClusterMigration.Status.LoadingClusters");
            if (SelectedServer is not { } server)
                return;

            clusters = await _rac.GetClustersAsync(BuildTargetParams(server)).ConfigureAwait(false);
        }
        catch (RacClientException ex)
        {
            ErrorMessage = ex.Message;
            StatusText = LocalizationManager.T("ClusterMigration.Status.ConnectFailed");
            return;
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationManager.T("ClusterMigration.Status.Cancelled");
            return;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusText = LocalizationManager.T("ClusterMigration.Status.ConnectFailed");
            _logger?.Error("Ошибка загрузки кластеров сервера назначения (миграция баз)", ex);
            return;
        }
        finally
        {
            ExitBusy();
        }

        if (_dispatchToUi is null)
            ApplyClusters(clusters);
        else
        {
            try
            {
                _dispatchToUi(() => ApplyClusters(clusters));
            }
            catch
            {
                ApplyClusters(clusters);
            }
        }
    }

    /// <summary>
    /// Применяет список кластеров назначения в UI-потоке единым блоком: наполнение
    /// <see cref="Clusters"/>, сброс выбора кластера и автовыбор единственного — так же,
    /// как <see cref="ClusterImportViewModel.CompleteConnect"/> (issue #25).
    /// </summary>
    private void ApplyClusters(IReadOnlyList<RacCluster> clusters)
    {
        Clusters = clusters;
        SelectedClusterId = null;
        StatusText = clusters.Count == 0
            ? LocalizationManager.T("ClusterMigration.Status.NoClusters")
            : string.Format(LocalizationManager.T("ClusterMigration.Status.LoadedClustersFormat"), clusters.Count);
        if (clusters.Count == 1)
            SelectedClusterId = clusters[0].Id;
    }

    /// <summary>
    /// «Перенести» (issue #41): для КАЖДОЙ отмеченной базы — добавить базу с теми же
    /// данными в новый кластер (rac «infobase create», данные источника + учётные данные
    /// СУБД из диалога), затем, если <see cref="KeepInSource"/> НЕ отмечен — удалить базу
    /// из старого кластера БЕЗ удаления БД на сервере СУБД (rac «infobase drop» без
    /// --drop-database). Фоновый прогресс: <see cref="ProgressIndex"/> из
    /// <see cref="ProgressTotal"/>, <see cref="CurrentBaseName"/>. При успехе формирует
    /// <see cref="Result"/> (новый сервер/порт — из «cluster info» кластера назначения)
    /// и устанавливает <see cref="MigrationCompleted"/>. Первая ошибка останавливает
    /// перенос и показывается в <see cref="ErrorMessage"/> (настройки настоятельно
    /// рекомендуют переносить базы по одной — частичный перенос корректен).
    /// </summary>
    public async Task MigrateAsync()
    {
        if (!TryEnterBusy())
            return;

        try
        {
            ErrorMessage = string.Empty;

            if (_rows.Count == 0)
            {
                ErrorMessage = LocalizationManager.T("ClusterMigration.Error.NoBases");
                return;
            }

            if (SelectedServer is not { } targetServer)
            {
                ErrorMessage = LocalizationManager.T("ClusterMigration.Error.NoTargetServer");
                return;
            }

            if (SelectedClusterId is not Guid targetClusterId)
            {
                ErrorMessage = LocalizationManager.T("ClusterMigration.Error.NoTargetCluster");
                return;
            }

            var targetCluster = Clusters.FirstOrDefault(c => c.Id == targetClusterId);
            if (targetCluster is null)
            {
                ErrorMessage = LocalizationManager.T("ClusterMigration.Error.NoTargetCluster");
                return;
            }

            var targetParams = BuildTargetParams(targetServer);

            // Хост строки подключения после миграции: «cluster info» кластера назначения
            // (корректен для удалённого RAS) → host-часть адреса сервера назначения.
            RacClusterInfo? info = null;
            try
            {
                info = await _rac.GetClusterInfoAsync(targetParams, targetClusterId).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                info = null; // сбой «cluster info» не блокирует перенос (fallback — адрес сервера)
            }

            var newServer = ClusterMigrationService.ResolveClusterHost(targetServer.Address, info?.HostName);
            var newPort = targetCluster.Port > 0 ? targetCluster.Port : IRacClient.DefaultPort;

            ProgressTotal = _rows.Count;
            var migrated = new List<string>(_rows.Count);

            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                ProgressIndex = i + 1;
                CurrentBaseName = row.Name;

                // 1) Добавляем базу с теми же данными в новый кластер (rac «infobase create»).
                var request = ClusterMigrationService.BuildCreateRequest(row.Source!, DbUser, DbPassword);
                var created = await _rac.CreateInfobaseAsync(targetParams, targetClusterId, request).ConfigureAwait(false);
                if (!created)
                {
                    ErrorMessage = string.Format(
                        LocalizationManager.T("ClusterMigration.Error.CreateFailedFormat"), row.Name, _rac.LastActionError);
                    return;
                }

                // 2) Если «Не удалять в старом» не отмечено — удаляем базу из старого
                //    кластера БЕЗ удаления БД на сервере СУБД (--drop-database не передаётся).
                if (!KeepInSource)
                {
                    var dropped = await _rac.DropInfobaseAsync(
                        _sourceParams, _sourceClusterId, row.Source!.InfobaseId, dropDatabase: false).ConfigureAwait(false);
                    if (!dropped)
                    {
                        ErrorMessage = string.Format(
                            LocalizationManager.T("ClusterMigration.Error.DropFailedFormat"), row.Name, _rac.LastActionError);
                        return;
                    }
                }

                migrated.Add(row.Name);
            }

            Result = new ClusterMigrationResult(migrated, newServer, newPort, ReplaceConnectionStrings: false);
            StatusText = string.Format(LocalizationManager.T("ClusterMigration.SuccessFormat"), migrated.Count);
            MigrationCompleted = true;
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationManager.T("ClusterMigration.Status.Cancelled");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusText = LocalizationManager.T("ClusterMigration.Status.Failed");
            _logger?.Error("Ошибка миграции баз между кластерами 1С", ex);
        }
        finally
        {
            ExitBusy();
        }
    }

    // ===================== Внутреннее =====================

    /// <summary>Запуск загрузки кластеров без ожидания (fire-and-forget, как в окне импорта).</summary>
    private void StartConnect() => _ = ConnectAsync();

    /// <summary>Параметры подключения к серверу назначения из записи справочника
    /// (адрес «host[:port]» с приоритетом порта из адреса, issue #324).</summary>
    private static RacConnectionParams BuildTargetParams(ManagementServer server)
    {
        var (host, port) = RacConnectionAddress.Split(server.Address, server.Port);
        return new RacConnectionParams
        {
            Address = host,
            Port = port,
            User = server.User?.Trim() ?? string.Empty,
            Password = server.Password ?? string.Empty
        };
    }

    private bool TryEnterBusy()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return false;
        NotifyIsBusy();
        return true;
    }

    private void ExitBusy()
    {
        Interlocked.Exchange(ref _busy, 0);
        NotifyIsBusy();
    }

    /// <summary>Доставляет уведомление IsBusy в UI-поток (паттерн окна импорта, issue #25).</summary>
    private void NotifyIsBusy()
    {
        if (_dispatchToUi is null)
        {
            OnPropertyChanged(nameof(IsBusy));
            return;
        }
        _dispatchToUi(() => OnPropertyChanged(nameof(IsBusy)));
    }
}
