using System.Threading.Tasks;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты кэша статусов служб (issue #11, 1.0.0.14): мгновенное чтение из памяти
/// (без обращения к контроллеру) при построении меню трея/«СЛУЖБЫ», фоновое
/// обновление <see cref="ServiceStatusCache.RefreshAsync"/>, сброс после операций.
/// </summary>
public sealed class ServiceStatusCacheTests
{
    [Fact]
    public async Task GetStatus_AfterRefresh_ReturnsCachedStatus_WithoutControllerCalls()
    {
        var fake = new ServicesWindowViewModelTests.FakeServiceController();
        fake.Services["srv"] = Info("srv", "Srv", ServiceStatus.Running);
        var cache = new ServiceStatusCache(fake);

        await cache.RefreshAsync(new[] { "srv" });

        Assert.Equal(ServiceStatus.Running, cache.GetStatus("srv").Status);
        Assert.Equal("Srv", cache.GetStatus("srv").DisplayName);
        // Повторный вызов GetStatus не должен опрашивать контроллер (только кэш).
        var callsBefore = fake.Calls.Count;
        _ = cache.GetStatus("srv");
        Assert.Equal(callsBefore, fake.Calls.Count);
    }

    [Fact]
    public void GetStatus_BeforeRefresh_ReturnsUnknown_WithoutBlockingController()
    {
        var fake = new ServicesWindowViewModelTests.FakeServiceController();
        var cache = new ServiceStatusCache(fake);

        // Мгновенный «холодный» статус — Unknown, контроллер не опрашивается
        // (меню трея показывается без задержки; свежесть поддерживает фон).
        var status = cache.GetStatus("srv");
        Assert.Equal(ServiceStatus.Unknown, status.Status);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task Refresh_CachesControllerStatus_ForEachName_WithoutDeadlock()
    {
        var fake = new ServicesWindowViewModelTests.FakeServiceController();
        fake.Services["bad"] = Info("bad", "Bad", ServiceStatus.Running);
        var cache = new ServiceStatusCache(fake);
        await cache.RefreshAsync(new[] { "ok", "bad" });

        // Неизвестная служба — статус из контроллера (NotFound), исключений нет.
        Assert.Equal(ServiceStatus.NotFound, cache.GetStatus("ok").Status);
        Assert.Equal(ServiceStatus.Running, cache.GetStatus("bad").Status);
    }

    [Fact]
    public async Task Invalidate_ClearsEntries()
    {
        var fake = new ServicesWindowViewModelTests.FakeServiceController();
        fake.Services["srv"] = Info("srv", "Srv", ServiceStatus.Running);
        var cache = new ServiceStatusCache(fake);
        await cache.RefreshAsync(new[] { "srv" });

        cache.Invalidate();

        Assert.Equal(ServiceStatus.Unknown, cache.GetStatus("srv").Status);
    }

    private static ServiceInfo Info(string name, string display, ServiceStatus status) => new()
    {
        ServiceName = name,
        DisplayName = display,
        Status = status,
        CanStop = true
    };
}