using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Workspace1C.Services;

/// <summary>
/// Кэш операционных статусов служб (issue #11, 1.0.0.14): мгновенное чтение статуса
/// для построения меню трея и «СЛУЖБЫ» без блокирующего опроса контроллера на
/// UI-потоке. До этой версии меню строилось синхронно с вызовом
/// <c>ServiceController.GetStatus</c> по каждой службе — задержка Ctrl+клика в трее
/// составляла около секунды. Теперь чтение — из памяти, обновление — фоновой задачей
/// (<see cref="RefreshAsync"/>), после операций кэш сбрасывается (<see cref="Invalidate"/>).
/// Потокобезопасен. Чистый .NET — обе платформы (WPF и Avalonia).
/// </summary>
public sealed class ServiceStatusCache
{
    private readonly IServiceControllerService _services;
    private readonly ConcurrentDictionary<string, ServiceInfo> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="services">Платформенный контроллер служб (источник свежих статусов).</param>
    public ServiceStatusCache(IServiceControllerService services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>Мгновенный статус из кэша (Unknown при отсутствии записи).</summary>
    public ServiceInfo GetStatus(string serviceName)
    {
        if (_cache.TryGetValue(serviceName, out var info))
            return info.Clone();
        return new ServiceInfo { ServiceName = serviceName, DisplayName = serviceName, Status = ServiceStatus.Unknown };
    }

    /// <summary>Обновляет статусы перечисленных служб в фоновом рабочем потоке
    /// (блокирующие вызовы контроллера не попадают на UI-поток).</summary>
    public Task RefreshAsync(IReadOnlyCollection<string> names, CancellationToken token = default)
    {
        return Task.Run(() =>
        {
            foreach (var name in names)
            {
                if (token.IsCancellationRequested)
                    return;
                try
                {
                    _cache[name] = _services.GetStatus(name);
                }
                catch
                {
                    // Статус не опрошен — запись не держим, следующий опрос повторит.
                }
            }
        }, token);
    }

    /// <summary>Полностью сбрасывает записи (после операции со службами статусы
    /// могли измениться — следующий RefreshAsync прочитает их заново).</summary>
    public void Invalidate() => _cache.Clear();
}