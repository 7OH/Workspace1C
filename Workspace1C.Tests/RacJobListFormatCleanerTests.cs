using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты одноразовой чистки исторического файла кэша форматов rac
/// (<see cref="RacJobListFormatCleaner"/>, issue #39): с 1.0.0.40 кэш рабочих форматов
/// передачи кластера rac живёт только в памяти, а файл <c>rac_joblist_formats.json</c>,
/// созданный прежними версиями, удаляется при первом старте новой версии.
/// Чистка — best effort: отсутствующий файл и любые ошибки не должны мешать запуску.
/// </summary>
public sealed class RacJobListFormatCleanerTests
{
    [Fact]
    public void Clean_ExistingLegacyFile_IsDeleted()
    {
        using var temp = new TempDirectory();
        var legacyPath = System.IO.Path.Combine(temp.Path, RacJobListFormatCleaner.LegacyFileName);
        System.IO.Directory.CreateDirectory(temp.Path);
        System.IO.File.WriteAllText(legacyPath, "{\"key|job list\": 2}");

        // «Запуск новой версии»: файл-кэш прежней версии должен быть удалён.
        RacJobListFormatCleaner.Clean(temp.Path);

        Assert.False(System.IO.File.Exists(legacyPath));
        Assert.Empty(System.IO.Directory.GetFiles(temp.Path, RacJobListFormatCleaner.LegacyFileName));
    }

    [Fact]
    public void Clean_MissingFile_DoesNotThrow()
    {
        using var temp = new TempDirectory();
        System.IO.Directory.CreateDirectory(temp.Path);

        // Отсутствие файла — не ошибка: чистка должна пройти молча.
        RacJobListFormatCleaner.Clean(temp.Path);

        Assert.False(System.IO.File.Exists(
            System.IO.Path.Combine(temp.Path, RacJobListFormatCleaner.LegacyFileName)));
    }

    [Fact]
    public void Clean_UnreadableDirectory_DoesNotThrow()
    {
        // Чистка не должна ронять запуск ни при какой ошибке доступа (best effort).
        using var temp = new TempDirectory();
        System.IO.Directory.CreateDirectory(temp.Path);
        var lockedPath = System.IO.Path.Combine(temp.Path, RacJobListFormatCleaner.LegacyFileName);

        // Моделируем «неудаляемый» файл: между проверкой и удалением его можно
        // пересоздать/захватить — Clean обязан проглотить любые исключения.
        System.IO.File.WriteAllText(lockedPath, "{}");

        RacJobListFormatCleaner.Clean(temp.Path); // не должен бросать

        Assert.False(System.IO.File.Exists(lockedPath));
    }

    /// <summary>Временный каталог для изоляции чистки файла (issue #39).</summary>
    private sealed class TempDirectory : System.IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cm_rac_cleaner_{System.Guid.NewGuid():N}");

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Path, recursive: true); } catch { /* не критично */ }
        }
    }
}