using System.Collections.Generic;
using System.Linq;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты построителя меню служб системного трея (issue #11, итерация 2 — группы
/// служб, релиз 1.0.0.12; требования 7OH к issue #11 от 13:30/13:47): порядок
/// пунктов (Настроить → группы/службы по Order → разделитель → Все сервисы),
/// состав подменю группы (команды → разделитель → члены), доступность команд по
/// статусам (ServiceActionAvailability), статус группы в пункте, пустой список.
/// </summary>
public sealed class ServiceTrayMenuBuilderTests
{
    private const ServicePlatform Windows = ServicePlatform.Windows;
    private const ServicePlatform Linux = ServicePlatform.Linux;

    private static ServiceTrayMenuBuilder Builder() => new();

    /// <summary>Пример данных: группа «База данных» (pgsql + redis) и служба web; порядок смешанный.</summary>
    private static ServiceStoreData SampleData() => new()
    {
        Services = { "web.service" },
        Groups =
        {
            new ServiceGroup { Name = "База данных", Services = { "pgsql.service", "redis.service" } }
        },
        Order = { ServiceStoreData.GroupKey("База данных"), "web.service" }
    };

    private static ServiceStatus StatusOf(string name) => name switch
    {
        "web.service" => ServiceStatus.Stopped,
        "pgsql.service" => ServiceStatus.Running,
        "redis.service" => ServiceStatus.Stopped,
        _ => ServiceStatus.Unknown
    };

    // ===================== Структура верхнего списка =====================

    [Fact]
    public void Build_SettingsFirstThenRowsByOrderThenSeparatorThenAllServices()
    {
        var items = Builder().Build(SampleData(), StatusOf, Windows);

        // 1) «Настроить…» — первым (требование 7OH: кнопка настройки сверху).
        Assert.Equal(ServiceMenuItemKind.Settings, items[0].Kind);

        // 2) Строки в порядке data.Order: сначала группа, затем служба web.
        Assert.Equal(ServiceMenuItemKind.GroupSubmenu, items[1].Kind);
        Assert.Equal("База данных", items[1].TargetName);
        Assert.Equal(ServiceMenuItemKind.ServiceSubmenu, items[2].Kind);
        Assert.Equal("web.service", items[2].TargetName);

        // 3) Разделитель и подменю «Все сервисы» в конце.
        Assert.Equal(ServiceMenuItemKind.Separator, items[3].Kind);
        Assert.Equal(ServiceMenuItemKind.AllServicesSubmenu, items[4].Kind);
        Assert.Equal(5, items.Count);
    }

    // ===================== Подменю службы =====================

    [Fact]
    public void ServiceSubmenu_CommandsFollowFixedOrderAndCanExecuteDependsOnStatus()
    {
        var items = Builder().Build(SampleData(), StatusOf, Windows);
        var web = items[2]; // web.service — Stopped
        var commands = web.Children;

        Assert.Equal(5, commands.Count);
        Assert.All(commands, c => Assert.Equal(ServiceMenuItemKind.ServiceCommand, c.Kind));
        Assert.All(commands, c => Assert.Equal(ServiceMenuItemTarget.Service, c.Target));
        Assert.All(commands, c => Assert.Equal("web.service", c.TargetName));
        Assert.Equal(
            new[] { ServiceAction.Start, ServiceAction.Stop, ServiceAction.Pause, ServiceAction.Continue, ServiceAction.Restart },
            commands.Select(c => c.Action!.Value));

        // Stopped на Windows: доступен только «Запустить»; «Перезапустить» — только для Running.
        Assert.True(commands[0].IsEnabled);   // Start
        Assert.False(commands[1].IsEnabled);  // Stop
        Assert.False(commands[2].IsEnabled);  // Pause
        Assert.False(commands[3].IsEnabled);  // Continue
        Assert.False(commands[4].IsEnabled);  // Restart
    }

    [Fact]
    public void Linux_StoppedServiceRestartEnabled()
    {
        // Linux: systemctl restart допустим и для остановленного unit'а (раздел 3.1 плана).
        var data = new ServiceStoreData { Services = { "web.service" } };
        var items = Builder().Build(data, StatusOf, Linux);
        var commands = items[1].Children; // ServiceSubmenu(web)
        Assert.True(commands[4].IsEnabled); // Restart при Stopped
    }

    [Fact]
    public void Build_WithServiceInfo_RespectsCanPauseAndContinue()
    {
        // Полная перегрузка (Func<string, ServiceInfo>): Running + поддержка паузы →
        // «Пауза» доступна, «Восстановить» — нет (служба не на паузе).
        var data = new ServiceStoreData { Services = { "mail.service" } };
        var items = Builder().Build(
            data,
            _ => new ServiceInfo
            {
                ServiceName = "mail.service",
                Status = ServiceStatus.Running,
                CanPauseAndContinue = true,
                CanStop = true
            },
            Windows);

        var commands = items[1].Children;
        Assert.False(commands[0].IsEnabled); // Start (Running)
        Assert.True(commands[1].IsEnabled);  // Stop
        Assert.True(commands[2].IsEnabled);  // Pause
        Assert.False(commands[3].IsEnabled); // Continue (не Paused)
        Assert.True(commands[4].IsEnabled);  // Restart
    }

    // ===================== Подменю группы =====================

    [Fact]
    public void GroupSubmenu_CommandsFirstThenSeparatorThenMembersInOrder()
    {
        var items = Builder().Build(SampleData(), StatusOf, Windows);
        var group = items[1];
        var children = group.Children;

        // 5 команд группы + разделитель + 2 члена (в порядке списка группы).
        Assert.Equal(8, children.Count);
        Assert.Equal(Enumerable.Repeat(ServiceMenuItemKind.GroupCommand, 5), children.Take(5).Select(c => c.Kind));
        Assert.Equal(ServiceMenuItemKind.Separator, children[5].Kind);
        Assert.Equal(ServiceMenuItemKind.ServiceSubmenu, children[6].Kind);
        Assert.Equal("pgsql.service", children[6].TargetName);
        Assert.Equal(ServiceMenuItemKind.ServiceSubmenu, children[7].Kind);
        Assert.Equal("redis.service", children[7].TargetName);

        // Все команды группы направлены на группу и содержат список членов в порядке группы.
        Assert.All(children.Take(5), c =>
        {
            Assert.Equal(ServiceMenuItemTarget.Group, c.Target);
            Assert.Equal("База данных", c.TargetName);
            Assert.Equal(new[] { "pgsql.service", "redis.service" }, c.ServiceNames);
        });

        // Статусы членов: pgsql Running + redis Stopped → Start (redis) и Stop (pgsql) доступны.
        Assert.True(children[0].IsEnabled); // Start
        Assert.True(children[1].IsEnabled); // Stop
    }

    [Fact]
    public void GroupSubmenu_StatusReflectedInItem()
    {
        // pgsql Running + redis Stopped → Partial: жёлтый кружок и текст статуса.
        var items = Builder().Build(SampleData(), StatusOf, Windows);
        var group = items[1];
        Assert.Equal("#F59E0B", group.StatusColorHex);
        Assert.False(string.IsNullOrEmpty(group.StatusText));

        // Все члены Running → зелёный кружок.
        var allRunning = Builder().Build(
            new ServiceStoreData
            {
                Groups = { new ServiceGroup { Name = "Все", Services = { "a.service", "b.service" } } }
            },
            _ => ServiceStatus.Running,
            Windows);
        Assert.Equal("#22C55E", allRunning[1].StatusColorHex);

        // Ни один не Running → серый кружок.
        var noneRunning = Builder().Build(
            new ServiceStoreData
            {
                Groups = { new ServiceGroup { Name = "Нет", Services = { "a.service", "b.service" } } }
            },
            _ => ServiceStatus.Stopped,
            Windows);
        Assert.Equal("#6B7280", noneRunning[1].StatusColorHex);
    }

    [Fact]
    public void GroupWithNotFoundMembers_StatusNone_AllCommandsDisabled()
    {
        // Группа, все члены которой не найдены (NotFound): агрегированный статус — None
        // (серый кружок), и ни одна команда группы неприменима — NotFound не подходит
        // ни под один статус таблицы ServiceActionAvailability (Задача 5, issue #11).
        var data = new ServiceStoreData
        {
            Groups =
            {
                new ServiceGroup { Name = "Пропавшие", Services = { "gone-1.service", "gone-2.service" } }
            }
        };

        var items = Builder().Build(data, _ => ServiceStatus.NotFound, Windows);
        var group = items[1];

        Assert.Equal(ServiceMenuItemKind.GroupSubmenu, group.Kind);
        Assert.Equal("#6B7280", group.StatusColorHex);

        // Все 5 команд группы недоступны.
        Assert.All(group.Children.Take(5), c =>
        {
            Assert.Equal(ServiceMenuItemKind.GroupCommand, c.Kind);
            Assert.False(c.IsEnabled);
        });

        // Член-подменю — красный (NotFound), все его команды тоже недоступны.
        var member = group.Children[6];
        Assert.Equal(ServiceMenuItemKind.ServiceSubmenu, member.Kind);
        Assert.Equal("#EF4444", member.StatusColorHex);
        Assert.All(member.Children, c => Assert.False(c.IsEnabled));
    }

    // ===================== «Все сервисы» =====================

    [Fact]
    public void AllServices_CommandsTargetAllListedServices()
    {
        var data = new ServiceStoreData
        {
            Services = { "web.service", "pgsql.service" },
            Order = { "pgsql.service", "web.service" }
        };
        var items = Builder().Build(data, StatusOf, Windows);

        var all = items[items.Count - 1];
        Assert.Equal(ServiceMenuItemKind.AllServicesSubmenu, all.Kind);

        // Start (web Stopped), Stop (pgsql Running) и Restart (pgsql Running);
        // Pause/Continue неприменимы (CanPauseAndContinue у обеих нет) — отсутствуют.
        Assert.Equal(
            new[] { ServiceAction.Start, ServiceAction.Stop, ServiceAction.Restart },
            all.Children.Select(c => c.Action!.Value));

        Assert.All(all.Children, c =>
        {
            Assert.Equal(ServiceMenuItemKind.AllServicesCommand, c.Kind);
            Assert.Equal(ServiceMenuItemTarget.All, c.Target);
            Assert.Equal(new[] { "pgsql.service", "web.service" }, c.ServiceNames);
        });
    }

    [Fact]
    public void Linux_AllServices_RestartEnabledForStopped()
    {
        // Linux: systemctl restart допустим и для остановленных unit'ов — подменю
        // «Все сервисы» включает «Перезапустить», даже когда ВСЕ службы Stopped
        // (платформенное правило, Задача 5, issue #11).
        var data = new ServiceStoreData { Services = { "web.service", "db.service" } };
        var items = Builder().Build(data, _ => ServiceStatus.Stopped, Linux);

        var all = items[items.Count - 1];
        Assert.Equal(ServiceMenuItemKind.AllServicesSubmenu, all.Kind);
        Assert.True(all.IsEnabled);
        Assert.True(all.Children.Single(c => c.Action == ServiceAction.Restart).IsEnabled);
        Assert.True(all.Children.Single(c => c.Action == ServiceAction.Start).IsEnabled);
        Assert.DoesNotContain(all.Children, c => c.Action == ServiceAction.Stop);
    }

    [Fact]
    public void Build_WithServiceInfo_CanStopFalse_Running_StopDisabled()
    {
        // Перегрузка с Func<string, ServiceInfo>: у Running-службы с CanStop=false
        // «Остановить» недоступен, «Пауза» при CanPauseAndContinue=true — доступна
        // (Задача 5, issue #11).
        var data = new ServiceStoreData { Services = { "mail.service" } };
        var items = Builder().Build(
            data,
            _ => new ServiceInfo
            {
                ServiceName = "mail.service",
                Status = ServiceStatus.Running,
                CanPauseAndContinue = true,
                CanStop = false
            },
            Windows);

        var commands = items[1].Children;
        Assert.False(commands[1].IsEnabled); // Stop: CanStop=false
        Assert.True(commands[2].IsEnabled);  // Pause: CanPauseAndContinue=true и Running
        Assert.True(commands[4].IsEnabled);  // Restart: Running
    }

    // ===================== Пустой список =====================

    [Fact]
    public void EmptyList_OnlySettingsSeparatorAndAllServices()
    {
        var items = Builder().Build(new ServiceStoreData(), _ => ServiceStatus.Unknown, Windows);

        // Только «Настроить…», разделитель и «Все сервисы» (без активных команд).
        Assert.Equal(3, items.Count);
        Assert.Equal(ServiceMenuItemKind.Settings, items[0].Kind);
        Assert.True(items[0].IsEnabled);
        Assert.Equal(ServiceMenuItemKind.Separator, items[1].Kind);
        Assert.Equal(ServiceMenuItemKind.AllServicesSubmenu, items[2].Kind);
        Assert.Empty(items[2].Children);
        // «Все сервисы» без служб неактивно (Задача 5, issue #11); ровно один
        // разделитель — перед «Все сервисы», лишние разделители не висят.
        Assert.False(items[2].IsEnabled);
        Assert.Single(items, i => i.IsSeparator);
    }

    [Fact]
    public void SettingsFirst_AllServicesLast_InFilledAndEmptyLists()
    {
        // «Настроить…» всегда первый пункт, «Все сервисы» — последний (перед ним
        // разделитель) — как для заполненного, так и для пустого списка
        // (Задача 5, issue #11).
        var filled = Builder().Build(SampleData(), StatusOf, Windows);
        Assert.Equal(ServiceMenuItemKind.Settings, filled[0].Kind);
        Assert.Equal(ServiceMenuItemKind.Separator, filled[^2].Kind);
        Assert.Equal(ServiceMenuItemKind.AllServicesSubmenu, filled[^1].Kind);

        var empty = Builder().Build(new ServiceStoreData(), _ => ServiceStatus.Unknown, Windows);
        Assert.Equal(ServiceMenuItemKind.Settings, empty[0].Kind);
        Assert.Equal(ServiceMenuItemKind.Separator, empty[^2].Kind);
        Assert.Equal(ServiceMenuItemKind.AllServicesSubmenu, empty[^1].Kind);
    }
}