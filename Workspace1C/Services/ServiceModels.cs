using System.Collections.Generic;
using System.Linq;

namespace Workspace1C.Services;

/// <summary>
/// Операционный статус службы (Windows: <c>ServiceControllerStatus</c>;
/// Linux: состояние unit'а systemd). Промежуточные состояния «перезапуска/паузы»
/// отображаются на <see cref="Starting"/>/<see cref="Stopping"/> — в этих состояниях
/// все команды недоступны (см. <see cref="ServiceActionAvailability"/>).
/// </summary>
public enum ServiceStatus
{
    /// <summary>Статус неизвестен (например, не удалось опросить службу).</summary>
    Unknown,

    /// <summary>Запущена.</summary>
    Running,

    /// <summary>Остановлена.</summary>
    Stopped,

    /// <summary>Запускается (StartPending / activating).</summary>
    Starting,

    /// <summary>Останавливается (StopPending / deactivating).</summary>
    Stopping,

    /// <summary>На паузе (Paused; только Windows, systemd паузу не поддерживает).</summary>
    Paused,

    /// <summary>Служба с таким именем не найдена в системе.</summary>
    NotFound
}

/// <summary>
/// Операция над службой из окна «Управление службами» (issue #11).
/// </summary>
public enum ServiceAction
{
    Start,
    Stop,
    Pause,
    Continue,
    Restart
}

/// <summary>
/// Платформа для выбора «мапы» доступности команд (issue #11, раздел 3.1 плана):
/// на Linux <c>systemctl restart</c> допустим и для остановленных unit'ов, поэтому
/// «Перезапустить» разрешён и для Stopped; на Windows — только для Running.
/// </summary>
public enum ServicePlatform
{
    Windows,
    Linux
}

/// <summary>
/// Лёгкое представление службы (то, что показываем в списке «Управление службами»).
/// Windows: <c>ServiceName</c> — имя службы, <c>DisplayName</c> — отображаемое имя.
/// Linux: <c>ServiceName</c> — имя unit'а (<c>*.service</c>), <c>DisplayName</c> — описание.
/// </summary>
public sealed class ServiceInfo
{
    /// <summary>Windows: ServiceName; Linux: имя unit'а (например <c>postgresql.service</c>).</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Windows: DisplayName; Linux: Description/имя.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Операционный статус.</summary>
    public ServiceStatus Status { get; set; }

    /// <summary>Поддерживает ли служба паузу и продолжение (Windows: код 0x102).
    /// На Linux всегда false — systemd не умеет pause/continue для обычных служб.</summary>
    public bool CanPauseAndContinue { get; set; }

    /// <summary>Можно ли остановить службу (Windows: <c>CanStop</c>; Linux: true).</summary>
    public bool CanStop { get; set; } = true;

    /// <summary>Копия для безопасного переноса между потоками.</summary>
    public ServiceInfo Clone() => new()
    {
        ServiceName = ServiceName,
        DisplayName = DisplayName,
        Status = Status,
        CanPauseAndContinue = CanPauseAndContinue,
        CanStop = CanStop
    };
}

/// <summary>
/// Чистый вычислитель доступности команд служб (issue #11, таблица истинности из 3.1):
/// каждая операция допустима только при определённом статусе и возможностях службы.
/// Тестируется исчерпывающей таблицей (<see cref="Workspace1C.Tests.ServiceActionAvailabilityTests"/>).
/// </summary>
public static class ServiceActionAvailability
{
    /// <summary>
    /// Доступна ли операция <paramref name="action"/> для службы с указанным статусом
    /// и возможностями (платформозависимое правило перезапуска остановленной службы).
    /// </summary>
    public static bool CanExecute(
        ServiceAction action,
        ServiceStatus status,
        bool canPauseAndContinue,
        bool canStop,
        ServicePlatform platform)
    {
        switch (action)
        {
            case ServiceAction.Start:
                return status == ServiceStatus.Stopped;

            case ServiceAction.Stop:
                return (status == ServiceStatus.Running || status == ServiceStatus.Paused) && canStop;

            case ServiceAction.Pause:
                return status == ServiceStatus.Running && canPauseAndContinue;

            case ServiceAction.Continue:
                return status == ServiceStatus.Paused && canPauseAndContinue;

            case ServiceAction.Restart:
                // Windows: System.ServiceProcess.ServiceController.Restart() требует Running.
                // Linux: systemctl restart сам запускает остановленный unit, поэтому остановка
                // не мешает — «Перезапустить» допустим и для Stopped (раздел 3.1 плана).
                if (status == ServiceStatus.Running)
                    return true;
                return platform == ServicePlatform.Linux && status == ServiceStatus.Stopped;

            default:
                return false;
        }
    }

    /// <summary>Доступна ли операция хотя бы для одной службы набора (подменю «Все сервисы»).</summary>
    public static bool CanExecuteAny(
        IEnumerable<ServiceInfo> services,
        ServiceAction action,
        ServicePlatform platform)
    {
        if (services is null)
            return false;
        return services.Any(s => CanExecute(action, s.Status, s.CanPauseAndContinue, s.CanStop, platform));
    }
}