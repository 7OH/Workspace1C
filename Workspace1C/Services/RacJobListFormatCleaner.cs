using System;
using System.IO;

namespace Workspace1C.Services;

/// <summary>
/// Одноразовая чистка исторического файла <c>rac_joblist_formats.json</c> (issue #39).
/// С версии 1.0.0.40 кэш рабочих форматов передачи идентификатора кластера rac хранится
/// только в памяти (<see cref="RacClient"/>._clusterFormats) и на диск не пишется; файл,
/// созданный прежними версиями (бывший <c>RacJobListFormatStore</c>), удаляется при первом
/// старте новой версии. Удаление — best effort: ошибки игнорируются, отсутствующий файл
/// не считается ошибкой — «идиот-файл» не должен мешать запуску приложения.
/// </summary>
public static class RacJobListFormatCleaner
{
    /// <summary>Имя удаляемого файла (историческое, issue #39).</summary>
    public const string LegacyFileName = "rac_joblist_formats.json";

    /// <summary>
    /// Удаляет <c>rac_joblist_formats.json</c> из каталога данных. Каталог задаётся
    /// параметром <paramref name="dataDirectory"/> (обычно
    /// <see cref="PlatformPaths.AppDataDirectory"/>); отдельный путь можно передать
    /// для юнит-тестов. Любые исключения и отсутствие файла молча игнорируются.
    /// </summary>
    /// <param name="dataDirectory">Каталог данных приложения; по умолчанию
    /// <see cref="PlatformPaths.AppDataDirectory"/>.</param>
    public static void Clean(string? dataDirectory = null)
    {
        try
        {
            var directory = dataDirectory ?? PlatformPaths.AppDataDirectory;
            if (string.IsNullOrWhiteSpace(directory))
                return;

            var path = Path.Combine(directory, LegacyFileName);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Чистка — вспомогательная операция: при неудаче продолжаем без файла.
        }
    }
}