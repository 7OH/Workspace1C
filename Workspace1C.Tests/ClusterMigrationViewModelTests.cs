using System;
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
/// Тесты вью-модели диалога миграции баз между кластерами 1С (1.0.0.51, issue #41):
/// выбор сервера/кластера назначения (сервер-источник скрыт), загрузка кластеров
/// назначения с автовыбором единственного, перенос баз (сначала «infobase create»
/// в новом кластере, затем «infobase drop» без --drop-database из старого при снятой
/// галочке «Не удалять в старом»), прогресс «N из M: база», обработка ошибок create/drop
/// и формирование результата (перенесённые базы + новый сервер/порт).
/// </summary>
public sealed class ClusterMigrationViewModelTests
{
    [Fact]
    public void Ctor_ExcludesSourceServer_FromAvailableServers()
    {
        var source = BuildServer("src", "srv-old");
        var target = BuildServer("tgt", "srv-new");

        var vm = new ClusterMigrationViewModel(
            new FakeRacClient(), BuildParams(), ClusterId,
            BuildRows("База1"), new ManagementServer[] { source, target }, source);

        Assert.Single(vm.AvailableServers);
        Assert.Same(target, vm.AvailableServers[0]);
        Assert.True(vm.HasAvailableServers);
        Assert.Equal(1, vm.BaseCount);
        Assert.False(vm.IsBusy);
        Assert.False(vm.MigrationCompleted);
        Assert.Null(vm.Result);
    }

    [Fact]
    public void Ctor_FiltersRowsWithoutSource()
    {
        var withSource = BuildRow("База1", FakeRacClient.FirstInfobaseId);
        var withoutSource = new ClusterImportRow(
            "База2", "sub", "cluster", "", true, false, null, new Infobase(), null!);

        var vm = new ClusterMigrationViewModel(
            new FakeRacClient(), BuildParams(), ClusterId,
            new[] { withSource, withoutSource }, Array.Empty<ManagementServer>(), null);

        Assert.Equal(1, vm.BaseCount);
    }

    [Fact]
    public async Task SelectedServerChange_LoadsClusters_SingleClusterAutoSelected()
    {
        var client = new FakeRacClient(singleCluster: true);
        var vm = new ClusterMigrationViewModel(
            client, BuildParams(), ClusterId, BuildRows("База1"),
            new[] { BuildServer("tgt", "srv-new") }, null);

        vm.SelectedServer = vm.AvailableServers[0];

        // Дожидаемся загрузки кластеров (fire-and-forget из сеттера).
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.Clusters.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.Single(vm.Clusters);
        Assert.Equal(FakeRacClient.TargetClusterId, vm.SelectedClusterId); // автовыбор единственного
        Assert.Equal("srv-new", client.LastParams!.Address);
        Assert.Empty(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task ClusterSelection_WithTargetBases_ExposesConflicts()
    {
        // Имитация приёмника, где «База1» уже существует (issue #41, 1.0.0.52).
        var client = new FakeRacClient(
            singleCluster: true, targetBaseNames: new[] { "База1", "ДругаяБаза" });
        var vm = new ClusterMigrationViewModel(
            client, BuildParams(), ClusterId, BuildRows("База1", "База2"),
            new[] { BuildServer("tgt", "srv-new") }, null);

        vm.SelectedServer = vm.AvailableServers[0];
        await AwaitClusters(vm);                  // автовыбор единственного кластера
        await Task.Delay(150);                    // завершение фоновой проверки конфликтов

        Assert.True(vm.HasConflicts);
        Assert.Equal(new[] { "База1" }, vm.ConflictingBaseNames);
        Assert.Equal(1, vm.ConflictingBaseNames.Count);
    }

    [Fact]
    public async Task ClusterSelection_WithoutConflicts_HasEmptyConflicts()
    {
        var client = new FakeRacClient(
            singleCluster: true, targetBaseNames: new[] { "СовсемДругаяБаза" });
        var vm = new ClusterMigrationViewModel(
            client, BuildParams(), ClusterId, BuildRows("База1", "База2"),
            new[] { BuildServer("tgt", "srv-new") }, null);

        vm.SelectedServer = vm.AvailableServers[0];
        await AwaitClusters(vm);
        await Task.Delay(150);

        Assert.False(vm.HasConflicts);
        Assert.Empty(vm.ConflictingBaseNames);
    }

    [Fact]
    public async Task Migrate_CreatesInTarget_AndDropsFromSourceEachBase()
    {
        var client = new FakeRacClient();
        var rows = BuildRows("База1", "База2");
        var vm = new ClusterMigrationViewModel(
            client, BuildParams(), ClusterId, rows,
            new[] { BuildServer("tgt", "srv-new") }, null);
        vm.SelectedServer = vm.AvailableServers[0];
        await AwaitClusters(vm);
        vm.DbUser = "db-user";
        vm.DbPassword = "db-pass";
        vm.KeepInSource = false;

        await vm.MigrateAsync();

        Assert.True(vm.MigrationCompleted);
        Assert.Empty(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        Assert.Equal(2, vm.Result!.MigratedBaseNames.Count);
        Assert.Equal(new[] { "База1", "База2" }, vm.Result.MigratedBaseNames);
        Assert.Equal("srv-new-host", vm.Result.NewServer);   // hostName из «cluster info»
        Assert.Equal(1542, vm.Result.NewPort);               // порт кластера назначения

        // Каждая база: create в новом кластере + drop из старого (dropDatabase=false).
        Assert.Equal(2, client.Created.Count);
        Assert.Equal(2, client.Dropped.Count);
        Assert.All(client.Created, c => Assert.Equal(FakeRacClient.TargetClusterId, c.ClusterId));
        Assert.All(client.Dropped, d => Assert.False(d.DropDatabase));
        // Drop шёл по соответствию очереди create: те же базы, тот же порядок.
        Assert.Equal(rows.Select(r => r.Source.InfobaseId), client.Dropped.Select(d => d.InfobaseId));
        Assert.Equal("db-user", client.Created[0].Request.DbUser);
        Assert.Equal("db-pass", client.Created[0].Request.DbPassword);
        Assert.Equal("MSSQLServer", client.Created[0].Request.Dbms);   // те же данные, что в источнике
        Assert.Equal(rows.Select(r => r.Name), client.Created.Select(c => c.Request.Name));
    }

    [Fact]
    public async Task Migrate_KeepInSource_CreateOnly_NoDrop()
    {
        var client = new FakeRacClient();
        var vm = new ClusterMigrationViewModel(
            client, BuildParams(), ClusterId, BuildRows("База1"),
            new[] { BuildServer("tgt", "srv-new") }, null);
        vm.SelectedServer = vm.AvailableServers[0];
        await AwaitClusters(vm);
        vm.KeepInSource = true; // «Не удалять в старом»

        await vm.MigrateAsync();

        Assert.True(vm.MigrationCompleted);
        Assert.Single(client.Created);
        Assert.Empty(client.Dropped); // копия в другой кластер БЕЗ удаления
    }

    [Fact]
    public async Task Migrate_Progress_UpdatesIndexTotalCurrentBase()
    {
        var client = new FakeRacClient();
        var vm = new ClusterMigrationViewModel(
            client, BuildParams(), ClusterId, BuildRows("База1", "База2", "База3"),
            new[] { BuildServer("tgt", "srv-new") }, null);
        vm.SelectedServer = vm.AvailableServers[0];
        await AwaitClusters(vm);

        await vm.MigrateAsync();

        Assert.Equal(3, vm.ProgressTotal);
        Assert.Equal(3, vm.ProgressIndex);
        Assert.Equal("База3", vm.CurrentBaseName);
        // Прогресс не пуст: «N из M: база» — но в тестовом окружении локализация
        // может вернуть ключ, поэтому проверяем только отсутствие нулевых значений.
        Assert.NotEmpty(vm.ProgressText);
    }

    [Fact]
    public async Task Migrate_CreateFailure_SetsError_Stops()
    {
        var client = new FakeRacClient(failCreate: true);
        var vm = new ClusterMigrationViewModel(
            client, BuildParams(), ClusterId, BuildRows("База1", "База2"),
            new[] { BuildServer("tgt", "srv-new") }, null);
        vm.SelectedServer = vm.AvailableServers[0];
        await AwaitClusters(vm);

        await vm.MigrateAsync();

        Assert.False(vm.MigrationCompleted);
        Assert.Null(vm.Result);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        // Первая база не создана — drop не выполнялся, вторая не обработана.
        Assert.Empty(client.Dropped);
        Assert.Single(client.Created);
    }

    [Fact]
    public async Task Migrate_DropFailure_SetsError()
    {
        var client = new FakeRacClient(failDrop: true);
        var vm = new ClusterMigrationViewModel(
            client, BuildParams(), ClusterId, BuildRows("База1"),
            new[] { BuildServer("tgt", "srv-new") }, null);
        vm.SelectedServer = vm.AvailableServers[0];
        await AwaitClusters(vm);

        await vm.MigrateAsync();

        Assert.False(vm.MigrationCompleted);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Single(client.Created);   // база добавлена в новый кластер
        Assert.Single(client.Dropped);   // попытка удаления была, но не удалась
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Migrate_NoTargetServer_Validates()
    {
        var vm = new ClusterMigrationViewModel(
            new FakeRacClient(), BuildParams(), ClusterId, BuildRows("База1"),
            Array.Empty<ManagementServer>(), null);

        await vm.MigrateAsync();

        Assert.False(vm.MigrationCompleted);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Empty(vm.Clusters);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Migrate_NoTargetCluster_Validates()
    {
        var client = new FakeRacClient(singleCluster: false); // несколько кластеров — автовыбора нет
        var vm = new ClusterMigrationViewModel(
            client, BuildParams(), ClusterId, BuildRows("База1"),
            new[] { BuildServer("tgt", "srv-new") }, null);
        vm.SelectedServer = vm.AvailableServers[0];
        await AwaitClusters(vm);
        Assert.Null(vm.SelectedClusterId);

        await vm.MigrateAsync();

        Assert.False(vm.MigrationCompleted);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Empty(client.Created);    // кластер не выбран — rac не трогаем
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Migrate_NoBases_Validates()
    {
        var vm = new ClusterMigrationViewModel(
            new FakeRacClient(), BuildParams(), ClusterId,
            Array.Empty<ClusterImportRow>(), Array.Empty<ManagementServer>(), null);

        await vm.MigrateAsync();

        Assert.False(vm.MigrationCompleted);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Equal(0, vm.BaseCount);
    }

    [Fact]
    public async Task SetReplaceConnectionStrings_UpdatesResultFlag()
    {
        var client = new FakeRacClient();
        var vm = new ClusterMigrationViewModel(
            client, BuildParams(), ClusterId, BuildRows("База1"),
            new[] { BuildServer("tgt", "srv-new") }, null);
        vm.SelectedServer = vm.AvailableServers[0];
        await AwaitClusters(vm);

        await vm.MigrateAsync();

        Assert.True(vm.MigrationCompleted);
        Assert.False(vm.Result!.ReplaceConnectionStrings);
        vm.SetReplaceConnectionStrings(true);
        Assert.True(vm.Result.ReplaceConnectionStrings);
        vm.SetReplaceConnectionStrings(false);
        Assert.False(vm.Result.ReplaceConnectionStrings);
    }

    // ===================== Хелперы =====================

    private static readonly Guid ClusterId = FakeRacClient.SourceClusterId;

    private static async Task AwaitClusters(ClusterMigrationViewModel vm)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.Clusters.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    private static RacConnectionParams BuildParams() => new()
    {
        Address = "srv-old",
        Port = 1540,
        User = "admin",
        Password = "secret"
    };

    private static ManagementServer BuildServer(string id, string address) => new()
    {
        Id = id,
        Description = "Сервер " + address,
        Address = address,
        Port = 1545,
        User = "admin",
        Password = "secret"
    };

    private static List<ClusterImportRow> BuildRows(params string[] names) =>
        names.Select(name => BuildRow(name, Guid.NewGuid())).ToList();

    private static ClusterImportRow BuildRow(string name, Guid infobaseId) => new(
        name,
        "Описание • MSSQLServer",
        "Кластер-источник",
        $"Srvr=\"srv-old\";Ref=\"{name}\"",
        isChecked: true,
        isDuplicate: false,
        skipReason: null,
        tag: new Infobase { Name = name },
        source: new RacInfobaseSummary
        {
            InfobaseId = infobaseId,
            Name = name,
            Descr = "Описание",
            Dbms = "MSSQLServer",
            DbServer = "sql-01",
            DbName = name.ToLowerInvariant()
        });

    // ===================== Fake =====================

    /// <summary>
    /// Fake-клиент rac для тестов миграции: кластеры назначения (один по умолчанию,
    /// несколько по запросу), «cluster info» с hostName, подсчёт и запись вызовов
    /// «infobase create»/«infobase drop» (с параметрами) и имитация сбоев.
    /// </summary>
    private sealed class FakeRacClient : IRacClient
    {
        public static readonly Guid SourceClusterId = Guid.Parse("99000000-0000-0000-0000-000000000001");
        public static readonly Guid TargetClusterId = Guid.Parse("88000000-0000-0000-0000-000000000002");
        public static readonly Guid TargetCluster2Id = Guid.Parse("77000000-0000-0000-0000-000000000003");
        public static readonly Guid FirstInfobaseId = Guid.Parse("66000000-0000-0000-0000-000000000001");

        private readonly bool _singleCluster;
        private readonly bool _failCreate;
        private readonly bool _failDrop;
        private readonly string[] _targetBaseNames;

        public FakeRacClient(bool singleCluster = true, bool failCreate = false, bool failDrop = false,
            string[]? targetBaseNames = null)
        {
            _singleCluster = singleCluster;
            _failCreate = failCreate;
            _failDrop = failDrop;
            _targetBaseNames = targetBaseNames ?? Array.Empty<string>();
        }

        public RacConnectionParams? LastParams { get; private set; }

        public List<(Guid ClusterId, RacInfobaseCreateRequest Request)> Created { get; } = new();
        public List<(Guid ClusterId, Guid InfobaseId, bool DropDatabase)> Dropped { get; } = new();

        public string LastActionError { get; private set; } = string.Empty;

        public Task<IReadOnlyList<RacCluster>> GetClustersAsync(
            RacConnectionParams parameters, CancellationToken cancellationToken = default)
        {
            LastParams = parameters;
            IReadOnlyList<RacCluster> clusters = _singleCluster
                ? new RacCluster[] { new() { Id = TargetClusterId, Name = "Новый кластер", Port = 1542 } }
                : new RacCluster[]
                {
                    new() { Id = TargetClusterId, Name = "Новый кластер", Port = 1542 },
                    new() { Id = TargetCluster2Id, Name = "Второй новый", Port = 2542 }
                };
            return Task.FromResult(clusters);
        }

        public Task<RacClusterInfo?> GetClusterInfoAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RacClusterInfo?>(new RacClusterInfo
            {
                Name = "Новый кластер",
                HostName = "srv-new-host",
                Port = clusterId == TargetClusterId ? 1542 : 2542
            });

        public Task<IReadOnlyList<RacInfobaseSummary>> GetInfobasesAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacInfobaseSummary>>(
                _targetBaseNames.Select(name => new RacInfobaseSummary { Name = name }).ToList());

        public Task<bool> CreateInfobaseAsync(
            RacConnectionParams parameters, Guid clusterId, RacInfobaseCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            Created.Add((clusterId, request));
            if (_failCreate)
            {
                LastActionError = "нет прав на создание базы (имитация)";
                return Task.FromResult(false);
            }
            LastActionError = string.Empty;
            return Task.FromResult(true);
        }

        public Task<bool> DropInfobaseAsync(
            RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
            bool dropDatabase = false, CancellationToken cancellationToken = default)
        {
            Dropped.Add((clusterId, infobaseId, dropDatabase));
            if (_failDrop)
            {
                LastActionError = "нет прав на удаление базы (имитация)";
                return Task.FromResult(false);
            }
            LastActionError = string.Empty;
            return Task.FromResult(true);
        }

        // ---- Неиспользуемые методы интерфейса ----

        public Task<bool> UpdateClusterAsync(
            RacConnectionParams parameters, Guid clusterId, RacClusterUpdate changes,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<IReadOnlyList<RacProcessInfo>> GetProcessesAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacProcessInfo>>(Array.Empty<RacProcessInfo>());

        public Task<IReadOnlyList<RacSessionInfo>> GetSessionsAsync(
            RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacSessionInfo>>(Array.Empty<RacSessionInfo>());

        public Task<IReadOnlyList<RacConnectionInfo>> GetConnectionsAsync(
            RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacConnectionInfo>>(Array.Empty<RacConnectionInfo>());

        public Task<IReadOnlyList<RacLockInfo>> GetLocksAsync(
            RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacLockInfo>>(Array.Empty<RacLockInfo>());

        public Task<IReadOnlyList<RacJobInfo>> GetJobsAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacJobInfo>>(Array.Empty<RacJobInfo>());

        public Task<bool> SetJobStateAsync(
            RacConnectionParams parameters, Guid clusterId, Guid jobId, RacJobAction action,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> TerminateSessionAsync(
            RacConnectionParams parameters, Guid clusterId, Guid sessionId,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> DisconnectConnectionAsync(
            RacConnectionParams parameters, Guid clusterId, Guid connectionId,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> UpdateInfobaseAsync(
            RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
            RacInfobaseUpdateRequest request, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
