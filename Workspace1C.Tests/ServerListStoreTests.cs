using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты хранилища справочника «Сервера управления» (issue #3): JSON-файл
/// server_list.json рядом с настройками; сохранение/чтение записей (описание,
/// адрес и порт, логин и пароль); нормализация (пустые записи отбрасываются,
/// порт ограничивается диапазоном); битый файл не роняет загрузку.
/// </summary>
public sealed class ServerListStoreTests
{
    [Fact]
    public void Save_ThenLoad_ReturnsSavedServers()
    {
        using var tmp = TempDir();
        var store = new ServerListStore(directoryOverride: tmp.Path);

        store.Save(new[]
        {
            new ManagementServer
            {
                Id = "srv-1",
                Description = "Основной сервер",
                Address = "srv1c.local",
                Port = 1540,
                User = "admin",
                Password = "secret"
            }
        });

        var loaded = store.Load();
        var server = Assert.Single(loaded);
        Assert.Equal("srv-1", server.Id);
        Assert.Equal("Основной сервер", server.Description);
        Assert.Equal("srv1c.local", server.Address);
        Assert.Equal(1540, server.Port);
        Assert.Equal("admin", server.User);
        Assert.Equal("secret", server.Password);
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public void Save_TwoServers_BothStoredIndependently()
    {
        using var tmp = TempDir();
        var store = new ServerListStore(directoryOverride: tmp.Path);

        store.Save(new[]
        {
            new ManagementServer { Id = "a", Description = "Сервер А", Address = "srv-a", Port = 1540 },
            new ManagementServer { Id = "b", Description = "Сервер Б", Address = "srv-b", Port = 1545 }
        });

        var loaded = store.Load();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("srv-a", loaded[0].Address);
        Assert.Equal(1545, loaded[1].Port);
    }

    [Fact]
    public void Save_OverwritesWholeList()
    {
        using var tmp = TempDir();
        var store = new ServerListStore(directoryOverride: tmp.Path);

        store.Save(new[] { new ManagementServer { Id = "a", Description = "А", Address = "srv-a" } });
        store.Save(new[] { new ManagementServer { Id = "b", Description = "Б", Address = "srv-b" } });

        var loaded = store.Load();
        var server = Assert.Single(loaded);
        Assert.Equal("b", server.Id);
    }

    [Fact]
    public void Load_WhenNoFile_ReturnsEmpty()
    {
        using var tmp = TempDir();
        var store = new ServerListStore(directoryOverride: tmp.Path);

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmptyWithoutThrowing()
    {
        using var tmp = TempDir();
        Directory.CreateDirectory(tmp.Path);
        var store = new ServerListStore(directoryOverride: tmp.Path);
        File.WriteAllText(store.FilePath, "{ not valid json ");

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Save_EmptyDescriptionAndAddress_IsDropped()
    {
        using var tmp = TempDir();
        var store = new ServerListStore(directoryOverride: tmp.Path);

        store.Save(new[]
        {
            new ManagementServer { Id = "empty", Description = "", Address = "" },
            new ManagementServer { Id = "ok", Description = "Рабочий", Address = "srv" }
        });

        var loaded = store.Load();
        var server = Assert.Single(loaded);
        Assert.Equal("ok", server.Id);
    }

    [Fact]
    public void Save_OutOfRangePort_IsSanitizedToDefault()
    {
        using var tmp = TempDir();
        var store = new ServerListStore(directoryOverride: tmp.Path);

        store.Save(new[]
        {
            new ManagementServer { Id = "srv", Description = "Сервер", Address = "srv", Port = 70000 }
        });

        var server = Assert.Single(store.Load());
        Assert.Equal(1540, server.Port);
    }

    [Fact]
    public void Save_MissingId_IsAssigned()
    {
        using var tmp = TempDir();
        var store = new ServerListStore(directoryOverride: tmp.Path);

        store.Save(new[] { new ManagementServer { Id = "", Description = "Сервер", Address = "srv" } });

        var server = Assert.Single(store.Load());
        Assert.False(string.IsNullOrWhiteSpace(server.Id));
    }

    private static TempDirectory TempDir() => new();

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cm_sls_{Guid.NewGuid():N}");

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* не критично */ }
        }
    }
}
