using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Workspace1C.Services;

/// <summary>
/// Хранилище списка служб окна «Управление службами» (issue #11): файл
/// <c>services.json</c> в каталоге данных профиля/приложения — службы и группы
/// служб, за которыми следит пользователь (Windows: ServiceName; Linux: имена
/// unit'ов systemd). Итерация 2 (релиз 1.0.0.12): объектный формат
/// <c>{services: [...], groups: [{name, services}], order: [...]}</c> с обратной
/// совместимостью — старый формат (массив строк) при чтении мигрируется в объект
/// со службами и пустыми группами; объект без <c>order</c> получает порядок
/// автоматически (все службы, затем группы — см. <see cref="ServiceStoreData.NormalizeOrder"/>).
/// Запись — атомарная (временный файл + замена), JSON с отступами; битый файл не
/// роняет приложение — возвращается пустой объект.
/// </summary>
public interface IServicesStore
{
    /// <summary>Читает службы и группы (пустой объект при отсутствии/битости файла).</summary>
    ServiceStoreData Load();

    /// <summary>Сохраняет службы и группы (атомарная запись, JSON с отступами).</summary>
    void Save(ServiceStoreData data);
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
    public ServiceStoreData Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new ServiceStoreData();

            var json = File.ReadAllText(FilePath);
            if (json.Trim().Length == 0)
                return new ServiceStoreData();

            // Старый формат (итерация 1, релиз ≤ 1.0.0.11): массив строк — мигрируем
            // в объект со службами и пустыми группами (обратная совместимость, issue #11).
            if (json.Trim().StartsWith("["))
            {
                var legacy = JsonSerializer.Deserialize<List<string>>(json, _jsonOptions);
                return ServiceStoreData.Normalize(ServiceStoreData.FromLegacy(legacy));
            }

            // Новый формат (итерация 2): объект {services, groups} — читаем как есть.
            var data = JsonSerializer.Deserialize<ServiceStoreData>(json, _jsonOptions);
            return ServiceStoreData.Normalize(data ?? new ServiceStoreData());
        }
        catch
        {
            // Битый/нечитаемый файл не должен ронять загрузку: возвращаем пустой объект.
            return new ServiceStoreData();
        }
    }

    /// <inheritdoc />
    public void Save(ServiceStoreData data)
    {
        var normalized = ServiceStoreData.Normalize(data);
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(normalized, _jsonOptions);

        // Атомарная запись: сначала во временный файл, затем замена целевого.
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, FilePath, overwrite: true);
    }
}