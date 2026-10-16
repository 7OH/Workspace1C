using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты публичного метода <see cref="SchedulerService.Refresh"/> (issue #7 «Старт автообновления
/// заданий»): окно «Задания по расписанию» при добавлении/изменении/удалении задания сохраняет
/// его в хранилище, а планировщик узнаёт об этом только после пересборки кэша ближайших моментов.
/// До Refresh новое/изменённое задание отсутствует в кэше (или ждёт старого момента) — фоновый
/// тик его пропускает; после Refresh — попадает в расписание с ближайшим моментом запуска.
/// Кэш <c>_nextRuns</c> читается через рефлексию (поле приватное, но прямой контроль словаря
/// даёт детерминированную проверку без ожидания реального времени тика).
/// </summary>
public sealed class SchedulerServiceRefreshTests
{
    [Fact]
    public void TaskAddedAfterStart_NotInScheduleUntilRefresh()
    {
        var store = new FakeTaskStore();
        using var scheduler = CreateScheduler(store);
        scheduler.Start();
        try
        {
            var runs = NextRuns(scheduler);

            // Хранилище пустое — расписание после Start тоже пустое.
            Assert.Empty(runs);

            // Пользователь добавил задание из окна (сохранил в хранилище).
            var task = NewTask("Новое задание");
            store.Tasks.Add(task);

            // Баг из issue #7: планировщик о новом задании не знает до Refresh —
            // в кэше ключа нет, и ProcessDueAsync будет пропускать его (next is null).
            Assert.False(runs.ContainsKey(task.Id));

            // После Refresh задание появляется в расписании с ближайшим моментом запуска.
            scheduler.Refresh();
            Assert.True(runs.ContainsKey(task.Id));
            Assert.NotNull(runs[task.Id]);
        }
        finally
        {
            scheduler.Stop();
        }
    }

    [Fact]
    public void ModifiedTask_KeepsOldMomentUntilRefresh_ThenRecalculates()
    {
        var store = new FakeTaskStore();
        var task = NewTask("Ежедневно в 00:00", time: "00:00");
        store.Tasks.Add(task);

        using var scheduler = CreateScheduler(store);
        scheduler.Start();
        try
        {
            var runs = NextRuns(scheduler);
            var old = runs[task.Id];
            Assert.NotNull(old);

            // Пользователь изменил задание (сценарий issue #7): включён повтор каждые 5 минут.
            task.RepeatEnabled = true;
            task.RepeatIntervalValue = 5;
            task.RepeatIntervalUnit = ScheduledTaskRepeatUnit.Minutes;

            // До Refresh планировщик всё ещё ждёт СТАРОГО момента (например завтра 00:00)
            // и не выполняет задание, хотя повтор уже настроен.
            Assert.Equal(old, runs[task.Id]);

            // После Refresh момент пересчитан по новому расписанию (серия повторов).
            scheduler.Refresh();
            var expected = ScheduleCalculator.ComputeNextRun(task, DateTime.Now);
            Assert.NotNull(expected);
            Assert.NotNull(runs[task.Id]);
            Assert.Equal(expected!.Value, runs[task.Id]!.Value, TimeSpan.FromSeconds(10));
        }
        finally
        {
            scheduler.Stop();
        }
    }

    [Fact]
    public void DeletedTask_RemovedFromScheduleAfterRefresh()
    {
        var store = new FakeTaskStore();
        var task = NewTask("Удаляемое задание");
        store.Tasks.Add(task);

        using var scheduler = CreateScheduler(store);
        scheduler.Start();
        try
        {
            var runs = NextRuns(scheduler);
            Assert.True(runs.ContainsKey(task.Id));

            // Пользователь удалил задание из окна (как делает _store.Delete).
            store.Delete(task.Id);

            // После Refresh удалённый id исчезает из кэша — задание больше не выполняется.
            scheduler.Refresh();
            Assert.False(runs.ContainsKey(task.Id));
            Assert.Empty(runs);
        }
        finally
        {
            scheduler.Stop();
        }
    }

    [Fact]
    public void DisabledTask_HasNullNextRun_AfterRefresh()
    {
        var store = new FakeTaskStore();
        var task = NewTask("Выключенное задание");
        task.Enabled = false;
        store.Tasks.Add(task);

        using var scheduler = CreateScheduler(store);
        scheduler.Start();
        try
        {
            var runs = NextRuns(scheduler);

            // Выключенное задание присутствует в кэше, но с null-моментом:
            // ComputeNextRun для него возвращает null, ProcessDueAsync пропускает (issue #7).
            scheduler.Refresh();
            Assert.True(runs.ContainsKey(task.Id));
            Assert.Null(runs[task.Id]);
        }
        finally
        {
            scheduler.Stop();
        }
    }

    // ---- Вспомогательные ----

    private static ScheduledTask NewTask(
        string name,
        string time = "02:00",
        bool repeatEnabled = false,
        int repeatValue = 0,
        ScheduledTaskRepeatUnit repeatUnit = ScheduledTaskRepeatUnit.Minutes) => new()
    {
        Name = name,
        Kind = ScheduledTaskKind.UpdateApp,
        Time = time,
        RepeatEnabled = repeatEnabled,
        RepeatIntervalValue = repeatValue,
        RepeatIntervalUnit = repeatUnit
    };

    /// <summary>
    /// Читает приватный кэш ближайших моментов планировщика через рефлексию —
    /// единственный надёжный способ проверить расписание без ожидания тика таймера.
    /// </summary>
    private static Dictionary<string, DateTime?> NextRuns(SchedulerService scheduler)
    {
        var field = typeof(SchedulerService).GetField("_nextRuns", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Поле _nextRuns в SchedulerService не найдено");
        return (Dictionary<string, DateTime?>)field.GetValue(scheduler)!;
    }

    private static SchedulerService CreateScheduler(FakeTaskStore store) => new(
        store,
        new FakeScenarios(),
        new FakeRepository(),
        null!, // IBackupService: задания в этих тестах не выполняются
        null!, // IConfigUpdateService: —//—
        null!, // IInfobaseAdminService: —//—
        new FakeGitHubReleaseService(),
        new FakeAppUpdateService(),
        new FakeNotifications(),
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

        public void Save(ScheduledTask task)
        {
            if (!Tasks.Contains(task))
                Tasks.Add(task);
        }

        public void Delete(string id) => Tasks.RemoveAll(t => t.Id == id);
        public ScheduledTask? Get(string id) => Tasks.FirstOrDefault(t => t.Id == id);
    }

    /// <summary>Фейк GitHub-релизов: стабильный результат без сети.</summary>
    private sealed class FakeGitHubReleaseService : GitHubReleaseService
    {
        public override Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken ct = default)
            => Task.FromResult<ReleaseInfo?>(null);
    }

    /// <summary>Фейк подсистемы автообновления: только считает вызовы установки.</summary>
    private sealed class FakeAppUpdateService : IAppUpdateService
    {
        public int InstallCalls { get; private set; }

        public Task DownloadAndInstallAutoAsync(ReleaseInfo release)
        {
            InstallCalls++;
            return Task.CompletedTask;
        }
    }

    /// <summary>Фейк системных уведомлений — считает вызовы.</summary>
    private sealed class FakeNotifications : INotificationService
    {
        public int ShowCount { get; private set; }

        public void Show(string title, string message) => ShowCount++;

        public void Show(string title, string message, NotificationKind kind, NotificationEvent evt = NotificationEvent.ManualTest)
            => ShowCount++;
    }

    /// <summary>Фейк списка баз: задания в этих тестах его не используют.</summary>
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

    /// <summary>Фейк сценариев резервирования (задания в этих тестах их не используют).</summary>
    private sealed class FakeScenarios : IBackupScenarioStore
    {
        public string ScenariosDirectory => "";
        public IReadOnlyList<BackupScenario> LoadAll() => Array.Empty<BackupScenario>();
        public BackupScenario? Get(string id) => null;
        public void Save(BackupScenario scenario) { }
        public void Delete(string id) { }
    }

    /// <summary>Фейк каталога платформы (задания в этих тестах его не используют).</summary>
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

    /// <summary>Фейк хранилища типовых конфигураций (задания в этих тестах его не используют).</summary>
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