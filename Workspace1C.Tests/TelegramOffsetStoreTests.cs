using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты <see cref="TelegramOffsetStore"/> (персистентный update_id) и
/// <see cref="TelegramConfirmationLock"/> (блокировка одного активного запроса).
/// </summary>
public sealed class TelegramOffsetStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "w1c_tg_offset_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* каталог может не существовать */ }
    }

    private TelegramOffsetStore CreateStore() => new(directoryOverride: _dir);

    [Fact]
    public void Load_MissingFile_ReturnsNull()
    {
        Assert.Null(CreateStore().Load());
    }

    [Fact]
    public void SaveLoad_RoundTrip()
    {
        var store = CreateStore();
        store.Save(105);
        Assert.Equal(105, store.Load());
    }

    [Fact]
    public void Save_OverwritesPrevious()
    {
        var store = CreateStore();
        store.Save(105);
        store.Save(200);
        Assert.Equal(200, store.Load());
    }

    [Fact]
    public void Save_NonPositive_Ignored()
    {
        var store = CreateStore();
        store.Save(0);
        store.Save(-5);
        Assert.Null(store.Load());
    }

    [Fact]
    public void Load_CorruptedFile_ReturnsNull()
    {
        var store = CreateStore();
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, TelegramOffsetStore.FileName), "not-a-number");
        Assert.Null(store.Load());
    }

    // ---------- блокировка одного активного запроса ----------

    [Fact]
    public void Lock_AcquireRelease_RoundTrip()
    {
        var lockFile = new TelegramConfirmationLock(_dir);
        Assert.True(lockFile.TryAcquire(Guid.NewGuid(), TimeSpan.FromMinutes(5)));
        Assert.True(File.Exists(lockFile.FilePath));
        lockFile.Release();
        Assert.False(File.Exists(lockFile.FilePath));
    }

    [Fact]
    public void Lock_ActiveRequest_RejectsSecond()
    {
        var lockFile = new TelegramConfirmationLock(_dir);
        Assert.True(lockFile.TryAcquire(Guid.NewGuid(), TimeSpan.FromMinutes(5)));
        // Второй процесс: активный блок — отказ.
        Assert.False(new TelegramConfirmationLock(_dir).TryAcquire(Guid.NewGuid(), TimeSpan.FromMinutes(5)));
        lockFile.Release();
    }

    [Fact]
    public void Lock_StaleBlock_IsOverwritten()
    {
        // Блок, созданный давно (после полного таймаута) — перезаписывается.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, TelegramConfirmationLock.FileName),
            "{\"pid\":1,\"requestId\":\"00000000-0000-0000-0000-000000000001\",\"createdUtc\":\"2000-01-01T00:00:00Z\"}");
        Assert.True(new TelegramConfirmationLock(_dir).TryAcquire(Guid.NewGuid(), TimeSpan.FromMinutes(5)));
    }
}
