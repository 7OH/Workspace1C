using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты ViewModel окна «Управление службами» (issue #11) на fake
/// <see cref="IServiceControllerService"/>/<see cref="IDialogService"/>:
/// загрузка статусов, доступность команд по выделению и возможностям службы,
/// вызовы операций и перечитывание статусов, ошибки операций, «Все сервисы».
/// </summary>
public sealed class ServicesWindowViewModelTests
{
    [Fact]
    public async Task LoadAsync_FromStore_CreatesRowsWithStatuses()
    {
        var env = Create();
        env.Fake.Services["srv-a"] = Info("srv-a", "Service A", ServiceStatus.Running);
        env.Fake.Services["srv-b"] = Info("srv-b", "Service B", ServiceStatus.Stopped);
        Seed(env, "srv-a", "srv-b");

        await env.Vm.LoadAsync();

        Assert.Equal(2, env.Vm.Rows.Count);
        Assert.True(env.Vm.HasRows);
        Assert.Equal(ServiceStatus.Running, env.Vm.Rows.OfType<ServiceInfoRow>().Single(r => r.ServiceName == "srv-a").StatusKind);
        Assert.Equal(ServiceStatus.Stopped, env.Vm.Rows.OfType<ServiceInfoRow>().Single(r => r.ServiceName == "srv-b").StatusKind);
        Assert.Equal("Service A", env.Vm.Rows[0].DisplayName);
    }

    [Fact]
    public async Task LoadAsync_EmptyStore_NoRows()
    {
        var env = Create();
        await env.Vm.LoadAsync();

        Assert.Empty(env.Vm.Rows);
        Assert.False(env.Vm.HasRows);
    }

    [Fact]
    public async Task SelectedRow_UpdatesCommandAvailability()
    {
        var env = Create();
        env.Fake.Services["run"] = Info("run", "Run", ServiceStatus.Running, canPause: true);
        env.Fake.Services["stop"] = Info("stop", "Stop", ServiceStatus.Stopped);
        Seed(env, "run", "stop");
        await env.Vm.LoadAsync();

        Assert.False(env.Vm.CanStart);
        Assert.False(env.Vm.CanStop);

        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceInfoRow>().First(r => r.ServiceName == "stop");
        Assert.True(env.Vm.CanStart);
        Assert.False(env.Vm.CanStop);
        Assert.False(env.Vm.CanPause);
        Assert.False(env.Vm.CanContinue);

        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceInfoRow>().First(r => r.ServiceName == "run");
        Assert.False(env.Vm.CanStart);
        Assert.True(env.Vm.CanStop);
        Assert.True(env.Vm.CanPause);
        Assert.False(env.Vm.CanContinue);
        Assert.True(env.Vm.CanRestart);
    }

    [Fact]
    public async Task StartCommand_InvokesServiceAndRefreshesStatus()
    {
        var env = Create();
        env.Fake.Services["srv-a"] = Info("srv-a", "Service A", ServiceStatus.Stopped);
        Seed(env, "srv-a");
        await env.Vm.LoadAsync();
        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceInfoRow>().First(r => r.ServiceName == "srv-a");

        await env.Vm.ExecuteCommandAsync(ServiceAction.Start, all: false);

        Assert.Contains(("Start", "srv-a"), env.Fake.Calls);
        var row = env.Vm.Rows.OfType<ServiceInfoRow>().Single(r => r.ServiceName == "srv-a");
        Assert.Equal(ServiceStatus.Running, row.StatusKind);
        Assert.False(env.Vm.IsBusy);
    }

    [Fact]
    public async Task StopCommand_RequiresSelection()
    {
        var env = Create();
        env.Fake.Services["run"] = Info("run", "Run", ServiceStatus.Running, canPause: false);
        Seed(env, "run");
        await env.Vm.LoadAsync();

        // Без выделения команда не выполняется.
        await env.Vm.ExecuteCommandAsync(ServiceAction.Stop, all: false);
        Assert.DoesNotContain(("Stop", "run"), env.Fake.Calls);
    }

    [Fact]
    public async Task CommandFailure_ShowsErrorDialog_AndNoHang()
    {
        var env = Create();
        env.Fake.CommandResult = false;
        env.Fake.Services["run"] = Info("run", "Run", ServiceStatus.Running, canPause: true);
        Seed(env, "run");
        await env.Vm.LoadAsync();
        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceInfoRow>().First(r => r.ServiceName == "run");

        await env.Vm.ExecuteCommandAsync(ServiceAction.Stop, all: false);

        Assert.Single(env.Dialogs.Errors);
        Assert.False(env.Vm.IsBusy);
        Assert.Equal(ServiceStatus.Running, env.Vm.Rows.OfType<ServiceInfoRow>().Single(r => r.ServiceName == "run").StatusKind);
    }

    [Fact]
    public async Task Refresh_ReReadsStatuses()
    {
        var env = Create();
        env.Fake.Services["srv"] = Info("srv", "Srv", ServiceStatus.Running);
        Seed(env, "srv");
        await env.Vm.LoadAsync();
        Assert.Equal(ServiceStatus.Running, ((ServiceInfoRow)env.Vm.Rows[0]).StatusKind);

        env.Fake.Services["srv"] = Info("srv", "Srv", ServiceStatus.Stopped);
        await env.Vm.RefreshAsync();

        Assert.Equal(ServiceStatus.Stopped, ((ServiceInfoRow)env.Vm.Rows[0]).StatusKind);
    }

    [Fact]
    public async Task All_Stop_CallsServiceOnlyForApplicable()
    {
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running, canPause: true, canStop: true);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Running, canPause: true, canStop: false);
        env.Fake.Services["c"] = Info("c", "C", ServiceStatus.Stopped);
        Seed(env, "a", "b", "c");
        await env.Vm.LoadAsync();

        await env.Vm.ExecuteCommandAsync(ServiceAction.Stop, all: true);

        Assert.Contains(("Stop", "a"), env.Fake.Calls);
        Assert.DoesNotContain(("Stop", "b"), env.Fake.Calls);
        Assert.DoesNotContain(("Stop", "c"), env.Fake.Calls);
    }

    [Fact]
    public async Task All_Start_CallsServiceForStoppedRows()
    {
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Stopped);
        Seed(env, "a", "b");
        await env.Vm.LoadAsync();

        await env.Vm.ExecuteCommandAsync(ServiceAction.Start, all: true);

        Assert.DoesNotContain(("Start", "a"), env.Fake.Calls);
        Assert.Contains(("Start", "b"), env.Fake.Calls);
    }

    [Fact]
    public void OpenSystemDialog_InvokedWhenAvailable()
    {
        var env = Create();
        env.Fake.DialogAvailable = true;
        env.Vm.OpenSystemDialogCommand.Execute(null);

        Assert.Equal(1, env.Fake.SystemDialogCalls);
    }

    [Fact]
    public async Task PlatformUnavailable_LoadClearsAndCommandsDoNothing()
    {
        var env = Create(f => f.Available = false);
        await env.Vm.LoadAsync();

        Assert.Empty(env.Vm.Rows);
        Assert.False(env.Vm.IsPlatformAvailable);
        // Команды без доступа бездействуют и не падают.
        await env.Vm.ExecuteCommandAsync(ServiceAction.Start, all: true);
        Assert.False(env.Vm.IsBusy);
    }

    [Fact]
    public async Task ApplyConfiguration_SavesAndRebuildsRows()
    {
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Stopped);
        env.Fake.Services["c"] = Info("c", "C", ServiceStatus.Stopped);
        Seed(env, "a", "b", "c");
        await env.Vm.LoadAsync();

        await env.Vm.ApplyConfigurationAsync(new[] { "b", "c" });

        Assert.Equal(new[] { "b", "c" }, env.Store.Saved);
        Assert.Equal(2, env.Vm.Rows.Count);
        Assert.Equal("c", ((ServiceInfoRow)env.Vm.Rows[1]).ServiceName);
    }

    // ===================== Вспомогательное =====================

    private static ServiceInfo Info(string name, string display, ServiceStatus status, bool canPause = false, bool canStop = true) => new()
    {
        ServiceName = name,
        DisplayName = display,
        Status = status,
        CanPauseAndContinue = canPause,
        CanStop = canStop
    };

    /// <summary>Заполняет хранилище именами служб (строки появляются после LoadAsync).</summary>
    private static void Seed(TestEnv env, params string[] names)
    {
        env.Store.Stored = ServiceStoreData.FromLegacy(names);
    }

    private static TestEnv Create(Action<FakeServiceController>? configure = null)
    {
        var fake = new FakeServiceController();
        configure?.Invoke(fake);
        var store = new FakeServicesStore();
        var dialogs = new FakeDialogService();
        var vm = new ServicesWindowViewModel(fake, store, dialogs, dispatchToUi: null);
        return new TestEnv(vm, fake, dialogs, store);
    }

    /// <summary>Сборка окружения теста: VM + фейковые сервисы.</summary>
    private sealed record TestEnv(
        ServicesWindowViewModel Vm,
        FakeServiceController Fake,
        FakeDialogService Dialogs,
        FakeServicesStore Store);

    /// <summary>Fake-сервис управления службами с управляемыми статусами и журналом вызовов.</summary>
    internal sealed class FakeServiceController : IServiceControllerService
    {
        public bool Available { get; set; } = true;
        public bool DialogAvailable { get; set; } = true;
        public bool CommandResult { get; set; } = true;
        public Dictionary<string, ServiceInfo> Services { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Action, string Name)> Calls { get; } = new();
        public int SystemDialogCalls;

        public bool IsAvailable => Available;
        public bool CanOpenSystemDialog => DialogAvailable;

        public IReadOnlyList<ServiceInfo> GetAllServices() =>
            Services.Values.OrderBy(s => s.ServiceName).Select(s => s.Clone()).ToList();

        public ServiceInfo GetStatus(string serviceName)
        {
            if (!Services.TryGetValue(serviceName, out var info))
                return new ServiceInfo { ServiceName = serviceName, DisplayName = serviceName, Status = ServiceStatus.NotFound };
            return info.Clone();
        }

        public void OpenSystemServicesDialog() => SystemDialogCalls++;

        public bool Start(string name)
        {
            Calls.Add(("Start", name));
            if (CommandResult && Services.TryGetValue(name, out var info)) info.Status = ServiceStatus.Running;
            return CommandResult;
        }

        public bool Stop(string name)
        {
            Calls.Add(("Stop", name));
            if (CommandResult && Services.TryGetValue(name, out var info)) info.Status = ServiceStatus.Stopped;
            return CommandResult;
        }

        public bool Pause(string name)
        {
            Calls.Add(("Pause", name));
            if (CommandResult && Services.TryGetValue(name, out var info)) info.Status = ServiceStatus.Paused;
            return CommandResult;
        }

        public bool Continue(string name)
        {
            Calls.Add(("Continue", name));
            if (CommandResult && Services.TryGetValue(name, out var info)) info.Status = ServiceStatus.Running;
            return CommandResult;
        }

        public bool Restart(string name)
        {
            Calls.Add(("Restart", name));
            if (CommandResult && Services.TryGetValue(name, out var info)) info.Status = ServiceStatus.Running;
            return CommandResult;
        }
    }

    /// <summary>Fake-диалоги: записывает сообщения об ошибках, остальное — no-op.</summary>
    internal sealed class FakeDialogService : IDialogService
    {
        public List<string> Errors { get; } = new();

        /// <summary>Результат <see cref="Confirm"/> (по умолчанию true; для сценариев отмены — false).</summary>
        public bool ConfirmResult { get; set; } = true;

        public void ShowInfo(string message, string title = "") { }
        public void ShowWarning(string message, string title = "") { }
        public void ShowError(string message, string title = "") => Errors.Add(message);
        public bool Confirm(string message, string title = "") => ConfirmResult;
        public string? OpenFileDialog(string title = "", string filter = "", string? initialDirectory = null) => null;
        public string? SaveFileDialog(string title = "", string defaultFileName = "", string filter = "", string? initialDirectory = null) => null;
        public string? OpenFolderDialog(string title = "", string? initialDirectory = null) => null;
    }

    /// <summary>Fake-хранилище списка служб (in-memory, журнал сохранений служб и порядка).</summary>
    internal sealed class FakeServicesStore : IServicesStore
    {
        public List<string> Saved { get; } = new();
        public List<string> SavedOrder { get; } = new();
        public ServiceStoreData Stored { get; set; } = new();

        public ServiceStoreData Load() => Stored;

        public void Save(ServiceStoreData data)
        {
            Saved.Clear();
            Saved.AddRange(data?.Services ?? new List<string>());
            SavedOrder.Clear();
            SavedOrder.AddRange(data?.Order ?? new List<string>());
            Stored = data ?? new ServiceStoreData();
        }
    }
}