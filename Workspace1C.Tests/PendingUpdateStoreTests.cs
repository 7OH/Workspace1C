using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты маркера «запланировано уведомление об успешном обновлении» (issue #10):
/// запись/чтение/удаление файла pending_update.json и чистая функция решения
/// ShouldNotify (версия маркера совпадает с запущенной — уведомлять, иначе нет).
/// Паттерн как у ServerListStoreTests: временный каталог, изоляция от профиля.
/// </summary>
public sealed class PendingUpdateStoreTests
{
    [Fact]
    public void Write_ReadAndClear_ReturnsVersion_AndDeletesFile()
    {
        using var tmp = new TempDirectory();
        var store = new PendingUpdateStore(directoryOverride: tmp.Path);

        store.Write("v1.0.0.9");

        Assert.True(System.IO.File.Exists(store.FilePath));
        Assert.Equal("v1.0.0.9", store.ReadAndClear());
        // Маркер удалён — повторное чтение ничего не даёт.
        Assert.False(System.IO.File.Exists(store.FilePath));
        Assert.Null(store.ReadAndClear());
    }

    [Fact]
    public void Write_OverwritesPreviousMarker()
    {
        using var tmp = new TempDirectory();
        var store = new PendingUpdateStore(directoryOverride: tmp.Path);

        store.Write("v1.0.0.8");
        store.Write("v1.0.0.9");

        Assert.Equal("v1.0.0.9", store.ReadAndClear());
    }

    [Fact]
    public void ReadAndClear_WithoutMarker_ReturnsNull()
    {
        using var tmp = new TempDirectory();
        var store = new PendingUpdateStore(directoryOverride: tmp.Path);

        Assert.Null(store.ReadAndClear());
    }

    [Fact]
    public void ReadAndClear_CorruptedFile_ReturnsNull_AndDeletesFile()
    {
        using var tmp = new TempDirectory();
        var store = new PendingUpdateStore(directoryOverride: tmp.Path);
        System.IO.Directory.CreateDirectory(tmp.Path);
        System.IO.File.WriteAllText(store.FilePath, "{{{ не json");

        Assert.Null(store.ReadAndClear());
        Assert.False(System.IO.File.Exists(store.FilePath));
    }

    [Fact]
    public void ShouldNotify_MatchingVersions_WithAndWithoutV()
    {
        // Тег релиза «v1.0.0.9» совпадает с информационной версией «1.0.0.9».
        Assert.True(PendingUpdateStore.ShouldNotify("v1.0.0.9", "1.0.0.9"));
        Assert.True(PendingUpdateStore.ShouldNotify("1.0.0.9", "1.0.0.9"));
        Assert.True(PendingUpdateStore.ShouldNotify("V1.0.0.9", "1.0.0.9"));
    }

    [Fact]
    public void ShouldNotify_MismatchedVersions_False()
    {
        // Версии не совпали — обновление не применилось (помощник не заменил exe),
        // уведомлять об «успешном обновлении» нельзя (issue #10).
        Assert.False(PendingUpdateStore.ShouldNotify("v1.0.0.9", "1.0.0.8"));
        Assert.False(PendingUpdateStore.ShouldNotify("v1.0.0.9", "1.0.0.10"));
    }

    [Fact]
    public void ShouldNotify_EmptyOrNull_False()
    {
        Assert.False(PendingUpdateStore.ShouldNotify(null, "1.0.0.9"));
        Assert.False(PendingUpdateStore.ShouldNotify("", "1.0.0.9"));
        Assert.False(PendingUpdateStore.ShouldNotify("   ", "1.0.0.9"));
        Assert.False(PendingUpdateStore.ShouldNotify("v1.0.0.9", null));
        Assert.False(PendingUpdateStore.ShouldNotify("v1.0.0.9", ""));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"cm_pending_{System.Guid.NewGuid():N}");

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Path, recursive: true); }
            catch { /* не критично */ }
        }
    }
}