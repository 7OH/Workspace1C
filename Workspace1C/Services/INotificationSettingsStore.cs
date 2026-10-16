using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Хранилище настроек внешних каналов уведомлений (Telegram, email) — файл
/// <c>notifications.json</c> в каталоге данных активного профиля (выпуск 0.3.12.69).
/// </summary>
public interface INotificationSettingsStore
{
    /// <summary>Полный путь к файлу настроек.</summary>
    string FilePath { get; }

    /// <summary>Читает настройки; при отсутствии или повреждении файла — значения по умолчанию.</summary>
    NotificationChannelSettings Load();

    /// <summary>Сохраняет настройки атомарно (временный файл + замена).</summary>
    void Save(NotificationChannelSettings settings);
}
