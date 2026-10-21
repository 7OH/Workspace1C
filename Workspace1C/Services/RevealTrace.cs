using System;
using System.IO;
using System.Text;

namespace Workspace1C.Services;

/// <summary>
/// Отладочный журнал «перехода к строке» (issue #31): при включённом флаге
/// <c>CM_REVEAL</c> в конфиге <c>trace.json</c> подробные диагностические записи
/// reveal (цель: id/IsPinned, revealToHome, домашний узел — маркер/путь или null,
/// результаты поиска контейнера: найдено/поддерево/предки, attempt, исход попыток)
/// пишутся в файл <c>reveal-debug.log</c> в каталоге логов
/// (<see cref="PlatformPaths.LogDirectory"/> — AppDataDirectory/logs).
/// <para>
/// Гейт — тот же, что у CM_TREESWITCH/CM_UPDATES (<see cref="TraceFlags.IsEnabled"/>):
/// запись выполняется только при включённом флаге, конфиг перечитывается при изменении
/// — включение флага работает без перезапуска приложения. Статический доступ — чтобы
/// логировать из окон WPF (<see cref="Workspace1C.MainWindow"/>) и Avalonia без
/// инжектируемого логгера. Сбой записи не роняет приложение — диагностика молча
/// пропускается.
/// </para>
/// </summary>
public static class RevealTrace
{
    private static readonly object Sync = new();

    /// <summary>Пишет строку в журнал reveal (только при включённом флаге <c>CM_REVEAL</c>).</summary>
    public static void Log(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        try
        {
            if (!TraceFlags.IsEnabled(TraceFlags.RevealFlag))
                return;

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
            lock (Sync)
            {
                var directory = PlatformPaths.LogDirectory;
                Directory.CreateDirectory(directory);
                File.AppendAllText(
                    Path.Combine(directory, "reveal-debug.log"),
                    line,
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Диагностика не должна ронять приложение.
        }
    }
}