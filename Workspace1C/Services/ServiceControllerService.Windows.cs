#if WINDOWS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;

namespace Workspace1C.Services;

/// <summary>
/// Управление системными службами Windows (issue #11): список/статусы через
/// <c>System.ServiceProcess.ServiceController</c>, системный диалог — <c>services.msc</c>.
/// Ошибки прав (UnauthorizedAccessException/Win32Exception) и сбои отдельных служб
/// не роняют приложение: команды возвращают false, список — без проблемных записей.
/// </summary>
public sealed class ServiceControllerService : IServiceControllerService
{
    /// <summary>Таймаут ожидания смены статуса службы (WaitForStatus).</summary>
    private static readonly TimeSpan StatusWaitTimeout = TimeSpan.FromSeconds(15);

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public bool CanOpenSystemDialog => true;

    /// <inheritdoc />
    public IReadOnlyList<ServiceInfo> GetAllServices()
    {
        var result = new List<ServiceInfo>();
        try
        {
            foreach (var controller in ServiceController.GetServices())
            {
                var info = Map(controller);
                if (info is not null)
                    result.Add(info);
            }
        }
        catch
        {
            // Список служб недоступен (нет прав/сервис управления отключён) — пустой список.
        }
        return result;
    }

    /// <inheritdoc />
    public ServiceInfo GetStatus(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return NotFound(serviceName ?? string.Empty);
        try
        {
            using var controller = new ServiceController(serviceName);
            return Map(controller) ?? NotFound(serviceName);
        }
        catch
        {
            // Служба не найдена или доступ к ней запрещён — статус «Не найдена».
            return NotFound(serviceName);
        }
    }

    /// <inheritdoc />
    public void OpenSystemServicesDialog()
    {
        try
        {
            // services.msc — управляющий элемент консоли, запускается как в
            // InfobaseAdminService.OpenServerAdminConsole (UseShellExecute=true).
            Process.Start(new ProcessStartInfo { FileName = "services.msc", UseShellExecute = true });
        }
        catch
        {
            // Не удалось открыть системный диалог — молча пропускаем (операция вспомогательная).
        }
    }

    /// <inheritdoc />
    public bool CanGrantControlRights => true;

    /// <inheritdoc />
    public ServiceRightsResult GrantControlRights(IReadOnlyList<string> serviceNames)
    {
        var result = new ServiceRightsResult();
        string? sid;
        try
        {
            sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
        if (string.IsNullOrWhiteSpace(sid))
        {
            result.Error = "SID текущего пользователя не определён.";
            return result;
        }

        // 1) Текущие дескрипторы читаются без повышения прав; новые — с записью для SID.
        var changes = new List<(string Name, string Sddl)>();
        foreach (var name in serviceNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!ServiceRights.IsSafeName(name))
            {
                result.Failed[name] = "недопустимые символы в имени службы";
                continue;
            }
            var output = RunHidden("sc.exe", $"sdshow \"{name}\"");
            var sddl = ServiceRights.ParseSdShowOutput(output);
            if (sddl is null)
            {
                result.Failed[name] = "не удалось прочитать дескриптор безопасности";
                continue;
            }
            var updated = ServiceRights.AddControlAce(sddl, sid);
            if (updated is null)
                result.Failed[name] = "дескриптор без DACL";
            else if (string.Equals(updated, sddl, StringComparison.Ordinal))
                result.AlreadyGranted.Add(name);
            else
                changes.Add((name, updated));
        }
        if (changes.Count == 0)
            return result;

        // 2) Один командный файл sc sdset для всех служб — один запрос UAC.
        var dir = Path.Combine(Path.GetTempPath(), "Workspace1C-service-rights-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var resultFile = Path.Combine(dir, "result.txt");
            var batchFile = Path.Combine(dir, "grant.cmd");
            File.WriteAllText(batchFile, ServiceRights.BuildBatch(changes, resultFile), System.Text.Encoding.ASCII);

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batchFile}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            });
            if (process is null)
            {
                result.Error = "Не удалось запустить cmd.exe с повышением прав.";
                return result;
            }
            if (!process.WaitForExit(120_000))
            {
                result.Error = "Превышено время ожидания выдачи прав.";
                return result;
            }
            var codes = ServiceRights.ParseResultFile(File.Exists(resultFile) ? File.ReadAllText(resultFile) : null);
            ServiceRights.ApplyResults(result, changes.Select(c => c.Name), codes);
        }
        catch (System.ComponentModel.Win32Exception wex) when (wex.NativeErrorCode == 1223)
        {
            result.Canceled = true; // отмена запроса UAC
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* временные файлы */ }
        }
        return result;
    }

    /// <summary>Запускает консольную утилиту без окна и возвращает stdout (пусто при ошибке).</summary>
    private static string RunHidden(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null)
                return string.Empty;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(15_000);
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <inheritdoc />
    public bool Start(string serviceName)
        => RunCommand(serviceName,
            s => s.Start(),
            s => s.WaitForStatus(ServiceControllerStatus.Running, StatusWaitTimeout));

    /// <inheritdoc />
    public bool Stop(string serviceName)
        => RunCommand(serviceName,
            s => s.Stop(),
            s => s.WaitForStatus(ServiceControllerStatus.Stopped, StatusWaitTimeout));

    /// <inheritdoc />
    public bool Pause(string serviceName)
        => RunCommand(serviceName,
            s => s.Pause(),
            s => s.WaitForStatus(ServiceControllerStatus.Paused, StatusWaitTimeout));

    /// <inheritdoc />
    public bool Continue(string serviceName)
        => RunCommand(serviceName,
            s => s.Continue(),
            s => s.WaitForStatus(ServiceControllerStatus.Running, StatusWaitTimeout));

    /// <inheritdoc />
    public bool Restart(string serviceName)
    {
        try
        {
            using var service = new ServiceController(serviceName);
            if (service.Status == ServiceControllerStatus.Running)
            {
                // Перезапуск делаем последовательностью Stop/Start с явными таймаутами
                // (аналог ServiceController.Restart без неявного времени ожидания).
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, StatusWaitTimeout);
            }
            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, StatusWaitTimeout);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Выполняет команду над службой и ждёт целевой статус (при сбое — false).</summary>
    private static bool RunCommand(
        string serviceName,
        Action<ServiceController> command,
        Action<ServiceController>? wait)
    {
        try
        {
            using var service = new ServiceController(serviceName);
            command(service);
            wait?.Invoke(service);
            return true;
        }
        catch
        {
            // UnauthorizedAccessException / Win32Exception и др. — команда не выполнена.
            return false;
        }
    }

    /// <summary>Переносит свойства контроллера службы в модель (null — доступ запрещён).</summary>
    private static ServiceInfo? Map(ServiceController controller)
    {
        try
        {
            var statusRaw = controller.Status;
            var canPause = false;
            var canStop = false;
            try { canPause = controller.CanPauseAndContinue; }
            catch { /* нет прав на чтение возможностей — считаем false */ }
            try
            {
                // Остановить можно только запущенную/паузную службу (логика таблицы 3.1).
                canStop = (statusRaw is ServiceControllerStatus.Running or ServiceControllerStatus.Paused)
                          && controller.CanStop;
            }
            catch { /* нет прав — считаем false */ }

            var name = controller.ServiceName ?? string.Empty;
            if (name.Length == 0)
                return null;

            return new ServiceInfo
            {
                ServiceName = name,
                DisplayName = string.IsNullOrWhiteSpace(controller.DisplayName) ? name : controller.DisplayName,
                Status = MapStatus(statusRaw),
                CanPauseAndContinue = canPause,
                CanStop = canStop
            };
        }
        catch
        {
            return null;
        }
    }

    private static ServiceStatus MapStatus(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Running => ServiceStatus.Running,
        ServiceControllerStatus.Stopped => ServiceStatus.Stopped,
        ServiceControllerStatus.Paused => ServiceStatus.Paused,
        ServiceControllerStatus.StartPending => ServiceStatus.Starting,
        ServiceControllerStatus.StopPending => ServiceStatus.Stopping,
        // Переходные состояния паузы/продолжения — «нестабильные»: все команды недоступны.
        ServiceControllerStatus.PausePending => ServiceStatus.Starting,
        ServiceControllerStatus.ContinuePending => ServiceStatus.Stopping,
        _ => ServiceStatus.Unknown
    };

    private static ServiceInfo NotFound(string serviceName) => new()
    {
        ServiceName = serviceName,
        DisplayName = serviceName,
        Status = ServiceStatus.NotFound
    };
}
#endif