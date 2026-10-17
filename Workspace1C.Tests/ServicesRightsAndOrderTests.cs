using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #11 (7OH 17.10): порядок служб задаёт пользователь (выше/ниже, новые — в конец),
/// чеклист «Настроить» — по алфавиту, выдача прав на управление службами одним запросом
/// повышения прав (SDDL, командный файл sc sdset, разбор результата).
/// </summary>
public sealed class ServicesRightsAndOrderTests
{
    private const string Sid = "S-1-5-21-1000-2000-3000-1001";

    // ---- SDDL ----

    [Fact]
    public void AddControlAce_InsertsAfterDaclFlags()
    {
        const string sddl = "D:(A;;CCLCSWRPWPDTLOCRSDRCWDWO;;;SY)(A;;CCLCSWLOCRRC;;;IU)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)";

        var updated = ServiceRights.AddControlAce(sddl, Sid);

        Assert.Equal(
            $"D:(A;;{ServiceRights.ControlRights};;;{Sid})(A;;CCLCSWRPWPDTLOCRSDRCWDWO;;;SY)(A;;CCLCSWLOCRRC;;;IU)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)",
            updated);
        Assert.True(ServiceRights.HasControlAce(updated!, Sid));
    }

    [Fact]
    public void AddControlAce_ProtectedDacl_KeepsFlags()
    {
        var updated = ServiceRights.AddControlAce("D:P(A;;GA;;;SY)", Sid);

        Assert.StartsWith($"D:P(A;;{ServiceRights.ControlRights};;;{Sid})", updated);
    }

    [Fact]
    public void AddControlAce_AlreadyPresent_Unchanged()
    {
        var sddl = $"D:(A;;RPWPLC;;;{Sid})(A;;GA;;;SY)";

        Assert.Equal(sddl, ServiceRights.AddControlAce(sddl, Sid));
    }

    [Fact]
    public void AddControlAce_AceInSaclOnly_DoesNotCount()
    {
        var sddl = $"D:(A;;GA;;;SY)S:(AU;FA;RPWP;;;{Sid})";

        Assert.False(ServiceRights.HasControlAce(sddl, Sid));
        Assert.StartsWith($"D:(A;;{ServiceRights.ControlRights};;;{Sid})", ServiceRights.AddControlAce(sddl, Sid));
    }

    [Fact]
    public void AddControlAce_NoDacl_ReturnsNull()
    {
        Assert.Null(ServiceRights.AddControlAce("O:SYG:SY", Sid));
        Assert.Null(ServiceRights.AddControlAce("", Sid));
    }

    [Fact]
    public void ParseSdShowOutput_TakesDescriptorLine()
    {
        Assert.Equal("D:(A;;GA;;;SY)", ServiceRights.ParseSdShowOutput("\r\nD:(A;;GA;;;SY)\r\n"));
        Assert.Null(ServiceRights.ParseSdShowOutput("[SC] OpenService FAILED 5:\r\nAccess is denied."));
    }

    [Fact]
    public void Batch_RedirectFirst_AndEscapesPercent()
    {
        var batch = ServiceRights.BuildBatch(new[] { ("MSSQLSERVER", "D:(A;;GA;;;S%Y)") }, @"C:\t\r.txt");

        Assert.Contains("sc.exe sdset \"MSSQLSERVER\" \"D:(A;;GA;;;S%%Y)\"", batch);
        Assert.Contains(">>\"C:\\t\\r.txt\" echo MSSQLSERVER=%errorlevel%", batch);
    }

    [Fact]
    public void ResultFile_ParsedAndApplied()
    {
        var codes = ServiceRights.ParseResultFile("a=0\r\nb=5\r\n__done__=0\r\n");
        var result = new ServiceRightsResult();

        ServiceRights.ApplyResults(result, new[] { "a", "b", "c" }, codes);

        Assert.Equal(new[] { "a" }, result.Granted);
        Assert.True(result.Failed.ContainsKey("b"));
        Assert.True(result.Failed.ContainsKey("c"));
    }

    [Theory]
    [InlineData("MSSQLSERVER", true)]
    [InlineData("1C:Enterprise 8.3 Server Agent (x86-64)", true)]
    [InlineData("bad&name", false)]
    [InlineData("bad\"name", false)]
    public void IsSafeName(string name, bool expected) => Assert.Equal(expected, ServiceRights.IsSafeName(name));

    // ---- Порядок ----

    [Fact]
    public void MergeOrder_KeepsUserOrder_AppendsNew()
    {
        var merged = ServicesWindowViewModel.MergeOrder(
            new[] { "c", "a", "b" },
            new[] { "a", "B", "d", "e" });

        Assert.Equal(new[] { "a", "b", "d", "e" }, merged);
    }

    private static ServiceInfo Info(string name) =>
        new() { ServiceName = name, DisplayName = name.ToUpperInvariant(), Status = ServiceStatus.Stopped };

    private sealed class RightsFake : IServiceControllerService
    {
        public Dictionary<string, ServiceInfo> Services { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<IReadOnlyList<string>> GrantCalls { get; } = new();
        public bool IsAvailable => true;
        public bool CanOpenSystemDialog => false;
        public bool CanGrantControlRights => true;
        public IReadOnlyList<ServiceInfo> GetAllServices() => Services.Values.ToList();
        public ServiceInfo GetStatus(string serviceName) =>
            Services.TryGetValue(serviceName, out var i) ? i : new ServiceInfo { ServiceName = serviceName, Status = ServiceStatus.NotFound };
        public void OpenSystemServicesDialog() { }
        public bool Start(string serviceName) => true;
        public bool Stop(string serviceName) => true;
        public bool Pause(string serviceName) => true;
        public bool Continue(string serviceName) => true;
        public bool Restart(string serviceName) => true;

        public ServiceRightsResult GrantControlRights(IReadOnlyList<string> serviceNames)
        {
            GrantCalls.Add(serviceNames.ToList());
            var result = new ServiceRightsResult();
            result.Granted.AddRange(serviceNames);
            return result;
        }
    }

    private static (ServicesWindowViewModel Vm, RightsFake Fake, ServicesWindowViewModelTests.FakeServicesStore Store,
        ServicesWindowViewModelTests.FakeDialogService Dialogs) Create(params string[] stored)
    {
        var fake = new RightsFake();
        foreach (var name in new[] { "a", "b", "c", "d" })
            fake.Services[name] = Info(name);
        var store = new ServicesWindowViewModelTests.FakeServicesStore { Stored = stored.ToList() };
        var dialogs = new ServicesWindowViewModelTests.FakeDialogService();
        return (new ServicesWindowViewModel(fake, store, dialogs), fake, store, dialogs);
    }

    [Fact]
    public async Task MoveUpDown_ReordersAndSaves()
    {
        var (vm, _, store, _) = Create("a", "b", "c");
        await vm.LoadAsync();

        vm.SelectedRow = vm.Rows[2];
        Assert.True(vm.CanMoveUp);
        Assert.False(vm.CanMoveDown);
        vm.MoveUpCommand.Execute(null);

        Assert.Equal(new[] { "a", "c", "b" }, vm.Rows.Select(r => r.ServiceName));
        Assert.Equal(new[] { "a", "c", "b" }, store.Saved);

        vm.SelectedRow = vm.Rows[0];
        Assert.False(vm.CanMoveUp);
        vm.MoveDownCommand.Execute(null);
        Assert.Equal(new[] { "c", "a", "b" }, store.Saved);
    }

    [Fact]
    public async Task ApplyConfiguration_NewServices_OfferRightsForNewOnly()
    {
        var (vm, fake, store, _) = Create("b", "a");
        await vm.LoadAsync();

        await vm.ApplyConfigurationAsync(new[] { "a", "b", "d" });

        Assert.Equal(new[] { "b", "a", "d" }, store.Saved);
        Assert.Single(fake.GrantCalls);
        Assert.Equal(new[] { "d" }, fake.GrantCalls[0]);
    }

    [Fact]
    public async Task GrantRightsCommand_AllServicesOfList()
    {
        var (vm, fake, _, _) = Create("a", "c");
        await vm.LoadAsync();

        await vm.GrantRightsAsync(vm.Rows.Select(r => r.ServiceName).ToList(), confirm: false);

        Assert.Equal(new[] { "a", "c" }, fake.GrantCalls.Single());
        Assert.Contains("a", vm.StatusText);
    }

    [Fact]
    public void FormatRightsResult_Mentions_AllParts()
    {
        var result = new ServiceRightsResult();
        result.Granted.Add("a");
        result.AlreadyGranted.Add("b");
        result.Failed["c"] = "5";

        var text = ServicesWindowViewModel.FormatRightsResult(result);

        Assert.Contains("a", text);
        Assert.Contains("b", text);
        Assert.Contains("c", text);
    }

    [Fact]
    public void Picker_SortedAlphabetically()
    {
        var fake = new RightsFake();
        fake.Services["z"] = new ServiceInfo { ServiceName = "z", DisplayName = "Альфа" };
        fake.Services["a"] = new ServiceInfo { ServiceName = "a", DisplayName = "Яблоко" };
        fake.Services["m"] = new ServiceInfo { ServiceName = "m", DisplayName = "бета" };
        var picker = new ServicesPickerViewModel(fake, new ServicesWindowViewModelTests.FakeServicesStore());

        Assert.Equal(new[] { "Альфа", "бета", "Яблоко" }, picker.AllServices.Select(r => r.DisplayName));
    }
}
