using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты хранилища служб окна «Управление службами» (issue #11): JSON-файл
/// services.json рядом с настройками; запись/чтение round-trip; нормализация
/// (Trim, удаление пустых/дубликатов); битый файл не роняет загрузку;
/// временный файл (атомарность) не остаётся после сохранения.
/// </summary>
public sealed class ServicesStoreTests
{
    [Fact]
    public void Save_ThenLoad_ReturnsSavedNames()
    {
        using var tmp = TempDir();
        var store = new ServicesStore(directoryOverride: tmp.Path);

        store.Save(ServiceStoreData.FromLegacy(new[] { "1C:Enterprise 8.3 Server", "postgresql.service" }));

        var loaded = store.Load();
        Assert.Equal(2, loaded.Services.Count);
        Assert.Equal("1C:Enterprise 8.3 Server", loaded.Services[0]);
        Assert.Equal("postgresql.service", loaded.Services[1]);
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public void Save_OverwritesWholeList()
    {
        using var tmp = TempDir();
        var store = new ServicesStore(directoryOverride: tmp.Path);

        store.Save(ServiceStoreData.FromLegacy(new[] { "srv-a" }));
        store.Save(ServiceStoreData.FromLegacy(new[] { "srv-b" }));

        var loaded = store.Load();
        var name = Assert.Single(loaded.Services);
        Assert.Equal("srv-b", name);
    }

    [Fact]
    public void Load_WhenNoFile_ReturnsEmpty()
    {
        using var tmp = TempDir();
        var store = new ServicesStore(directoryOverride: tmp.Path);

        Assert.Empty(store.Load().Services);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmptyWithoutThrowing()
    {
        using var tmp = TempDir();
        Directory.CreateDirectory(tmp.Path);
        var store = new ServicesStore(directoryOverride: tmp.Path);
        File.WriteAllText(store.FilePath, "{ not valid json ");

        Assert.Empty(store.Load().Services);
    }

    [Fact]
    public void Save_TrimsEmptyAndDuplicates()
    {
        using var tmp = TempDir();
        var store = new ServicesStore(directoryOverride: tmp.Path);

        store.Save(ServiceStoreData.FromLegacy(new[] { "  srv-a  ", "", "  ", "srv-a", "srv-b", null! }));

        var loaded = store.Load();
        Assert.Equal(2, loaded.Services.Count);
        Assert.Equal("srv-a", loaded.Services[0]);
        Assert.Equal("srv-b", loaded.Services[1]);
    }

    [Fact]
    public void Save_LeavesNoTempFile()
    {
        using var tmp = TempDir();
        var store = new ServicesStore(directoryOverride: tmp.Path);

        store.Save(ServiceStoreData.FromLegacy(new[] { "srv-a" }));

        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Fact]
    public void DirectoryOverride_IsolatesFromProfile()
    {
        using var tmpA = TempDir();
        using var tmpB = TempDir();
        var storeA = new ServicesStore(directoryOverride: tmpA.Path);
        var storeB = new ServicesStore(directoryOverride: tmpB.Path);

        storeA.Save(ServiceStoreData.FromLegacy(new[] { "only-in-a" }));

        Assert.Single(storeA.Load().Services);
        Assert.Empty(storeB.Load().Services);
    }

    private static TempDirectory TempDir() => new();

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cm_svcs_{Guid.NewGuid():N}");

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* не критично */ }
        }
    }
}