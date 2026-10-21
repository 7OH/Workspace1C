using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты вью-модели диалога импорта баз из кластера 1С (этап 2, 0.3.9.173):
/// подключение к ragent/RAS через fake-клиент rac (кластеры), автовыбор первого кластера,
/// загрузка баз и чеклист с пометкой дубликатов/файловых баз, сводка перед подтверждением,
/// команда «Импортировать» (только отмеченные новые), флаг занятости и обработка ошибок.
/// </summary>
public sealed class ClusterImportViewModelTests
{
    [Fact]
    public void Defaults_AddressLocalhost_Port1540_CommandsExist()
    {
        var vm = new ClusterImportViewModel(new FakeRacClient(), Array.Empty<Infobase>());

        Assert.Equal("localhost", vm.ServerAddress);
        Assert.Equal(IRacClient.DefaultPort, vm.ServerPort);
        Assert.Equal(1540, vm.ServerPort);
        Assert.Equal(string.Empty, vm.UserName);
        Assert.Equal(string.Empty, vm.Password);
        Assert.True(vm.UseClusterGrouping);
        Assert.False(vm.IsBusy);
        Assert.False(vm.ImportCompleted);
        Assert.Empty(vm.Rows);
        Assert.Empty(vm.SelectedBases);
        Assert.Equal(0, vm.ReadyToImportCount);
        Assert.Equal(0, vm.DuplicateCount);
        Assert.NotNull(vm.ConnectCommand);
        Assert.NotNull(vm.LoadBasesCommand);
        Assert.NotNull(vm.ImportCommand);
        Assert.NotNull(vm.SelectAllCommand);
        Assert.NotNull(vm.SelectNoneCommand);
    }

    [Fact]
    public async Task Connect_Success_FillsClusters_NoAutoSelectWhenMultiple_Issue25()
    {
        // issue #25: при НЕСКОЛЬКИХ кластерах автовыбор не выполняется — пользователь
        // выбирает кластер сам, чтобы не запускать спонтанную загрузку баз не того
        // кластера (и не оставлять ошибку после успешного подключения).
        var client = new FakeRacClient();
        var vm = new ClusterImportViewModel(client, Array.Empty<Infobase>());

        await vm.ConnectAsync();

        Assert.Equal(3, vm.Clusters.Count);
        Assert.Null(vm.SelectedClusterId);
        Assert.Equal(1, client.ClustersCalls);
        Assert.Empty(client.InfobaseClusterIds);
        Assert.Empty(vm.Rows);
        Assert.Empty(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Connect_SingleCluster_AutoSelectsIt_AndLoadsBases_Issue25()
    {
        // issue #25: при ЕДИНСТВЕННОМ кластере он выбирается автоматически (как в окне
        // «Серверы 1С») — загрузка баз запускается сразу после подключения.
        var client = new FakeRacClient(singleCluster: true);
        var vm = new ClusterImportViewModel(client, Array.Empty<Infobase>());

        await vm.ConnectAsync();

        Assert.Equal(1, vm.Clusters.Count);
        Assert.Equal(FakeRacClient.FirstClusterId, vm.SelectedClusterId);
        Assert.Equal(1, client.ClustersCalls);
        Assert.Equal(FakeRacClient.FirstClusterId, client.InfobaseClusterIds.Single());
        Assert.Equal(3, vm.Rows.Count);
        Assert.Empty(vm.ErrorMessage);
        Assert.False(vm.IsBusy);

        // Поля строки чеклиста: подпись (описание/СУБД/сервер/имя БД), кластер, строка подключения.
        var buh = vm.Rows.Single(r => r.Name == "Бухгалтерия");
        Assert.Equal("Основная база • MSSQLServer • sql-01 • buho", buh.Subtitle);
        Assert.Equal("Главный кластер", buh.ClusterName);
        Assert.Equal("Srvr=\"srv1\";Ref=\"Бухгалтерия\"", buh.ConnectionString);
        Assert.False(buh.IsDuplicate);
        Assert.Null(buh.SkipReason);
        Assert.True(buh.IsChecked);
        Assert.NotNull(buh.Tag);
    }

    [Fact]
    public async Task Connect_Success_WhenRacRejectsEqualsClusterFormat_Issue25()
    {
        // Регрессионный сценарий issue #25: rac 8.5.x отклоняет «--cluster=<uuid>»
        // (код -1). Исправленный RacClient перебирает форматы внутри GetInfobasesAsync
        // (первая попытка отклонена, вторая успешна) — ViewModel видит успех:
        // подключение и загрузка чеклиста баз проходят без повторного нажатия.
        // Единственный кластер выбирается автоматически (issue #25).
        var client = new FakeRacClient(rejectEqualsClusterFormat: true, singleCluster: true);
        var vm = new ClusterImportViewModel(client, Array.Empty<Infobase>());

        await vm.ConnectAsync();

        Assert.Equal(1, vm.Clusters.Count);
        Assert.Equal(FakeRacClient.FirstClusterId, vm.SelectedClusterId);
        Assert.Equal(3, vm.Rows.Count);
        Assert.Empty(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        // Клиент действительно «повторил» запрос с альтернативным форматом (как
        // исправленный RacClient на платформе 8.5.1).
        Assert.Equal(1, client.InfobaseRetryCount);
    }

    [Fact]
    public async Task Connect_Failure_ReportsError_AndWindowStaysUsable()
    {
        var client = new FakeRacClient(throwOnClusters: true);
        var vm = new ClusterImportViewModel(client, Array.Empty<Infobase>());

        await vm.ConnectAsync();

        Assert.NotEmpty(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        Assert.Empty(vm.Clusters);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task Connect_EmptyAddress_ValidatesWithoutRac()
    {
        var client = new FakeRacClient();
        var vm = new ClusterImportViewModel(client, Array.Empty<Infobase>());
        vm.ServerAddress = "   ";

        await vm.ConnectAsync();

        Assert.Equal(0, client.ClustersCalls);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task Connect_HostPortInAddress_TakesPriorityOverPortField()
    {
        // Issue #324: пользователь указывает нестандартный порт прямо в адресе «host:port»
        // (как в командной строке rac.exe localhost:27545 cluster list), а поле «Порт»
        // остаётся дефолтным (1540). Без нормализации rac получил бы невалидный токен
        // «localhost:27545:1540» и подключение бы не установилось.
        var client = new FakeRacClient();
        var vm = new ClusterImportViewModel(client, Array.Empty<Infobase>());
        vm.ServerAddress = "localhost:27545";
        vm.ServerPort = 1540;

        await vm.ConnectAsync();

        Assert.NotNull(client.LastParams);
        Assert.Equal("localhost", client.LastParams.Address);
        Assert.Equal(27545, client.LastParams.Port);
        Assert.NotEmpty(vm.Clusters);
    }

    [Fact]
    public async Task ChangingCluster_ReloadsBases_AndClusterInfoIsCachedPerCluster()
    {
        var client = new FakeRacClient();
        var vm = new ClusterImportViewModel(client, Array.Empty<Infobase>());
        await vm.ConnectAsync();

        // Несколько кластеров — автовыбор не выполняется (issue #25); выбираем первый вручную.
        Assert.Null(vm.SelectedClusterId);
        await vm.LoadBasesAsync(FakeRacClient.FirstClusterId);

        Assert.Equal(FakeRacClient.FirstClusterId, client.InfobaseClusterIds.Single());
        Assert.Equal(1, client.ClusterInfoCalls);

        vm.SelectedClusterId = FakeRacClient.SecondClusterId;
        // Дожидаемся завершения фоновой загрузки (таймаут-цикл вместо сна).
        var deadline = System.DateTime.UtcNow.AddSeconds(5);
        while (client.InfobaseCalls < 2 && System.DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.Equal(2, client.InfobaseCalls);
        Assert.Equal(FakeRacClient.SecondClusterId, client.InfobaseClusterIds[^1]);
        Assert.All(vm.Rows, r => Assert.Equal("Второй кластер", r.ClusterName));
        Assert.Equal("srv2", vm.Rows[0].Tag.Connection.Server);
        Assert.Equal(2541, vm.Rows[0].Tag.Connection.Port); // порт КЛАСТЕРА, не ragent/RAS

        // Повторная загрузка первого кластера: cluster info берётся из кэша,
        // GetClusterInfoAsync повторно не вызывается.
        await vm.LoadBasesAsync(FakeRacClient.FirstClusterId);
        Assert.Equal(2, client.ClusterInfoCalls);
        Assert.Equal(3, client.InfobaseCalls);
    }

    [Fact]
    public async Task Duplicate_IsMarked_NotChecked_AndNotImportedEvenWhenChecked()
    {
        var existing = RacInfobaseMapper.ToInfobase(
            new RacInfobaseSummary { Name = "Бухгалтерия", Dbms = "MSSQLServer" },
            "srv1", 1541, null, "Группа");
        var client = new FakeRacClient(singleCluster: true);
        var vm = new ClusterImportViewModel(client, new[] { existing });

        await vm.ConnectAsync();

        var dup = vm.Rows.Single(r => r.Name == "Бухгалтерия");
        Assert.True(dup.IsDuplicate);
        Assert.False(dup.IsChecked);
        Assert.Equal(LocalizationManager.T("ClusterImport.AlreadyExists"), dup.SkipReason);

        // Принудительный чек пользователем не приводит к импорту — фильтр на этапе подтверждения.
        dup.IsChecked = true;
        vm.Import();

        Assert.DoesNotContain(vm.SelectedBases, b => b.Name == "Бухгалтерия");
    }

    [Fact]
    public async Task EmptyDbms_IsImportable_NotSkipped()
    {
        // Файловых баз в кластере не бывает (комментарий 7OH от 2026-10-18 20:24):
        // пустой «dbms» не является причиной пропуска — база импортируется как
        // клиент-серверная (строка подключения строится из сервера/имени).
        var vm = new ClusterImportViewModel(new FakeRacClient(singleCluster: true), Array.Empty<Infobase>());
        await vm.ConnectAsync();

        var row = vm.Rows.Single(r => r.Name == "Кадры");
        Assert.False(row.IsDuplicate);
        Assert.True(row.IsChecked);
        Assert.Null(row.SkipReason);

        vm.Import();

        Assert.Contains(vm.SelectedBases, b => b.Name == "Кадры");
    }

    [Fact]
    public async Task Summary_CountsReadyToImport_AndDuplicates()
    {
        var existing = RacInfobaseMapper.ToInfobase(
            new RacInfobaseSummary { Name = "Бухгалтерия", Dbms = "MSSQLServer" },
            "srv1", 1541, null, "Группа");
        var vm = new ClusterImportViewModel(new FakeRacClient(singleCluster: true), new[] { existing });
        await vm.ConnectAsync();

        // Первый кластер: Бухгалтерия (дубликат), Управление персоналом и Кадры (новые).
        Assert.Equal(2, vm.ReadyToImportCount);
        Assert.Equal(1, vm.DuplicateCount);

        vm.SelectNone();
        Assert.Equal(0, vm.ReadyToImportCount);

        vm.SelectAll();
        Assert.Equal(2, vm.ReadyToImportCount);
        Assert.Equal(1, vm.DuplicateCount);

        // Ручное снятие отметок новых строк пересчитывает сводку.
        foreach (var row in vm.Rows.Where(r => !r.IsDuplicate))
            row.IsChecked = false;
        Assert.Equal(0, vm.ReadyToImportCount);
    }

    [Fact]
    public async Task Import_ReturnsOnlyCheckedNewBases_WithMappedFields()
    {
        var client = new FakeRacClient(singleCluster: true);
        var vm = new ClusterImportViewModel(client, Array.Empty<Infobase>());
        await vm.ConnectAsync();

        // Отмечаем только одну из новых баз — остальные строки сняты.
        vm.SelectNone();
        vm.Rows.Single(r => r.Name == "Управление персоналом").IsChecked = true;
        vm.Import();

        Assert.True(vm.ImportCompleted);
        var ib = Assert.Single(vm.SelectedBases);
        Assert.Equal("Управление персоналом", ib.Name);
        Assert.Equal("Главный кластер", ib.Group); // UseClusterGrouping=true по умолчанию
        Assert.Equal(ConnectionType.ClientServer, ib.Connection.Type);
        Assert.Equal("srv1", ib.Connection.Server);         // хост из cluster info (hostName)
        Assert.Equal(1541, ib.Connection.Port);             // порт кластера, не порт RAS
        Assert.Equal("Управление персоналом", ib.Connection.DatabaseName);
        Assert.Equal(AuthenticationMode.Prompt, ib.Connection.AuthenticationMode);
    }

    [Fact]
    public async Task IsBusy_BlocksReentrantConnect()
    {
        // Fake-клиент с задержкой: пока первый запрос выполняется, второй не должен
        // наложиться (флаг занятости через Interlocked пропускает его).
        var client = new FakeRacClient(clusterDelay: TimeSpan.FromMilliseconds(300));
        var vm = new ClusterImportViewModel(client, Array.Empty<Infobase>());

        var first = vm.ConnectAsync();
        // Первый вызов синхронно устанавливает флаг занятости до первого await.
        Assert.True(vm.IsBusy);

        var second = vm.ConnectAsync();
        // Второй вызов сразу возвращается: флаг занятости уже установлен.
        Assert.True(second.IsCompleted);
        await second;

        await first;

        Assert.False(vm.IsBusy);
        // Кластеры запрошены ровно один раз.
        Assert.Equal(1, client.ClustersCalls);
    }

    [Fact]
    public async Task IsBusy_BlocksReentrantLoadBases()
    {
        var client = new FakeRacClient(loadDelay: TimeSpan.FromMilliseconds(300), singleCluster: true);
        var vm = new ClusterImportViewModel(client, Array.Empty<Infobase>());
        await vm.ConnectAsync();

        // Автовыбор первого кластера запустил фоновую загрузку баз — она ещё идёт.
        Assert.True(vm.IsBusy);

        var second = vm.LoadBasesAsync(FakeRacClient.FirstClusterId);
        // Повторный вход отклонён флагом занятости.
        Assert.True(second.IsCompleted);
        await second;

        // Дожидаемся завершения фоновой загрузки (таймаут-цикл вместо сна).
        var deadline = System.DateTime.UtcNow.AddSeconds(5);
        while (vm.IsBusy && System.DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.False(vm.IsBusy);
        // Базы прочитаны ровно один раз.
        Assert.Equal(1, client.InfobaseCalls);
    }

    [Fact]
    public async Task EmptyCluster_NoRows_NoError()
    {
        var client = new FakeRacClient();
        var vm = new ClusterImportViewModel(client, Array.Empty<Infobase>());
        await vm.ConnectAsync();

        vm.SelectedClusterId = FakeRacClient.ThirdClusterId;

        Assert.Empty(vm.Rows);
        Assert.Empty(vm.ErrorMessage);
        Assert.Equal(0, vm.ReadyToImportCount);
        Assert.Equal(0, vm.DuplicateCount);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task LoadBases_Failure_ReportsError_NotBusy()
    {
        var client = new FakeRacClient(throwOnLoad: true, singleCluster: true);
        var vm = new ClusterImportViewModel(client, Array.Empty<Infobase>());

        await vm.ConnectAsync(); // автовыбор единственного → загрузка баз падает внутри LoadBasesAsync

        Assert.Equal(1, client.InfobaseCalls);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task LoadBases_Cancelled_SetsStatus_WithoutError()
    {
        var client = new FakeRacClient(throwCancel: true, singleCluster: true);
        var vm = new ClusterImportViewModel(client, Array.Empty<Infobase>());

        await vm.ConnectAsync();

        Assert.Empty(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        Assert.NotEmpty(vm.StatusText);
    }

    // ============= Issue #27: выбор сервера из справочника «Серверы 1С» =============

    [Fact]
    public void Ctor_WithServers_AvailableServersFilled_DefaultsKept_Issue27()
    {
        // Совместимость со старыми вызовами (без серверов) + префилл по умолчанию.
        var servers = new[] { BuildServer("srv-1"), BuildServer("srv-2") };
        var client = new FakeRacClient();

        var withServers = new ClusterImportViewModel(client, Array.Empty<Infobase>(), availableServers: servers);
        Assert.Equal(2, withServers.AvailableServers.Count);
        Assert.True(withServers.HasAvailableServers);
        Assert.Null(withServers.SelectedServer);
        // Пока сервер не выбран — дефолты localhost:1540 сохраняются (регресс).
        Assert.Equal("localhost", withServers.ServerAddress);
        Assert.Equal(IRacClient.DefaultPort, withServers.ServerPort);
        Assert.Equal(string.Empty, withServers.UserName);
        Assert.Equal(string.Empty, withServers.Password);

        // Старый вызов без серверов работает как раньше.
        var withoutServers = new ClusterImportViewModel(client, Array.Empty<Infobase>());
        Assert.Empty(withoutServers.AvailableServers);
        Assert.False(withoutServers.HasAvailableServers);
        Assert.Equal("localhost", withoutServers.ServerAddress);
        Assert.Equal(IRacClient.DefaultPort, withoutServers.ServerPort);
    }

    [Fact]
    public void SelectingServer_FillsAddressPortUserPassword_Issue27()
    {
        var vm = new ClusterImportViewModel(new FakeRacClient(), Array.Empty<Infobase>());
        var server = BuildServer("srv-1");

        vm.SelectedServer = server;

        Assert.Same(server, vm.SelectedServer);
        Assert.Equal("srv-prod", vm.ServerAddress);
        Assert.Equal(1545, vm.ServerPort);
        Assert.Equal("admin", vm.UserName);
        Assert.Equal("secret", vm.Password);
    }

    [Fact]
    public async Task InitializeFromServer_AutoConnectTrue_CallsGetClustersWithServerParams_Issue27()
    {
        var client = new FakeRacClient();
        var vm = new ClusterImportViewModel(client, Array.Empty<Infobase>());
        var server = BuildServer("srv-1");

        await vm.InitializeFromServer(server, autoConnect: true);

        // Подключение выполнено с параметрами выбранного сервера (а не дефолтов).
        Assert.Equal(1, client.ClustersCalls);
        Assert.NotNull(client.LastParams);
        Assert.Equal("srv-prod", client.LastParams.Address);
        Assert.Equal(1545, client.LastParams.Port);
        Assert.Equal("admin", client.LastParams.User);
        Assert.Equal("secret", client.LastParams.Password);
        Assert.Equal(3, vm.Clusters.Count);
        // Несколько кластеров — автовыбор не выполняется (issue #25), пользователь выбирает сам.
        Assert.Null(vm.SelectedClusterId);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task InitializeFromServer_AutoConnectFalse_NoRacCalls_Issue27()
    {
        var client = new FakeRacClient();
        var vm = new ClusterImportViewModel(client, Array.Empty<Infobase>());
        var server = BuildServer("srv-1");

        await vm.InitializeFromServer(server, autoConnect: false);

        // Без авто-подключения rac не трогается — только префилл параметров.
        Assert.Equal(0, client.ClustersCalls);
        Assert.Empty(vm.Clusters);
        Assert.Equal("srv-prod", vm.ServerAddress);
        Assert.Equal(1545, vm.ServerPort);
        Assert.Equal("admin", vm.UserName);
        Assert.Equal("secret", vm.Password);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void InitializeFromServer_SelectsMatchingServerFromListById_Issue27()
    {
        // Сервер из окна «Серверы 1С» может отличаться по ссылке от записей списка —
        // VM подставляет запись с тем же Id (чтобы комбобокс показал выбранную строку).
        var listed = BuildServer("srv-1");
        var vm = new ClusterImportViewModel(
            new FakeRacClient(), Array.Empty<Infobase>(),
            availableServers: new[] { listed });
        var external = BuildServer("srv-1"); // тот же Id, другой экземпляр

        _ = vm.InitializeFromServer(external, autoConnect: false);

        Assert.Same(listed, vm.SelectedServer);
        Assert.Equal("srv-prod", vm.ServerAddress);
    }

    // ============= Issue #25: приоритет автовыбора кластера и опциональный «cluster info» =============

    [Fact]
    public async Task InitializeFromServer_WithRequestedClusterId_SelectsThatCluster_Issue25()
    {
        // Из окна «Серверы 1С» передаётся выбранный кластер: при нескольких кластерах
        // выбирается именно он (не первый, не единственный), и загружаются ЕГО базы.
        var client = new FakeRacClient(); // три кластера
        var vm = new ClusterImportViewModel(client, Array.Empty<Infobase>());
        var server = BuildServer("srv-1");

        await vm.InitializeFromServer(server, autoConnect: true, clusterId: FakeRacClient.SecondClusterId);

        Assert.Equal(3, vm.Clusters.Count);
        Assert.Equal(FakeRacClient.SecondClusterId, vm.SelectedClusterId);
        Assert.Equal(2, vm.Rows.Count); // базы «Второго кластера»
        Assert.Empty(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task InitializeFromServer_SeveralClustersNoRequested_NoBusy_SelectClusterHint_Issue25()
    {
        // Несколько кластеров и без переданного: автовыбор не выполняется, но прогресс
        // завершён, ошибок нет, и пользователю подсказывается выбор кластера (регрессия
        // «крутящегося прогресса» из жалобы после 1.0.0.31 при открытии из «Серверы 1С»).
        var client = new FakeRacClient();
        var vm = new ClusterImportViewModel(client, Array.Empty<Infobase>());
        var server = BuildServer("srv-1");

        await vm.InitializeFromServer(server, autoConnect: true);

        Assert.Equal(3, vm.Clusters.Count);
        Assert.Null(vm.SelectedClusterId);
        Assert.False(vm.IsBusy);
        Assert.Empty(vm.ErrorMessage);
        // Статус не пуст (подсказка о выборе кластера либо сам ключ локализации в тестовом
        // окружении без инициализации словарей) — главное: прогресс завершён и ошибок нет.
        Assert.NotEmpty(vm.StatusText);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task LoadBases_ClusterInfoFails_StillLoadsBases_NoError_Issue25()
    {
        // «cluster info» нужен только для подписи хоста в строке подключения: его сбой
        // (все три формата отклонены rac 8.5.x) не должен блокировать загрузку баз и
        // оставлять ошибку в окне (жалоба «ошибка на месте после подключения»).
        var client = new FakeRacClient(throwOnClusterInfo: true, singleCluster: true);
        var vm = new ClusterImportViewModel(client, Array.Empty<Infobase>());

        await vm.ConnectAsync(); // единственный кластер → автовыбор → LoadBasesAsync

        Assert.Equal(1, client.ClusterInfoCalls);
        Assert.Equal(1, client.InfobaseCalls);
        Assert.Equal(3, vm.Rows.Count);
        Assert.Empty(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    /// <summary>Запись справочника «Серверы 1С» для тестов issue #27.</summary>
    private static ManagementServer BuildServer(string id) => new()
    {
        Id = id,
        Description = "Продуктивный сервер",
        Address = "srv-prod",
        Port = 1545,
        User = "admin",
        Password = "secret",
    };

    // ===================== Fakes =====================

    /// <summary>
    /// Fake-клиент rac для тестов: три кластера (первый — 3 базы, вторая — 2, третий — пустой),
    /// «cluster info» с именем хоста для каждой базы; подсчёт вызовов и записи clusterId.
    /// </summary>
    private sealed class FakeRacClient : IRacClient
    {
        public static readonly System.Guid FirstClusterId = System.Guid.Parse("11111111-1111-1111-1111-111111111111");
        public static readonly System.Guid SecondClusterId = System.Guid.Parse("22222222-2222-2222-2222-222222222222");
        public static readonly System.Guid ThirdClusterId = System.Guid.Parse("33333333-3333-3333-3333-333333333333");

        private readonly bool _throwOnClusters;
        private readonly bool _throwOnLoad;
        private readonly bool _throwCancel;
        private readonly bool _rejectEqualsClusterFormat;
        private readonly bool _singleCluster;
        private readonly bool _throwOnClusterInfo;
        private readonly TimeSpan _clusterDelay;
        private readonly TimeSpan _loadDelay;

        public FakeRacClient(
            bool throwOnClusters = false, bool throwOnLoad = false, bool throwCancel = false,
            TimeSpan? clusterDelay = null, TimeSpan? loadDelay = null,
            bool rejectEqualsClusterFormat = false, bool singleCluster = false,
            bool throwOnClusterInfo = false)
        {
            _throwOnClusters = throwOnClusters;
            _throwOnLoad = throwOnLoad;
            _throwCancel = throwCancel;
            _rejectEqualsClusterFormat = rejectEqualsClusterFormat;
            _singleCluster = singleCluster;
            _throwOnClusterInfo = throwOnClusterInfo;
            _clusterDelay = clusterDelay ?? TimeSpan.Zero;
            _loadDelay = loadDelay ?? TimeSpan.Zero;
        }

        /// <summary>Текст последней ошибки действия (как в реальном клиенте).</summary>
        public string LastActionError { get; } = string.Empty;

        /// <summary>«cluster update» (issue #324, C3) тестами окна импорта не используется.</summary>
        public Task<bool> UpdateClusterAsync(
            RacConnectionParams parameters, Guid clusterId, RacClusterUpdate changes,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        /// <summary>Сколько раз запрошен список кластеров.</summary>
        public int ClustersCalls { get; private set; }

        /// <summary>Параметры последнего вызова «cluster list» (проверка нормализации host:port).</summary>
        public RacConnectionParams? LastParams { get; private set; }

        /// <summary>Сколько раз запрошена информация о кластере («cluster info»).</summary>
        public int ClusterInfoCalls { get; private set; }

        /// <summary>Сколько раз запрошены информационные базы кластера.</summary>
        public int InfobaseCalls { get; private set; }

        /// <summary>clusterId, переданные в «infobase summary list» (порядок вызовов).</summary>
        public List<System.Guid> InfobaseClusterIds { get; } = new();

        public async Task<IReadOnlyList<RacCluster>> GetClustersAsync(
            RacConnectionParams parameters, CancellationToken cancellationToken = default)
        {
            ClustersCalls++;
            LastParams = parameters;
            if (_clusterDelay > TimeSpan.Zero)
                await Task.Delay(_clusterDelay, cancellationToken).ConfigureAwait(false);
            if (_throwOnClusters)
                throw new RacClientException("rac not found");
            if (_singleCluster)
            {
                return new[]
                {
                    new RacCluster { Id = FirstClusterId, Name = "Главный кластер", Port = 1541 }
                };
            }
            return new[]
            {
                new RacCluster { Id = FirstClusterId, Name = "Главный кластер", Port = 1541 },
                new RacCluster { Id = SecondClusterId, Name = "Второй кластер", Port = 2541 },
                new RacCluster { Id = ThirdClusterId, Name = "Пустой кластер", Port = 1541 }
            };
        }

        public Task<RacClusterInfo?> GetClusterInfoAsync(
            RacConnectionParams parameters, System.Guid clusterId, CancellationToken cancellationToken = default)
        {
            ClusterInfoCalls++;
            if (_throwOnClusterInfo)
                throw new RacClientException("cluster info: rac отклонил все известные форматы (имитация 8.5.x)");
            var host = clusterId == FirstClusterId ? "srv1" : clusterId == SecondClusterId ? "srv2" : "srv3";
            return Task.FromResult<RacClusterInfo?>(new RacClusterInfo
            {
                Name = host == "srv1" ? "Главный кластер" : host == "srv2" ? "Второй кластер" : "Пустой кластер",
                HostName = host,
                Port = clusterId == SecondClusterId ? 2541 : 1541
            });
        }

        /// <summary>Сколько раз клиент «повторил» запрос баз из-за отклонённого формата
        /// «--cluster=<uuid>» (имитация перебора форматов исправленного RacClient, issue #25).</summary>
        public int InfobaseRetryCount { get; private set; }

        public async Task<IReadOnlyList<RacInfobaseSummary>> GetInfobasesAsync(
            RacConnectionParams parameters, System.Guid clusterId, CancellationToken cancellationToken = default)
        {
            InfobaseCalls++;
            InfobaseClusterIds.Add(clusterId);
            if (_loadDelay > TimeSpan.Zero)
                await Task.Delay(_loadDelay, cancellationToken).ConfigureAwait(false);
            if (_throwOnLoad)
                throw new RacClientException("нет прав на просмотр баз");
            if (_throwCancel)
                throw new OperationCanceledException();

            // issue #25: rac 8.5.x отклоняет «--cluster=<uuid>» (код -1). Исправленный
            // RacClient перебирает форматы ВНУТРИ одного вызова GetInfobasesAsync:
            // первая попытка (формат 0) отклоняется rac, вторая (формат 1) возвращает
            // данные. Fake имитирует это — ViewModel видит успех без повторного
            // нажатия «Загрузить» (регрессионный сценарий issue #25).
            if (_rejectEqualsClusterFormat)
            {
                InfobaseRetryCount++;
                try
                {
                    throw new RacClientException(
                        "rac завершился с кодом -1: Ошибка разбора параметра: --cluster=…");
                }
                catch (RacClientException)
                {
                    // Вторая попытка (альтернативный формат) — успешна: продолжаем.
                }
            }

            if (clusterId == FirstClusterId)
            {
                return new[]
                {
                    new RacInfobaseSummary { Name = "Бухгалтерия", Descr = "Основная база", Dbms = "MSSQLServer", DbServer = "sql-01", DbName = "buho" },
                    new RacInfobaseSummary { Name = "Управление персоналом", Dbms = "PostgreSQL", DbServer = "pg-01", DbName = "hr" },
                    new RacInfobaseSummary { Name = "Кадры", Descr = "База без сведений о СУБД", Dbms = "" }
                };
            }

            if (clusterId == SecondClusterId)
            {
                return new[]
                {
                    new RacInfobaseSummary { Name = "Зарплата", Descr = "Расчёт заработной платы", Dbms = "PostgreSQL", DbServer = "pg-02", DbName = "zp" },
                    new RacInfobaseSummary { Name = "Документооборот", Dbms = "MSSQLServer", DbServer = "sql-02", DbName = "do" }
                };
            }

            return Array.Empty<RacInfobaseSummary>();
        }

        public Task<IReadOnlyList<RacProcessInfo>> GetProcessesAsync(
            RacConnectionParams parameters, System.Guid clusterId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacProcessInfo>>(Array.Empty<RacProcessInfo>());

        public Task<IReadOnlyList<RacSessionInfo>> GetSessionsAsync(
            RacConnectionParams parameters, System.Guid clusterId, System.Guid? infobaseId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacSessionInfo>>(Array.Empty<RacSessionInfo>());

        public Task<IReadOnlyList<RacConnectionInfo>> GetConnectionsAsync(
            RacConnectionParams parameters, System.Guid clusterId, System.Guid? infobaseId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacConnectionInfo>>(Array.Empty<RacConnectionInfo>());

        public Task<IReadOnlyList<RacLockInfo>> GetLocksAsync(
            RacConnectionParams parameters, System.Guid clusterId, System.Guid? infobaseId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacLockInfo>>(Array.Empty<RacLockInfo>());

        // Регламентные задания импорт баз не использует — пустой список.
        public Task<IReadOnlyList<RacJobInfo>> GetJobsAsync(
            RacConnectionParams parameters, System.Guid clusterId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacJobInfo>>(Array.Empty<RacJobInfo>());

        public Task<bool> SetJobStateAsync(
            RacConnectionParams parameters, System.Guid clusterId, System.Guid jobId,
            RacJobAction action, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> TerminateSessionAsync(
            RacConnectionParams parameters, System.Guid clusterId, System.Guid sessionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> DisconnectConnectionAsync(
            RacConnectionParams parameters, System.Guid clusterId, System.Guid connectionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        // Создание/удаление/изменение баз (issue #3) окно импорта не использует — успех.
        public Task<bool> CreateInfobaseAsync(
            RacConnectionParams parameters, System.Guid clusterId, RacInfobaseCreateRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> UpdateInfobaseAsync(
            RacConnectionParams parameters, System.Guid clusterId, System.Guid infobaseId,
            RacInfobaseUpdateRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> DropInfobaseAsync(
            RacConnectionParams parameters, System.Guid clusterId, System.Guid infobaseId,
            bool dropDatabase = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}