using System;
using Workspace1C.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Workspace1C.Services;

/// <summary>
/// CLI-команды Telegram-моста для рабочего процесса разработки (выпуск 1.0.0.36+):
/// <c>--telegram-send</c> — уведомление о прогрессе (fire-and-forget), и
/// <c>--telegram-confirm</c> — запрос подтверждения через inline-кнопки с ожиданием
/// ответа (длинный поллинг в текущем процессе). Предназначены для вызова из агентов
/// (Roo Code), build-скриптов и ручного запуска из терминала.
/// </summary>
internal static class CliTelegramCommands
{
    /// <summary>Таймаут ожидания подтверждения по умолчанию (секунд), если --timeout не задан.</summary>
    public const int DefaultTimeoutSeconds = 300;

    /// <summary>
    /// <c>--telegram-send</c>: отправляет dev-уведомление в Telegram (в обход фильтра
    /// «какие события отправлять» — пользователь явно просил уведомить).
    /// </summary>
    public static int Send(string command, CliOptions options)
    {
        var channel = AppServices.GetRequiredService<TelegramNotificationChannel>();
        var message = new NotificationMessage
        {
            Title = string.IsNullOrWhiteSpace(options.Title) ? "Workspace 1C" : options.Title.Trim(),
            Message = (options.Text ?? string.Empty).Trim(),
            Kind = ParseKind(options.Kind),
            // ManualTest не фильтруется внешними каналами; здесь он означает «ручной
            // вызов из рабочего процесса разработки», а не «проверить подключение».
            Event = NotificationEvent.ManualTest,
        };

        var (ok, error) = channel.SendDevAsync(message).GetAwaiter().GetResult();
        if (ok)
        {
            if (options.Json)
                CliOutput.WriteResult(CliResult.Success(command, new { sent = true }), json: true);
            return CliExitCodes.Success;
        }

        CliOutput.WriteResult(
            CliResult.Failure(command, "send_failed", DescribeError(error, "Не удалось отправить уведомление в Telegram")),
            options.Json);
        return CliExitCodes.Error;
    }

    /// <summary>
    /// <c>--telegram-confirm</c>: отправляет сообщение с кнопками «Да»/«Нет» и ждёт ответа.
    /// Коды выхода: 0 — «Да», <see cref="CliExitCodes.Denied"/> — «Нет»,
    /// <see cref="CliExitCodes.Timeout"/> — время ожидания истекло, 1 — ошибка/занято/не настроено.
    /// В <c>--json</c> вывод — единый контракт с data.result: <c>yes|no|timeout</c>.
    /// </summary>
    public static int Confirm(string command, CliOptions options)
    {
        var service = AppServices.GetRequiredService<TelegramConfirmationService>();
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : DefaultTimeoutSeconds);

        var (ok, error, result) = service.ConfirmAsync(options.Prompt, options.Buttons, timeout).GetAwaiter().GetResult();
        if (!ok)
        {
            var code = error switch
            {
                "busy" => "busy",
                "not_configured" => "not_configured",
                _ => "confirm_failed"
            };
            CliOutput.WriteResult(
                CliResult.Failure(command, code, DescribeError(error, "Не удалось запросить подтверждение в Telegram")),
                options.Json);
            return CliExitCodes.Error;
        }

        if (options.Json)
            CliOutput.WriteResult(CliResult.Success(command, new { result = result!.MachineName }), json: true);
        else
            Console.WriteLine(result!.MachineName);

        return result.Outcome switch
        {
            TelegramConfirmationOutcome.Yes => CliExitCodes.Success,
            TelegramConfirmationOutcome.No => CliExitCodes.Denied,
            _ => CliExitCodes.Timeout
        };
    }

    /// <summary>Вид уведомления из --kind (допускаются нечувствительные к регистру значения).</summary>
    private static NotificationKind ParseKind(string? kind) => (kind ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "success" => NotificationKind.Success,
        "warning" or "warn" => NotificationKind.Warning,
        "error" => NotificationKind.Error,
        _ => NotificationKind.Info
    };

    private static string DescribeError(string? code, string fallback)
    {
        if (code == "not_configured")
            return "Telegram не настроен: заполните токен бота и ID чата в окне «Уведомления: Telegram и email».";
        if (code == "busy")
            return "Уже выполняется запрос подтверждения; дождитесь его завершения и повторите.";
        return string.IsNullOrWhiteSpace(code) ? fallback : code;
    }
}
