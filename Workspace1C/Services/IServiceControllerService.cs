using System.Collections.Generic;

namespace Workspace1C.Services;

/// <summary>
/// Управление системными службами (issue #11) — единый интерфейс для обеих платформ.
/// Windows: <c>System.ServiceProcess.ServiceController</c> + системный диалог
/// <c>services.msc</c> (<see cref="ServiceControllerService.Windows"/>); Linux —
/// команды <c>systemctl</c> (<see cref="ServiceControllerService.Linux"/>), системный
/// диалог недоступен (<see cref="CanOpenSystemDialog"/> == false), пауза/восстановление
/// не поддерживаются (у всех служб <see cref="ServiceInfo.CanPauseAndContinue"/> == false).
/// </summary>
public interface IServiceControllerService
{
    /// <summary>
    /// Доступно ли управление службами на этой системе. Windows: всегда true.
    /// Linux: true, если найден <c>systemctl</c> и systemd отвечает
    /// (<c>systemctl is-system-running</c>).
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Все доступные службы системы (Windows: ServiceController.GetServices(); Linux: list-units).</summary>
    IReadOnlyList<ServiceInfo> GetAllServices();

    /// <summary>Статус отдельной службы; служба не найдена → <see cref="ServiceStatus.NotFound"/>.</summary>
    ServiceInfo GetStatus(string serviceName);

    /// <summary>Есть ли системный диалог управления службами (Windows: true; Linux: false).</summary>
    bool CanOpenSystemDialog { get; }

    /// <summary>Открывает системный диалог (Windows: services.msc; Linux: не вызывается).</summary>
    void OpenSystemServicesDialog();

    /// <summary>Запускает службу; true при успехе (блокирующая — вызывать из фона).</summary>
    bool Start(string serviceName);

    /// <summary>Останавливает службу; true при успехе (блокирующая — вызывать из фона).</summary>
    bool Stop(string serviceName);

    /// <summary>Ставит службу на паузу; true при успехе (blockирующая; Linux — false).</summary>
    bool Pause(string serviceName);

    /// <summary>Продолжает службу после паузы; true при успехе (блокирующая; Linux — false).</summary>
    bool Continue(string serviceName);

    /// <summary>Перезапускает службу; true при успехе (блокирующая — вызывать из фона).</summary>
    bool Restart(string serviceName);

    /// <summary>
    /// Можно ли выдать текущему пользователю права на управление службами одним запросом
    /// повышения прав (issue #11, 7OH 17.10). Windows: true; Linux/заглушки: false.
    /// </summary>
    bool CanGrantControlRights => false;

    /// <summary>
    /// Выдаёт текущему пользователю права на запуск/остановку/паузу перечисленных служб
    /// (Windows: SDDL через sc sdshow/sdset, один запрос UAC). Блокирующая — вызывать из фона.
    /// </summary>
    ServiceRightsResult GrantControlRights(IReadOnlyList<string> serviceNames) => ServiceRightsResult.Unsupported();
}