using System.IO;

namespace Workspace1C.Services;

/// <summary>
/// Кроссплатформенные пути к каталогам данных приложения.
/// Windows: %APPDATA%\Workspace1C (Environment.SpecialFolder.ApplicationData).
/// Linux:   ~/.config/Workspace1C (XDG_CONFIG_HOME или ~/.config).
/// Единая точка расчёта каталога данных и каталога логов, чтобы все сервисы
/// (InfobaseRepository, FileAppLogger и др.) писали в одно место.
/// </summary>
public static class PlatformPaths
{
    /// <summary>
    /// Каталог данных приложения (infobases.json, groups.json, settings.json).
    /// В портативном режиме (<see cref="PortablePaths.IsPortable"/>) возвращается каталог
    /// рядом с исполняемым файлом (<see cref="PortablePaths.PortableAppDataDirectory"/>),
    /// что позволяет переносить все настройки вместе со сменным носителем (функция №1
    /// StartManager, Этап 10). Иначе — системный каталог профиля.
    /// </summary>
    public static string AppDataDirectory =>
        PortablePaths.TryResolveDataDirectory() ?? DefaultAppDataDirectory;

    /// <summary>
    /// Системный каталог данных приложения по умолчанию (без учёта портативного режима).
    /// Windows: %APPDATA%\Workspace1C; Linux: ~/.config/Workspace1C.
/// При первом обращении переносит данные из каталога прежней версии (ConfigurationManagement).
    /// </summary>
    public static string DefaultAppDataDirectory
    {
        get
        {
            var dir = Path.Combine(GetConfigBaseDirectory(), AppFolderName);
            EnsureMigratedFromLegacy(dir);
            return dir;
        }
    }

    /// <summary>Имя каталога данных приложения Workspace 1C.</summary>
    public const string AppFolderName = "Workspace1C";

    /// <summary>
    /// Имя каталога данных прежнего названия программы («Управление конфигурациями 1С»).
    /// Используется только для однократного переноса настроек при первом запуске Workspace 1C.
    /// </summary>
    public const string LegacyAppFolderName = "ConfigurationManagement";

    private const string MigrationMarkerFile = ".migrated-from-ConfigurationManagement";
    private static readonly object MigrationLock = new();
    private static bool _migrationChecked;

    /// <summary>
    /// Однократный перенос данных из каталога прежней версии (ConfigurationManagement)
    /// в каталог Workspace1C. Файлы копируются без перезаписи, старый каталог не удаляется
    /// (к нему можно вернуться). Повторно перенос не выполняется (файл-маркер).
    /// Любые ошибки игнорируются: программа продолжит работу с новым каталогом.
    /// </summary>
    public static void EnsureMigratedFromLegacy(string? targetDirectory = null)
    {
        if (_migrationChecked)
            return;
        lock (MigrationLock)
        {
            if (_migrationChecked)
                return;
            _migrationChecked = true;
            try
            {
                var baseDir = GetConfigBaseDirectory();
                var target = targetDirectory ?? Path.Combine(baseDir, AppFolderName);
                var legacy = Path.Combine(baseDir, LegacyAppFolderName);
                if (!Directory.Exists(legacy))
                    return;
                if (File.Exists(Path.Combine(target, MigrationMarkerFile)))
                    return;
                // Если в новом каталоге уже есть собственные данные — не вмешиваемся.
                if (File.Exists(Path.Combine(target, "settings.json")) ||
                    File.Exists(Path.Combine(target, "infobases.json")))
                    return;

                CopyDirectory(legacy, target);
                File.WriteAllText(Path.Combine(target, MigrationMarkerFile),
                    "Данные перенесены из " + legacy + " " + DateTime.Now.ToString("s"));
            }
            catch
            {
                // Перенос — best effort: при сбое программа стартует с пустым каталогом.
            }
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            var dest = Path.Combine(target, Path.GetFileName(file));
            if (!File.Exists(dest))
            {
                try { File.Copy(file, dest, overwrite: false); } catch { /* занят/нет доступа */ }
            }
        }
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    /// <summary>Базовый каталог конфигурации пользователя (%APPDATA% / ~/.config).</summary>
    private static string GetConfigBaseDirectory()
    {
        {
#if LINUX
            // На Linux SpecialFolder.ApplicationData уже указывает на ~/.config,
            // но задаём путь явно и корректно обрабатываем XDG_CONFIG_HOME.
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var baseDir = !string.IsNullOrWhiteSpace(configHome)
                ? configHome
                : string.IsNullOrEmpty(home)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                    : Path.Combine(home, ".config");
            return baseDir;
#else
            return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
#endif
        }
    }

    /// <summary>
    /// Каталог логов приложения (файловый логгер FileAppLogger).
    /// </summary>
    public static string LogDirectory => Path.Combine(AppDataDirectory, "logs");
}