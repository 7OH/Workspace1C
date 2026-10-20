using System;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Реализация <see cref="ISessionLockService"/>. Установка/снятие блокировки сеансов
/// файловой ИБ выполняется пакетным запуском конфигуратора (<see cref="OneCLauncher.RunDesignerBatch"/>
/// с операциями <c>LockIB</c>/<c>UnlockIB</c>) и ожиданием его завершения через событие
/// <see cref="OneCLauncher.DesignerBatchCompleted"/> (TaskCompletionSource с таймаутом),
/// как и другие пакетные операции конфигуратора.
/// </summary>
public class SessionLockService : ISessionLockService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    private readonly IAppLogger? _logger;
    private readonly FileBaseSessionLock _fileBaseLock;

    public SessionLockService(IAppLogger? logger = null, FileBaseSessionLock? fileBaseLock = null)
    {
        _logger = logger;
        _fileBaseLock = fileBaseLock ?? new FileBaseSessionLock();
    }

    /// <inheritdoc />
    public async Task<SessionLockResult> LockAsync(Infobase infobase, SessionLockOptions options)
    {
        if (infobase is null)
            return SessionLockResult.Fail(LocalizationManager.T("SessionLock.NoBase"));

        if (options is null)
            return SessionLockResult.Fail(LocalizationManager.T("SessionLock.Failed"));

        // Файловая ИБ (issue #20): блокировка через файл 1cv8.cdn в каталоге базы —
        // без запуска конфигуратора. Клиент-серверная — прежний /LockIB конфигуратора.
        if (IsFileBase(infobase))
            return _fileBaseLock.Lock(infobase, options);

        var lockString = options.BuildSessionLockString();

        var started = OneCLauncher.RunDesignerBatch(infobase, OneCLauncher.DesignerBatchOperation.LockIB, lockString);
        if (!started)
            return SessionLockResult.Fail(LocalizationManager.T("SessionLock.StartFailed"));

        return await WaitForCompletionAsync(OneCLauncher.DesignerBatchOperation.LockIB, lockString);
    }

    /// <inheritdoc />
    public async Task<SessionLockResult> UnlockAsync(Infobase infobase)
    {
        if (infobase is null)
            return SessionLockResult.Fail(LocalizationManager.T("SessionLock.NoBase"));

        if (IsFileBase(infobase))
            return _fileBaseLock.Unlock(infobase);

        var started = OneCLauncher.RunDesignerBatch(infobase, OneCLauncher.DesignerBatchOperation.UnlockIB, string.Empty);
        if (!started)
            return SessionLockResult.Fail(LocalizationManager.T("SessionLock.StartFailed"));

        return await WaitForCompletionAsync(OneCLauncher.DesignerBatchOperation.UnlockIB, string.Empty);
    }

    /// <summary>
    /// Файловая ли ИБ: тип подключения <see cref="ConnectionType.File"/> (путь к каталогу
    /// базы — в <see cref="ConnectionSettings.FilePath"/>). Для таких баз блокировка
    /// выполняется файлом 1cv8.cdn (issue #20).
    /// </summary>
    public static bool IsFileBase(Infobase infobase)
        => infobase?.Connection?.Type == ConnectionType.File;

    /// <summary>
    /// Ожидает завершения операции блокировки через событие
    /// <see cref="OneCLauncher.DesignerBatchCompleted"/> с таймаутом.
    /// </summary>
    private async Task<SessionLockResult> WaitForCompletionAsync(
        OneCLauncher.DesignerBatchOperation operation, string token)
    {
        using var cts = new CancellationTokenSource(DefaultTimeout);
        var tcs = new TaskCompletionSource<SessionLockResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<OneCLauncher.DesignerBatchInfo> handler = (_, info) =>
        {
            if (info.Operation == operation &&
                string.Equals(info.OutputPath ?? string.Empty, token ?? string.Empty, StringComparison.Ordinal))
            {
                tcs.TrySetResult(info.Success
                    ? SessionLockResult.Ok()
                    : SessionLockResult.Fail(info.ErrorMessage ?? LocalizationManager.T("SessionLock.Failed")));
            }
        };
        OneCLauncher.DesignerBatchCompleted += handler;
        try
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token));
            return completed == tcs.Task
                ? tcs.Task.Result
                : SessionLockResult.Fail(LocalizationManager.T("SessionLock.Timeout"));
        }
        finally
        {
            OneCLauncher.DesignerBatchCompleted -= handler;
        }
    }
}