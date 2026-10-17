#if LINUX
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Workspace1C.Services;

/// <summary>
/// Управление системными службами Linux (issue #11) поверх <c>systemctl</c>.
/// Список — <c>systemctl list-units --type=service --all</c>, статус из колонок
/// ACTIVE/SUB; команды — <c>start/stop/restart</c>. Пауза/продолжение НЕ поддерживаются
/// (systemd не имеет аналога SERVICE_PAUSE_CONTINUE) — у всех служб
/// <see cref="ServiceInfo.CanPauseAndContinue"/> == false, кнопки «Пауза/Восстановить»
/// неактивны. Если systemd недоступен (<c>systemctl is-system-running</c> не отвечает),
/// <see cref="IsAvailable"/> == false, окно показывает подсказку и блокирует действия.
/// Системного диалога (<c>services.msc</c>) на Linux нет — <see cref="CanOpenSystemDialog"/> == false.
/// </summary>
public sealed class ServiceControllerService : IServiceControllerService
{
    /// <summary>Таймаут выполнения команды systemctl, миллисекунды (~15 с).</summary>
    private const int CommandTimeoutMs = 15000;

    /// <inheritdoc />
    public bool IsAvailable => RunSystemCtl("is-system-running --no-pager") is not null;

    /// <inheritdoc />
    public bool CanOpenSystemDialog => false;

    /// <inheritdoc />
    public void OpenSystemServicesDialog()
    {
        // На Linux системного диалога служб нет; кнопка не показывается (CanOpenSystemDialog == false).
    }

    /// <inheritdoc />
    public IReadOnlyList<ServiceInfo> GetAllServices()
    {
        var result = new List<ServiceInfo>();
        // --plain — без «цветных» маркеров, -p/--no-pager — без пейджера.
        var output = RunSystemCtl("list-units --type=service --all --no-legend --no-pager --plain");
        if (string.IsNullOrWhiteSpace(output))
            return result;

        foreach (var line in output.Split('\n'))
        {
            var parsed = ParseUnitLine(line.TrimEnd('\r'));
            if (parsed is null)
                continue;
            var (unit, active, description) = parsed.Value;
            result.Add(new ServiceInfo
            {
                ServiceName = unit,
                DisplayName = string.IsNullOrWhiteSpace(description) ? unit : description,
                Status = MapActiveState(active),
                CanPauseAndContinue = false,
                CanStop = true
            });
        }
        return result;
    }

    /// <inheritdoc />
    public ServiceInfo GetStatus(string serviceName)
    {
        var unit = NormalizeUnit(serviceName);
        if (unit.Length == 0)
            return NotFound(serviceName);

        var active = RunSystemCtl("is-active " + Quote(unit));
        if (active is null)
            return NotFound(serviceName);

        return new ServiceInfo
        {
            ServiceName = unit,
            DisplayName = unit,
            Status = MapActiveState(active.Trim()),
            CanPauseAndContinue = false,
            CanStop = true
        };
    }

    /// <inheritdoc />
    public bool Start(string serviceName) => RunSystemCtl("start " + Quote(NormalizeUnit(serviceName))) is not null;

    /// <inheritdoc />
    public bool Stop(string serviceName) => RunSystemCtl("stop " + Quote(NormalizeUnit(serviceName))) is not null;

    /// <inheritdoc />
    public bool Continue(string serviceName) => false;

    /// <inheritdoc />
    public bool Pause(string serviceName) => false;

    /// <inheritdoc />
    public bool Restart(string serviceName) => RunSystemCtl("restart " + Quote(NormalizeUnit(serviceName))) is not null;

    // ===================== Вспомогательное =====================

    /// <summary>Дополняет имя unit'а суффиксом .service, если его нет.</summary>
    private static string NormalizeUnit(string serviceName)
    {
        var name = (serviceName ?? string.Empty).Trim();
        if (name.Length == 0)
            return string.Empty;
        return name.EndsWith(".service", StringComparison.Ordinal) ? name : name + ".service";
    }

    /// <summary>Экранирует аргумент для командной строки (пробелы/кавычки).</summary>
    private static string Quote(string value)
    {
        if (value.Length == 0)
            return "\"\"";
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    /// <summary>
    /// Запускает systemctl с аргументами и возвращает stdout при нулевом коде выхода
    /// (null — недоступен/ошибка/таймаут). Процесс с захватом stdout/stderr и
    /// «тихим» окном, таймаут <see cref="CommandTimeoutMs"/> мс (паттерн RacClient).
    /// </summary>
    private static string? RunSystemCtl(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("systemctl", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            if (process is null)
                return null;

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(CommandTimeoutMs))
            {
                try { process.Kill(); } catch { /* процесс мог завершиться */ }
                return null;
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            _ = stderrTask.GetAwaiter().GetResult(); // прочитали, чтобы канал не заблокировал процесс
            return process.ExitCode == 0 ? stdout : null;
        }
        catch
        {
            // systemctl не найден / нет прав на запуск — считаем недоступным.
            return null;
        }
    }

    /// <summary>
    /// Разбирает строку вывода <c>list-units --plain</c>:
    /// UNIT LOAD ACTIVE SUB DESCRIPTION (описание может содержать пробелы).
    /// </summary>
    private static (string Unit, string Active, string Description)? ParseUnitLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var parts = line.Split((char[]?)null, 4, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            return null;

        var unit = parts[0].Trim();
        if (!unit.EndsWith(".service", StringComparison.Ordinal))
            return null; // интересуют только службы (не sockets/timers/...)

        var active = parts[2].Trim();
        var description = parts.Length >= 4 ? parts[3].Trim() : string.Empty;
        return (unit, active, description);
    }

    /// <summary>Маппинг состояния ACTIVE (или вывода is-active) на операционный статус.</summary>
    private static ServiceStatus MapActiveState(string active)
    {
        switch (active.Trim().ToLowerInvariant())
        {
            case "active":
                return ServiceStatus.Running;
            case "activating":
                return ServiceStatus.Starting;
            case "deactivating":
                return ServiceStatus.Stopping;
            case "inactive":
            case "failed":
                return ServiceStatus.Stopped;
            default:
                return ServiceStatus.Unknown;
        }
    }

    private static ServiceInfo NotFound(string serviceName) => new()
    {
        ServiceName = serviceName,
        DisplayName = serviceName,
        Status = ServiceStatus.NotFound,
        CanPauseAndContinue = false,
        CanStop = false
    };
}
#endif