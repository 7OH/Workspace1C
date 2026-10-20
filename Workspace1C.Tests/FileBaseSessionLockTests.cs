using System;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты файловой блокировки сеансов через 1cv8.cdn (issue #20): создание файла
/// в каталоге файловой базы, удаление по «Разрешить работу», ошибки каталога
/// и определение типа базы (файловая/клиент-серверная).
/// </summary>
public sealed class FileBaseSessionLockTests
{
    [Fact]
    public void Lock_CreatesCdnFileWithContent()
    {
        using var tmp = TempDir();
        var infobase = FileBase(tmp.Path);
        var options = new SessionLockOptions
        {
            StartTime = new DateTime(2026, 10, 20, 10, 0, 0),
            Duration = TimeSpan.FromMinutes(30),
            Message = "Работы",
            PermissionCode = "CODE"
        };

        var result = new FileBaseSessionLock().Lock(infobase, options);

        Assert.True(result.Success);
        var path = Path.Combine(tmp.Path, FileBaseSessionLock.CdnFileName);
        Assert.True(File.Exists(path));
        Assert.Equal(options.BuildCdnString(), File.ReadAllText(path));
    }

    [Fact]
    public void Unlock_RemovesCdnFile()
    {
        using var tmp = TempDir();
        var infobase = FileBase(tmp.Path);
        var lockService = new FileBaseSessionLock();
        var options = new SessionLockOptions { StartTime = DateTime.Now, Message = "Работы" };

        Assert.True(lockService.Lock(infobase, options).Success);
        Assert.True(File.Exists(Path.Combine(tmp.Path, FileBaseSessionLock.CdnFileName)));

        var result = lockService.Unlock(infobase);

        Assert.True(result.Success);
        Assert.False(File.Exists(Path.Combine(tmp.Path, FileBaseSessionLock.CdnFileName)));
    }

    [Fact]
    public void Unlock_WhenNoCdnFile_IsSuccess()
    {
        using var tmp = TempDir();
        var infobase = FileBase(tmp.Path);

        var result = new FileBaseSessionLock().Unlock(infobase);

        Assert.True(result.Success);
    }

    [Fact]
    public void Lock_WhenDirectoryMissing_Fails()
    {
        using var tmp = TempDir();
        var missing = Path.Combine(tmp.Path, "no-such-dir");
        var infobase = FileBase(missing);
        var options = new SessionLockOptions { StartTime = DateTime.Now, Message = "Работы" };

        var result = new FileBaseSessionLock().Lock(infobase, options);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public void Lock_WhenNoPath_Fails()
    {
        var infobase = new Infobase
        {
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = "" }
        };
        var options = new SessionLockOptions { StartTime = DateTime.Now, Message = "Работы" };

        var result = new FileBaseSessionLock().Lock(infobase, options);

        Assert.False(result.Success);
    }

    [Fact]
    public void IsFileBase_DetectsFileType()
    {
        var file = new Infobase
        {
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\base" }
        };
        var cs = new Infobase
        {
            Connection = new ConnectionSettings { Type = ConnectionType.ClientServer, Server = "srv" }
        };

        Assert.True(SessionLockService.IsFileBase(file));
        Assert.False(SessionLockService.IsFileBase(cs));
        Assert.False(SessionLockService.IsFileBase(null));
    }

    private static Infobase FileBase(string path) => new()
    {
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = path }
    };

    private static TempDirectory TempDir() => new();

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cm_fbl_{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* не критично */ }
        }
    }
}