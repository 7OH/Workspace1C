using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты ViewModel окна «Настроить» (issue #11): галочки у уже сохранённых служб,
/// результат SelectedNames (отмеченные в порядке списка), «Выбрать все/Сбросить»
/// и поиск-фильтр (отмеченные не скрываются).
/// </summary>
public sealed class ServicesPickerViewModelTests
{
    [Fact]
    public void Constructor_ChecksSavedServices()
    {
        var fake = Fake();
        var store = new Store(new[] { "srv-b" });

        var vm = new ServicesPickerViewModel(fake, store);

        Assert.Equal(2, vm.AllServices.Count);
        Assert.False(vm.AllServices.Single(r => r.ServiceName == "srv-a").IsChecked);
        Assert.True(vm.AllServices.Single(r => r.ServiceName == "srv-b").IsChecked);
        Assert.Equal(1, vm.CheckedCount);
    }

    [Fact]
    public void Ok_ReturnsCheckedNamesInListOrder()
    {
        var fake = Fake();
        var store = new Store(new[] { "srv-a" });

        var vm = new ServicesPickerViewModel(fake, store);
        vm.AllServices.Single(r => r.ServiceName == "srv-b").IsChecked = true;

        vm.OkCommand.Execute(null);

        Assert.True(vm.OkPressed);
        Assert.Equal(new[] { "srv-a", "srv-b" }, vm.SelectedNames);
    }

    [Fact]
    public void SelectAll_ChecksAll()
    {
        var fake = Fake();
        var vm = new ServicesPickerViewModel(fake, new Store(Array.Empty<string>()));

        vm.SelectAllCommand.Execute(null);

        Assert.All(vm.AllServices, r => Assert.True(r.IsChecked));
        Assert.Equal(2, vm.CheckedCount);
    }

    [Fact]
    public void SelectNone_UnchecksAll()
    {
        var fake = Fake();
        var store = new Store(new[] { "srv-a", "srv-b" });

        var vm = new ServicesPickerViewModel(fake, store);
        Assert.Equal(2, vm.CheckedCount);

        vm.SelectNoneCommand.Execute(null);

        Assert.All(vm.AllServices, r => Assert.False(r.IsChecked));
        Assert.Equal(0, vm.CheckedCount);
    }

    [Fact]
    public void Search_FiltersByName_ButKeepsChecked()
    {
        var fake = Fake();
        var store = new Store(new[] { "srv-a" });

        var vm = new ServicesPickerViewModel(fake, store);

        vm.SearchText = "srv-b";

        // Отмеченная srv-a остаётся, к ней добавляется совпавшая с поиском srv-b.
        var filtered = vm.FilteredServices;
        Assert.Equal(2, filtered.Count);
        Assert.Contains(filtered, r => r.ServiceName == "srv-b");

        vm.SearchText = "несуществующее имя";
        // Отмеченные не скрываются, совпадений нет.
        var onlyChecked = vm.FilteredServices;
        var row = Assert.Single(onlyChecked);
        Assert.Equal("srv-a", row.ServiceName);
    }

    [Fact]
    public void Search_Empty_ShowsAll()
    {
        var fake = Fake();
        var vm = new ServicesPickerViewModel(fake, new Store(Array.Empty<string>()));

        vm.SearchText = "";
        Assert.Equal(2, vm.FilteredServices.Count);
    }

    /// <summary>Fake-сервис с двумя службами.</summary>
    private static FakeServiceController Fake() => new();

    private sealed class FakeServiceController : IServiceControllerService
    {
        public bool IsAvailable => true;
        public bool CanOpenSystemDialog => true;

        public IReadOnlyList<ServiceInfo> GetAllServices() => new[]
        {
            new ServiceInfo { ServiceName = "srv-a", DisplayName = "Service A", Status = ServiceStatus.Running },
            new ServiceInfo { ServiceName = "srv-b", DisplayName = "Service B", Status = ServiceStatus.Stopped }
        };

        public ServiceInfo GetStatus(string serviceName) =>
            new() { ServiceName = serviceName, DisplayName = serviceName, Status = ServiceStatus.Unknown };

        public void OpenSystemServicesDialog() { }
        public bool Start(string serviceName) => true;
        public bool Stop(string serviceName) => true;
        public bool Pause(string serviceName) => false;
        public bool Continue(string serviceName) => false;
        public bool Restart(string serviceName) => true;
    }

    private sealed class Store : IServicesStore
    {
        private readonly ServiceStoreData _saved;

        public Store(IReadOnlyCollection<string> saved) => _saved = ServiceStoreData.FromLegacy(saved.ToList());

        public ServiceStoreData Load() => _saved;

        public void Save(ServiceStoreData data) { }
    }
}