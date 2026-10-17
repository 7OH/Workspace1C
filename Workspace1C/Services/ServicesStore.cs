using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Workspace1C.Services;

/// <summary>
/// Хранилище списка служб окна «Управление службами» (issue #11): файл
/// <c>services.json</c> в каталоге данных профиля/приложения — имена служб,
/// за которыми следит пользователь (Windows: ServiceName; Linux: имена
/// unit'ов systemd). Запись — атомарная (временный файл + замена), JSON с
/// отступами; битый файл не роняет приложение — возвращается пустой список.
/// </summary>
public interface IServicesStore
{
    /// <summary>Читает имена служб (пустой список при отсутствии/битости файла).</summary>
    IReadOnlyList<string> Load();

    /// <summary>Сохраняет список имён служб (атомарная запись, JSON с отступами).</summary>
    void Save(IReadOnlyCollection<string> serviceNames);
}

/// <summary>
/// Реализация <see cref="IServicesStore"/> по образцу <see cref="ServerListStore"/>:
/// файл <c>services.json</c> рядом с настройками (<see cref="IProfileService.CurrentProfileDataDirectory"/>),
/// <c>directoryOverride</c> для юнит-тестов.
/// </summary>
public sealed class ServicesStore : IServicesStore
{
    /// <summary>Имя файла хранилища служб (рядом с настройками приложения).</summary>
    public const string FileName = "services.json";

    private readonly IProfileService? _profileService;
    private readonly string? _directoryOverride;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <param name="profileService">Профиль для определения каталога данных (может быть null).</param>
    /// <param name="directoryOverride">Явный каталог хранения вместо каталога данных
    /// (используется в юнит-тестах для изоляции от реальных данных профиля).</param>
    public ServicesStore(IProfileService? profileService = null, string? directoryOverride = null)
    {
        _profileService = profileService;
        _directoryOverride = directoryOverride;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            // Кириллицу (имена служб 1С) пишем читаемыми UTF-8, а не \uXXXX (паттерн ServerListStore).
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }

    /// <summary>Каталог данных: явный (тесты) → каталог активного профиля → общий каталог приложения.</summary>
    private string DataDirectory =>
        !string.IsNullOrWhiteSpace(_directoryOverride)
            ? _directoryOverride!
            : (!string.IsNullOrWhiteSpace(_profileService?.CurrentProfileDataDirectory)
                ? _profileService!.CurrentProfileDataDirectory
                : PlatformPaths.AppDataDirectory);

    /// <summary>Полный путь к файлу хранилища.</summary>
    public string FilePath => Path.Combine(DataDirectory, FileName);

    /// <inheritdoc />
    public IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new List<string>();

            var json = File.ReadAllText(FilePath);
            var raw = JsonSerializer.Deserialize<List<string>>(json, _jsonOptions);
            return Normalize(raw);
        }
        catch
        {
            // Битый/нечитаемый файл не должен ронять загрузку: возвращаем пустой список.
            return new List<string>();
        }
    }

    /// <inheritdoc />
    public void Save(IReadOnlyCollection<string> serviceNames)
    {
        var normalized = Normalize(serviceNames?.ToList() ?? new List<string>());
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(normalized, _jsonOptions);

        // Атомарная запись: сначала во временный файл, затем замена целевого.
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    /// <summary>
    /// Приводит список имён к каноническому виду: Trim, пропуск пустых,
    /// удаление дубликатов (порядок сохраняется) — по образцу ServerListStore.Normalize.
    /// </summary>
    private static IReadOnlyList<string> Normalize(IReadOnlyList<string>? source)
    {
        var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        if (source is null)
            return result;

        foreach (var name in source)
        {
            var normalized = (name ?? string.Empty).Trim();
            if (normalized.Length == 0)
                continue;
            if (seen.Add(normalized))
                result.Add(normalized);
        }
        return result;
    }
}