using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты задания по расписанию «Обновить приложение» (<see cref="ScheduledTaskKind.UpdateApp"/>,
/// issues #8/#10): актуальная версия — успех без ложного уведомления об ошибке и без спама
/// уведомлений; сбой проверки (null-релиз) — ошибка; новая версия с адресом загрузки —
/// успех с вызовом установки и подавлением уведомления «задание выполнено» при успешно
/// применённом обновлении (issue #10): единственное сообщение — «Приложение обновлено до
/// версии X» из новой версии после перезапуска. Если обновление применить не удалось —
/// уведомление о завершении задания уходит как обычно. Паттерн повторяет тесты проверки
/// платформы/конфигурации (issues #366/#367): планировщик собирается на фейках, без сети,
/// файловой системы и UI.
/// </summary>
public sealed class SchedulerServiceUpdateAppTests
{
    // Тег заведомо НЕ новее установленной версии (сейчас 1.0.0.5) — сценарий «версия актуальна».
    private const string UpToDateTag = "v0.1.0.0";

    // Тег заведомо новее установленной версии — сценарий «доступно обновление».
    private const string NewReleaseTag = "v99.0.0.0";

    [Fact]
    public async Task RunNow_ReleaseNotNewer_Success_NoErrorNotification_NoSpam()
    {
        var gitHub = new FakeGitHubReleaseService(new ReleaseInfo
        {
            TagName = UpToDateTag,
            DownloadUrl = "https://example.test/Workspace1C.exe"
        });
        var installer = new FakeAppUpdateService();
        var notifications = new FakeNotifications();
        var store = new FakeTaskStore();
        var task = NewUpdateAppTask();
        store.Tasks.Add(task);

        using var scheduler = CreateScheduler(store, gitHub, installer, notifications);
        var result = await scheduler.RunNowAsync(task);

        // «Версия актуальна» — НЕ ошибка: задание завершилось успешно.
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(LocalizationManager.T("Update.UpToDate"), result.ScenarioName);
        Assert.True(result.SuppressNotification);

        // Успех дошёл до статуса задания: LastRunMessage содержит текст «версия актуальна».
        Assert.True(task.LastRunSuccess);
        Assert.Equal(LocalizationManager.T("Update.UpToDate"), task.LastRunMessage);

        // Никакого спама: установка не запускалась, уведомление о завершении не отправлялось.
        Assert.Equal(0, installer.InstallCalls);
        Assert.Equal(0, notifications.ShowCount);
    }

    [Fact]
    public async Task RunNow_ReleaseNull_Error()
    {
        var gitHub = new FakeGitHubReleaseService(null);
        var installer = new FakeAppUpdateService();
        var notifications = new FakeNotifications();
        var store = new FakeTaskStore();
        var task = NewUpdateAppTask();
        store.Tasks.Add(task);

        using var scheduler = CreateScheduler(store, gitHub, installer, notifications);
        var result = await scheduler.RunNowAsync(task);

        // Сбой проверки остаётся ошибкой (issue #8): текст Update.CheckFailed.
        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal(LocalizationManager.T("Update.CheckFailed"), result.ErrorMessage);
        Assert.False(result.SuppressNotification);
        Assert.False(task.LastRunSuccess);
        Assert.Equal(LocalizationManager.T("Update.CheckFailed"), task.LastRunMessage);

        // Установка не запускалась; настоящая ошибка по-прежнему уведомляется.
        Assert.Equal(0, installer.InstallCalls);
        Assert.Equal(1, notifications.ShowCount);
        Assert.Equal(NotificationKind.Error, notifications.LastKind);
    }

    [Fact]
    public async Task RunNow_NewReleaseWithDownloadUrl_UpdateApplied_JobFinishedSuppressed()
    {
        var release = new ReleaseInfo { TagName = NewReleaseTag, DownloadUrl = "https://example.test/Workspace1C.exe" };
        var gitHub = new FakeGitHubReleaseService(release);
        var installer = new FakeAppUpdateService();
        var notifications = new FakeNotifications();
        var store = new FakeTaskStore();
        var task = NewUpdateAppTask();
        store.Tasks.Add(task);

        using var scheduler = CreateScheduler(store, gitHub, installer, notifications);
        var result = await scheduler.RunNowAsync(task);

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(NewReleaseTag, result.ScenarioName);
        // Обновление применено (фейк вернул true) — уведомление о завершении подавлено
        // (issue #10): после перезапуска единственное сообщение пришлёт новая версия.
        Assert.True(result.SuppressNotification);
        Assert.True(task.LastRunSuccess);
        Assert.Equal(NewReleaseTag, task.LastRunMessage);

        // Обновление реально запущено: скачивание+установка вызваны ровно один раз.
        Assert.Equal(1, installer.InstallCalls);
        Assert.Same(release, installer.LastRelease);
        // Никакого отдельного «Задание выполнено…» — обновление объявит о себе само.
        Assert.Equal(0, notifications.ShowCount);
    }

    [Fact]
    public async Task RunNow_NewReleaseUpdateFailed_JobFinishedNotificationSent()
    {
        // Применение обновления не удалось: фейк возвращает false, как реальный сервис
        // при сбое скачивания/замены (ошибку он показывает пользователю, но наружу
        // не выбрасывает).
        var release = new ReleaseInfo { TagName = NewReleaseTag, DownloadUrl = "https://example.test/Workspace1C.exe" };
        var gitHub = new FakeGitHubReleaseService(release);
        var installer = new FakeAppUpdateService { Result = false };
        var notifications = new FakeNotifications();
        var store = new FakeTaskStore();
        var task = NewUpdateAppTask();
        store.Tasks.Add(task);

        using var scheduler = CreateScheduler(store, gitHub, installer, notifications);
        var result = await scheduler.RunNowAsync(task);

        // Попытка установки была, но подавление НЕ включается: уведомление о завершении
        // задания уходит как обычно (issue #10 — только успешное применение «молчит»).
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(NewReleaseTag, result.ScenarioName);
        Assert.False(result.SuppressNotification);
        Assert.True(task.LastRunSuccess);
        Assert.Equal(NewReleaseTag, task.LastRunMessage);

        Assert.Equal(1, installer.InstallCalls);
        Assert.Equal(1, notifications.ShowCount);
        Assert.Equal(NotificationKind.Success, notifications.LastKind);
    }

    [Fact]
    public async Task RunNow_NewReleaseNoDownloadUrl_Error()
    {
        var gitHub = new FakeGitHubReleaseService(new ReleaseInfo { TagName = NewReleaseTag });
        var installer = new FakeAppUpdateService();
        var notifications = new FakeNotifications();
        var store = new FakeTaskStore();
        var task = NewUpdateAppTask();
        store.Tasks.Add(task);

        using var scheduler = CreateScheduler(store, gitHub, installer, notifications);
        var result = await scheduler.RunNowAsync(task);

        // Нет адреса загрузки — настоящая ошибка (Update.NoDownloadUrl), установка не запускается.
        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal(LocalizationManager.T("Update.NoDownloadUrl"), result.ErrorMessage);
        Assert.False(task.LastRunSuccess);
        Assert.Equal(LocalizationManager.T("Update.NoDownloadUrl"), task.LastRunMessage);
        Assert.Equal(0, installer.InstallCalls);
        Assert.Equal(1, notifications.ShowCount);
    }

    // ---- Вспомогательные ----

    private static ScheduledTask NewUpdateAppTask() => new()
    {
        Name = "Проверка обновлений программы",
        Kind = ScheduledTaskKind.UpdateApp
    };

    private static SchedulerService CreateScheduler(
        FakeTaskStore store,
        FakeGitHubReleaseService gitHub,
        FakeAppUpdateService installer,
        FakeNotifications notifications) => new(
        store,
        new FakeScenarios(),
        new FakeRepository(),
        null!, // IBackupService: задание обновления приложения его не использует
        null!, // IConfigUpdateService: —//—
        null!, // IInfobaseAdminService: —//—
        gitHub,
        installer,
        notifications,
        new FakePlatformUpdateService(),
        new FakePlatformVersions(),
        null!, // IOneCUpdatesService: —//—
        new FakeConfigTypes(),
        new FakeMainWindowNotifier());

    /// <summary>Фейковое хранилище заданий в памяти (без файловой системы).</summary>
    private sealed class FakeTaskStore : IScheduledTaskStore
    {
        public List<ScheduledTask> Tasks { get; } = new();

        public string TasksDirectory => "";
        public IReadOnlyList<ScheduledTask> LoadAll() => Tasks;
        public void Save(ScheduledTask task) { }
        public void Delete(string id) { }
        public ScheduledTask? Get(string id) => Tasks.FirstOrDefault(t => t.Id == id);
    }

    /// <summary>Фейк GitHub-релизов: возвращает заранее заданный результат без сети.</summary>
    private sealed class FakeGitHubReleaseService : GitHubReleaseService
    {
        private readonly ReleaseInfo? _release;

        public FakeGitHubReleaseService(ReleaseInfo? release) => _release = release;

        public override Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken ct = default)
            => Task.FromResult(_release);
    }

    /// <summary>Фейк подсистемы автообновления: считает вызовы установки и возвращает
    /// настраиваемый результат применения (по умолчанию — успех, как реальный сервис
    /// при успешно запущенном помощнике и записанном маркере).</summary>
    private sealed class FakeAppUpdateService : IAppUpdateService
    {
        public int InstallCalls { get; private set; }
        public ReleaseInfo? LastRelease { get; private set; }

        /// <summary>Результат применения обновления: true — помощник запущен и обновление
        /// применится после перезапуска, false — применить не удалось.</summary>
        public bool Result { get; set; } = true;

        public Task<bool> DownloadAndInstallAutoAsync(ReleaseInfo release)
        {
            InstallCalls++;
            LastRelease = release;
            return Task.FromResult(Result);
        }
    }

    /// <summary>Фейк системных уведомлений — считает вызовы и запоминает вид.</summary>
    private sealed class FakeNotifications : INotificationService
    {
        public int ShowCount { get; private set; }
        public NotificationKind? LastKind { get; private set; }

        public void Show(string title, string message) => ShowCount++;

        public void Show(string title, string message, NotificationKind kind, NotificationEvent evt = NotificationEvent.ManualTest)
        {
            ShowCount++;
            LastKind = kind;
        }
    }

    /// <summary>Фейк списка баз: задание обновления приложения его не использует.</summary>
    private sealed class FakeRepository : IInfobaseRepository
    {
        public List<Infobase> Bases { get; set; } = new();

        public List<Infobase> Load() => Bases;
        public void Save(List<Infobase> infobases) { }
        public Task SaveAsync(List<Infobase> infobases, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public List<Group> LoadGroups() => new();
        public void SaveGroups(List<Group> groups) { }
        public Task SaveGroupsAsync(List<Group> groups, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public AppSettings LoadSettings() => new();
        public void SaveSettings(AppSettings settings) { }
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Фейк сценариев резервирования (задание обновления приложения их не использует).</summary>
    private sealed class FakeScenarios : IBackupScenarioStore
    {
        public string ScenariosDirectory => "";
        public IReadOnlyList<BackupScenario> LoadAll() => Array.Empty<BackupScenario>();
        public BackupScenario? Get(string id) => null;
        public void Save(BackupScenario scenario) { }
        public void Delete(string id) { }
    }

    /// <summary>Фейк каталога платформы (задание обновления приложения его не использует).</summary>
    private sealed class FakePlatformUpdateService : IPlatformUpdateService
    {
        public Task<PlatformCatalogResult> GetAvailableReleasesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PlatformCatalogResult());

        public Task<PlatformCatalogResult> GetAllAvailableReleasesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PlatformCatalogResult());

        public Task<PlatformCatalogResult> GetAvailableReleasesForNickAsync(string nick, CancellationToken cancellationToken = default)
            => Task.FromResult(new PlatformCatalogResult());

        public Task<PlatformCatalogResult> LoadReleaseFilesAsync(PlatformRelease release, CancellationToken cancellationToken = default)
            => Task.FromResult(new PlatformCatalogResult());

        public Task<PlatformCatalogResult> LoadReleaseFilesForNickAsync(PlatformRelease release, string nick, CancellationToken cancellationToken = default)
            => Task.FromResult(new PlatformCatalogResult());

        public PlatformReleaseFile? PickDistribution(IReadOnlyList<PlatformReleaseFile> files) => null;
    }

    /// <summary>Фейк сканера установленных версий платформы.</summary>
    private sealed class FakePlatformVersions : IPlatformVersionService
    {
        public List<string> FindInstalledVersions(IEnumerable<string>? additionalPaths = null) => new();
    }

    /// <summary>Фейк хранилища типовых конфигураций (задание обновления приложения его не использует).</summary>
    private sealed class FakeConfigTypes : ICustomConfigTypesStore
    {
        public string FilePath => "";
        public IReadOnlyList<OneCConfigType> Load() => Array.Empty<OneCConfigType>();
        public void Save(IReadOnlyCollection<OneCConfigType> types) { }
        public IReadOnlyList<OneCConfigType> LoadAll() => Array.Empty<OneCConfigType>();
        public void RestoreDefaults() { }
    }

    /// <summary>Фейк показа сообщения в строке состояния главного окна.</summary>
    private sealed class FakeMainWindowNotifier : IMainWindowNotifier
    {
        public void ShowMessage(string message) { }
    }
}