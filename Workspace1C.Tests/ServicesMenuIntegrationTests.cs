using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Интеграция пункта меню «Управление службами» (issue #11): пункт каталога
/// <c>Services</c> стоит сразу после «Серверы 1С», имеет CommandKind=Services,
/// хоткей HotkeyServices и попадает в стандартную компоновку; дефолт хоткея —
/// Ctrl+Shift+S (проверяется конструктором AppSettings).
/// </summary>
public sealed class ServicesMenuIntegrationTests
{
    [Fact]
    public void Catalog_HasServicesItemWithCommandKindAndHotkey()
    {
        var item = UtilitiesMenuCatalog.Find("Services");
        Assert.NotNull(item);
        Assert.Equal(UtilitiesMenuCommandKind.Services, item!.CommandKind);
        Assert.Equal("HotkeyServices", item.HotkeyKey);
        Assert.True(item.IsNew); // при пользовательской компоновке — в «Новинки программы»
    }

    [Fact]
    public void Catalog_ServicesItem_IsRightAfterServerMonitor()
    {
        var all = UtilitiesMenuCatalog.All.ToList();
        var servicesIndex = all.FindIndex(x => x.Id == "Services");
        var serverIndex = all.FindIndex(x => x.Id == "ServerMonitor");

        Assert.True(serverIndex >= 0);
        Assert.Equal(serverIndex + 1, servicesIndex);
    }

    [Fact]
    public void DefaultLayout_ContainsServices_AfterServerMonitor()
    {
        var layout = UtilitiesMenuLayouts.Default();
        var ids = Flatten(layout.Root).ToList();

        var servicesIndex = ids.IndexOf("Services");
        var serverIndex = ids.IndexOf("ServerMonitor");

        Assert.True(serverIndex >= 0);
        Assert.Equal(serverIndex + 1, servicesIndex);
    }

    [Fact]
    public void AppSettings_HotkeyServices_DefaultIsCtrlShiftS()
    {
        var settings = new AppSettings();
        Assert.Equal("Ctrl+Shift+S", settings.HotkeyServices);
    }

    [Fact]
    public void AppSettings_Default_DoesNotConflictWithServerConsoleHotkey()
    {
        // «Серверы 1С» — Ctrl+Alt+S (монитор), «Управление службами» — Ctrl+Shift+S:
        // дефолты не конфликтуют.
        var settings = new AppSettings();
        Assert.Equal("Ctrl+Alt+S", settings.HotkeyServerConsole);
        Assert.NotEqual(
            settings.HotkeyServerConsole.Trim(),
            settings.HotkeyServices.Trim());
    }

    private static IEnumerable<string> Flatten(IEnumerable<UtilitiesMenuNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is null)
                continue;
            if (node.Kind == "item")
            {
                if (!string.IsNullOrWhiteSpace(node.ItemId))
                    yield return node.ItemId!;
            }
            else if (node.Kind == "group" && node.Children is not null)
            {
                foreach (var child in Flatten(node.Children))
                    yield return child;
            }
        }
    }
}