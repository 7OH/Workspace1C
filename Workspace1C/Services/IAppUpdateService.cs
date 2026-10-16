using System.Threading.Tasks;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Подсистема автообновления приложения: скачивание и применение новой версии,
/// которую использует планировщик заданий для <see cref="ScheduledTaskKind.UpdateApp"/>.
/// Выделена в интерфейс, чтобы <see cref="SchedulerService"/> оставался тестируемым
/// (фейк в Workspace1C.Tests); платформенные реализации — WPF и Avalonia
/// (<see cref="UpdateService"/>, общий тип с разной реализацией по #if WINDOWS/#if LINUX).
/// </summary>
public interface IAppUpdateService
{
    /// <summary>
    /// Скачивает новый исполняемый файл и применяет обновление (при необходимости
    /// перезапускает приложение). Ошибки скачивания/применения обрабатываются внутри
    /// реализацией и наружу не выбрасываются.
    /// </summary>
    Task DownloadAndInstallAutoAsync(ReleaseInfo release);
}