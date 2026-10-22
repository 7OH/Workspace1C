using System.IO;

namespace Workspace1C.Services;

/// <summary>
/// Персистентное хранение последнего обработанного <c>update_id</c> из <c>getUpdates</c>
/// (выпуск 1.0.0.36+): файл <c>telegram.offset</c> в каталоге данных профиля, по образцу
/// <see cref="NotificationSettingsStore"/> (явный каталог → активный профиль → общий каталог).
/// Запись атомарная (временный файл + переименование). Благодаря сохранённому offset каждый
/// следующий <c>--telegram-confirm</c> не перечитывает подтверждённую историю и не пропускает
/// новые нажатия кнопок.
/// </summary>
public sealed class TelegramOffsetStore
{
    /// <summary>Имя файла последнего обработанного update_id.</summary>
    public const string FileName = "telegram.offset";

    private readonly IProfileService? _profileService;
    private readonly string? _directoryOverride;
    private readonly IAppLogger? _logger;

    /// <param name="profileService">Профиль для определения каталога данных (может быть null).</param>
    /// <param name="directoryOverride">Явный каталог хранения (юнит-тесты).</param>
    /// <param name="logger">Журнал (может быть null).</param>
    public TelegramOffsetStore(
        IProfileService? profileService = null,
        string? directoryOverride = null,
        IAppLogger? logger = null)
    {
        _profileService = profileService;
        _directoryOverride = directoryOverride;
        _logger = logger;
    }

    private string DataDirectory =>
        !string.IsNullOrWhiteSpace(_directoryOverride)
            ? _directoryOverride!
            : (!string.IsNullOrWhiteSpace(_profileService?.CurrentProfileDataDirectory)
                ? _profileService!.CurrentProfileDataDirectory
                : PlatformPaths.AppDataDirectory);

    /// <summary>Полный путь файла offset.</summary>
    public string FilePath => Path.Combine(DataDirectory, FileName);

    /// <summary>
    /// Последний обработанный update_id (null, если подтверждений ещё не было).
    /// Битый/отсутствующий файл — null (более безопасно повторить историю, чем пропустить ответ).
    /// </summary>
    public long? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;
            var text = File.ReadAllText(FilePath).Trim();
            return long.TryParse(text, out var id) && id > 0 ? id : null;
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Не удалось прочитать {FileName}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Сохраняет последний обработанный update_id (значение ≤ 0 игнорируется).</summary>
    public void Save(long updateId)
    {
        if (updateId <= 0)
            return;
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, updateId.ToString());
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Не удалось сохранить {FileName}: {ex.Message}");
        }
    }
}
