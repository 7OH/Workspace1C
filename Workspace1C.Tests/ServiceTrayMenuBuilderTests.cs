using System.Collections.Generic;
using System.Linq;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты построителя меню служб системного трея (issue #11, итерация 2 — группы
/// служб, релиз 1.0.0.12; требования 7OH к issue #11 от 13:30/13:47 и замечания
/// 7OH от 18.10): порядок пунктов (Настроить → разделитель → группы/службы по
/// Order → разделитель → Все сервисы), разделитель после «Настроить…», структура
/// команд с разделителями как в контекстном меню окна «Управление службами»
/// (Запустить/Остановить | Пауза/Восстановить | Перезапустить), состав подменю
/// группы (команды → разделитель → члены), доступность команд по статусам
/// (ServiceActionAvailability), статус группы в пункте, пустой список, глифы
/// команд (единый источник ServiceCommandGlyphs).
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

    /// <summary>Первый пункт заданного вида в плоском списке верхнего уровня.</summary>
    private static ServiceMenuItem First(IReadOnlyList<ServiceMenuItem> items, ServiceMenuItemKind kind)
    {
        var item = items.FirstOrDefault(i => i.Kind == kind);
        Assert.NotNull(item);
        return item!;
    }

    /// <summary>Команды подменю заданного вида (разделители отбрасываются).</summary>
    private static List<ServiceMenuItem> Commands(IReadOnlyList<ServiceMenuItem> children, ServiceMenuItemKind kind)
        => children.Where(c => c.Kind == kind).ToList();

    // ===================== Структура верхнего списка =====================

    [Fact]
    public void Build_SettingsFirstThenRowsByOrderThenSeparatorThenAllServices()
    {
        var items = Builder().Build(SampleData(), StatusOf, Windows);

        // 1) «Настроить…» — первым (требование 7OH: кнопка настройки сверху).
        Assert.Equal(ServiceMenuItemKind.Settings, items[0].Kind);

        // 2) После «Настроить…» — разделитель (замечание 7OH 18.10).
        Assert.Equal(ServiceMenuItemKind.Separator, items[1].Kind);

        // 3) Строки в порядке data.Order: сначала группа, затем служба web.
        Assert.Equal(ServiceMenuItemKind.GroupSubmenu, items[2].Kind);
        Assert.Equal("База данных", items[2].TargetName);
        Assert.Equal(ServiceMenuItemKind.ServiceSubmenu, items[3].Kind);
        Assert.Equal("web.service", items[3].TargetName);

        // 4) Разделитель и подменю «Все сервисы» в конце.
        Assert.Equal(ServiceMenuItemKind.Separator, items[4].Kind);
        Assert.Equal(ServiceMenuItemKind.AllServicesSubmenu, items[5].Kind);
        Assert.Equal(6, items.Count);
    }

    [Fact]
    public void Build_SeparatorAfterSettings_ForEmptyListOnlyOneSeparator()
    {
        // Пустой список: «Настроить…», разделитель и «Все сервисы» — лишних
        // разделителей нет (замечание 7OH 18.10: разделитель после «Настроить…»).
        var items = Builder().Build(new ServiceStoreData(), _ => ServiceStatus.Unknown, Windows);

        Assert.Equal(3, items.Count);
        Assert.Equal(ServiceMenuItemKind.Settings, items[0].Kind);
        Assert.Equal(ServiceMenuItemKind.Separator, items[1].Kind);
        Assert.Equal(ServiceMenuItemKind.AllServicesSubmenu, items[2].Kind);
        Assert.Single(items, i => i.IsSeparator);
    }

    // ===================== Подменю службы =====================

    [Fact]
    public void ServiceSubmenu_CommandsFollowFixedOrderAndCanExecuteDependsOnStatus()
    {
        var items = Builder().Build(SampleData(), StatusOf, Windows);
        var web = First(items, ServiceMenuItemKind.ServiceSubmenu); // web.service — Stopped
        var commands = Commands(web.Children, ServiceMenuItemKind.ServiceCommand);

        Assert.Equal(5, commands.Count);
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
    public void ServiceSubmenu_CommandsSeparatedLikeMainWindowContextMenu()
    {
        // Замечание 7OH 18.10: меню службы в трее — по виду как контекстное меню
        // строки окна «Управление службами»: Запустить/Остановить | Пауза/Восстановить |
        // Перезапустить (разделители между группами команд).
        var items = Builder().Build(SampleData(), StatusOf, Windows);
        var web = First(items, ServiceMenuItemKind.ServiceSubmenu);
        var children = web.Children;

        Assert.Equal(7, children.Count);
        Assert.Equal(ServiceAction.Start, children[0].Action);
        Assert.Equal(ServiceAction.Stop, children[1].Action);
        Assert.Equal(ServiceMenuItemKind.Separator, children[2].Kind);
        Assert.Equal(ServiceAction.Pause, children[3].Action);
        Assert.Equal(ServiceAction.Continue, children[4].Action);
        Assert.Equal(ServiceMenuItemKind.Separator, children[5].Kind);
        Assert.Equal(ServiceAction.Restart, children[6].Action);
    }

    [Fact]
    public void Linux_StoppedServiceRestartEnabled()
    {
        // Linux: systemctl restart допустим и для остановленного unit'а (раздел 3.1 плана).
        var data = new ServiceStoreData { Services = { "web.service" } };
        var items = Builder().Build(data, StatusOf, Linux);
        var web = First(items, ServiceMenuItemKind.ServiceSubmenu);
        var commands = Commands(web.Children, ServiceMenuItemKind.ServiceCommand);
        Assert.True(commands[4].IsEnabled); // Restart при Stopped
    }

    // ===================== Временное отключение (issue #37) =====================

    [Fact]
    public void DisabledService_NotInTopListAndNotInAllServices()
    {
        var data = SampleData();
        data.TemporarilyDisabledServices.Add("web.service");

        var items = Builder().Build(data, StatusOf, Windows);

        // Корневая служба отключена — её нет в верхнем списке (осталась только группа).
        Assert.DoesNotContain(items, i => i.Kind == ServiceMenuItemKind.ServiceSubmenu);
        Assert.Single(items, i => i.Kind == ServiceMenuItemKind.GroupSubmenu);
        // «Настроить…» остаётся на месте.
        Assert.Equal(ServiceMenuItemKind.Settings, items[0].Kind);
        Assert.True(items[0].IsEnabled);
        // «Все сервисы» строится только из не отключённых корневых служб — их нет.
        var all = items[^1];
        Assert.Equal(ServiceMenuItemKind.AllServicesSubmenu, all.Kind);
        Assert.False(all.IsEnabled);
        Assert.DoesNotContain(all.Children, c => c.TargetName == "web.service");
    }

    [Fact]
    public void DisabledGroup_HiddenEntirelyWithMembers()
    {
        var data = SampleData();
        data.TemporarilyDisabledGroups.Add("База данных");

        var items = Builder().Build(data, StatusOf, Windows);

        // Отключённая группа скрыта целиком — и её члены нигде не показываются,
        // в том числе в «Все сервисы».
        Assert.DoesNotContain(items, i => i.Kind == ServiceMenuItemKind.GroupSubmenu);
        Assert.Single(items, i => i.Kind == ServiceMenuItemKind.ServiceSubmenu);
        Assert.Equal("web.service", items.Single(i => i.Kind == ServiceMenuItemKind.ServiceSubmenu).TargetName);
        Assert.All(Flatten(items), i => Assert.NotEqual("pgsql.service", i.TargetName));
        Assert.All(Flatten(items), i => Assert.NotEqual("redis.service", i.TargetName));
        var all = items[^1];
        Assert.DoesNotContain(all.Children, c => c.TargetName == "pgsql.service");
    }

    [Fact]
    public void DisabledGroupMember_HiddenFromGroupButGroupRemains()
    {
        var data = SampleData();
        data.TemporarilyDisabledServices.Add("pgsql.service");

        var items = Builder().Build(data, StatusOf, Windows);
        var group = First(items, ServiceMenuItemKind.GroupSubmenu);
        Assert.Equal("База данных", group.TargetName);

        // Подменю членов: только redis.service (pgsql.service отключена отдельно).
        var submenuMembers = group.Children
            .Where(c => c.Kind == ServiceMenuItemKind.ServiceSubmenu)
            .ToList();
        var member = Assert.Single(submenuMembers);
        Assert.Equal("redis.service", member.TargetName);
        // Команды группы («Запустить все» и т.п.) целятся только в не отключённых членов.
        var groupCommands = Commands(group.Children, ServiceMenuItemKind.GroupCommand);
        Assert.NotEmpty(groupCommands);
        Assert.All(groupCommands, c => Assert.Equal(new[] { "redis.service" }, c.ServiceNames));
    }

    [Fact]
    public void AllServices_ContainsOnlyEnabledRootServices()
    {
        var data = new ServiceStoreData
        {
            Services = { "web.service", "extra.service" },
            Order = { "web.service", "extra.service" }
        };
        data.TemporarilyDisabledServices.Add("extra.service");

        var items = Builder().Build(data, StatusOf, Windows);
        var all = items[^1];
        Assert.Equal(ServiceMenuItemKind.AllServicesSubmenu, all.Kind);

        var commands = Commands(all.Children, ServiceMenuItemKind.AllServicesCommand);
        Assert.NotEmpty(commands);
        // «Все сервисы» охватывают только не отключённые службы общего списка.
        Assert.All(commands, c => Assert.Equal(new[] { "web.service" }, c.ServiceNames));
        Assert.All(commands, c => Assert.DoesNotContain("extra.service", c.ServiceNames));
    }

    [Fact]
    public void AllDisabled_OnlySettingsAndAllServicesRemain()
    {
        var data = new ServiceStoreData
        {
            Services = { "web.service" },
            Groups = { new ServiceGroup { Name = "g", Services = { "pgsql.service" } } },
            Order = { ServiceStoreData.GroupKey("g"), "web.service" },
            TemporarilyDisabledServices = { "web.service", "pgsql.service" },
            TemporarilyDisabledGroups = { "g" }
        };

        var items = Builder().Build(data, StatusOf, Windows);

        // «Настроить…» остаётся доступным всегда — иначе нельзя вернуть галочки.
        Assert.Equal(ServiceMenuItemKind.Settings, items[0].Kind);
        Assert.True(items[0].IsEnabled);
        Assert.Equal(3, items.Count); // Настроить…, разделитель, «Все сервисы»
        Assert.Single(items, i => i.IsSeparator);
    }

    /// <summary>Плоский обход всех пунктов меню (включая детей подменю).</summary>
    private static IEnumerable<ServiceMenuItem> Flatten(IEnumerable<ServiceMenuItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in Flatten(item.Children))
                yield return child;
        }
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

        var mail = First(items, ServiceMenuItemKind.ServiceSubmenu);
        var commands = Commands(mail.Children, ServiceMenuItemKind.ServiceCommand);
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
        var group = First(items, ServiceMenuItemKind.GroupSubmenu);
        var children = group.Children;

        // 5 команд группы (с разделителями между группами команд) + разделитель
        // перед членами + 2 члена (в порядке списка группы).
        var commands = Commands(children, ServiceMenuItemKind.GroupCommand);
        Assert.Equal(5, commands.Count);
        Assert.Equal(
            new[] { ServiceAction.Start, ServiceAction.Stop, ServiceAction.Pause, ServiceAction.Continue, ServiceAction.Restart },
            commands.Select(c => c.Action!.Value));

        // Разделители: после Остановить, после Восстановить и перед членами.
        Assert.Equal(3, children.Count(c => c.IsSeparator));
        var memberSeparator = children.Last(c => c.IsSeparator);
        var memberStart = children.IndexOf(memberSeparator) + 1;
        Assert.Equal(ServiceMenuItemKind.ServiceSubmenu, children[memberStart].Kind);
        Assert.Equal("pgsql.service", children[memberStart].TargetName);
        Assert.Equal(ServiceMenuItemKind.ServiceSubmenu, children[memberStart + 1].Kind);
        Assert.Equal("redis.service", children[memberStart + 1].TargetName);

        // Все команды группы направлены на группу и содержат список членов в порядке группы.
        Assert.All(commands, c =>
        {
            Assert.Equal(ServiceMenuItemTarget.Group, c.Target);
            Assert.Equal("База данных", c.TargetName);
            Assert.Equal(new[] { "pgsql.service", "redis.service" }, c.ServiceNames);
        });

        // Статусы членов: pgsql Running + redis Stopped → Start (redis) и Stop (pgsql) доступны.
        Assert.True(commands[0].IsEnabled); // Start
        Assert.True(commands[1].IsEnabled); // Stop
    }

    [Fact]
    public void GroupSubmenu_StatusReflectedInItem()
    {
        // pgsql Running + redis Stopped → Partial: жёлтый кружок и текст статуса.
        var items = Builder().Build(SampleData(), StatusOf, Windows);
        var group = First(items, ServiceMenuItemKind.GroupSubmenu);
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
        Assert.Equal("#22C55E", First(allRunning, ServiceMenuItemKind.GroupSubmenu).StatusColorHex);

        // Ни один не Running → серый кружок.
        var noneRunning = Builder().Build(
            new ServiceStoreData
            {
                Groups = { new ServiceGroup { Name = "Нет", Services = { "a.service", "b.service" } } }
            },
            _ => ServiceStatus.Stopped,
            Windows);
        Assert.Equal("#6B7280", First(noneRunning, ServiceMenuItemKind.GroupSubmenu).StatusColorHex);
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
        var group = First(items, ServiceMenuItemKind.GroupSubmenu);

        Assert.Equal(ServiceMenuItemKind.GroupSubmenu, group.Kind);
        Assert.Equal("#6B7280", group.StatusColorHex);

        // Все 5 команд группы недоступны.
        Assert.All(Commands(group.Children, ServiceMenuItemKind.GroupCommand), c =>
        {
            Assert.Equal(ServiceMenuItemKind.GroupCommand, c.Kind);
            Assert.False(c.IsEnabled);
        });

        // Член-подменю — красный (NotFound), все его команды тоже недоступны.
        var member = group.Children.Last(c => c.Kind == ServiceMenuItemKind.ServiceSubmenu);
        Assert.Equal("#EF4444", member.StatusColorHex);
        Assert.All(Commands(member.Children, ServiceMenuItemKind.ServiceCommand), c => Assert.False(c.IsEnabled));
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

        var commands = Commands(all.Children, ServiceMenuItemKind.AllServicesCommand);

        // Start (web Stopped), Stop (pgsql Running) и Restart (pgsql Running);
        // Pause/Continue неприменимы (CanPauseAndContinue у обеих нет) — отсутствуют.
        Assert.Equal(
            new[] { ServiceAction.Start, ServiceAction.Stop, ServiceAction.Restart },
            commands.Select(c => c.Action!.Value));

        Assert.All(commands, c =>
        {
            Assert.Equal(ServiceMenuItemKind.AllServicesCommand, c.Kind);
            Assert.Equal(ServiceMenuItemTarget.All, c.Target);
            Assert.Equal(new[] { "pgsql.service", "web.service" }, c.ServiceNames);
        });
    }

    [Fact]
    public void AllServices_CommandsSeparatedLikeMainWindowMenu()
    {
        // Замечание 7OH 18.10: меню «Все сервисы» в трее — по виду как меню «Все
        // сервисы» окна «Управление службами»: Запустить/Остановить | ... | Перезапустить.
        var data = new ServiceStoreData
        {
            Services = { "web.service", "db.service" },
            Order = { "web.service", "db.service" }
        };
        // web.service — Stopped, db.service — Running (обе без поддержки паузы).
        var items = Builder().Build(
            data,
            name => name switch
            {
                "web.service" => ServiceStatus.Stopped,
                "db.service" => ServiceStatus.Running,
                _ => ServiceStatus.Unknown
            },
            Windows);
        var all = items[items.Count - 1];

        // Start (web Stopped) + Stop (db Running) + Restart (db Running) — без
        // Pause/Continue: средняя группа пуста — двойных разделителей нет.
        var children = all.Children;
        Assert.Equal(4, children.Count);
        Assert.Equal(ServiceAction.Start, children[0].Action);
        Assert.Equal(ServiceAction.Stop, children[1].Action);
        Assert.Equal(ServiceMenuItemKind.Separator, children[2].Kind);
        Assert.Equal(ServiceAction.Restart, children[3].Action);
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
        var commands = Commands(all.Children, ServiceMenuItemKind.AllServicesCommand);
        Assert.True(commands.Single(c => c.Action == ServiceAction.Restart).IsEnabled);
        Assert.True(commands.Single(c => c.Action == ServiceAction.Start).IsEnabled);
        Assert.DoesNotContain(commands, c => c.Action == ServiceAction.Stop);
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

        var mail = First(items, ServiceMenuItemKind.ServiceSubmenu);
        var commands = Commands(mail.Children, ServiceMenuItemKind.ServiceCommand);
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
        // разделитель — после «Настроить…» (замечание 7OH 18.10), лишних нет.
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

    // ===================== Глифы команд (замечание 7OH 18.10) =====================

    [Fact]
    public void CommandGlyphs_ContinueDiffersFromStartByVerticalBar()
    {
        // «Восстановить» — треугольник «Запустить» + палочка справа (7OH 18.10):
        // глиф Continue начинается с глифа Start и отличается добавленной чертой.
        var start = ServiceCommandGlyphs.Glyph(ServiceAction.Start);
        var cont = ServiceCommandGlyphs.Glyph(ServiceAction.Continue);
        Assert.Equal(ServiceCommandGlyphs.StartGlyph, start);
        Assert.StartsWith(start, cont);
        Assert.Contains("|", cont);
        Assert.NotEqual(start, cont);
    }

    [Fact]
    public void CommandGlyphs_AllActionsHaveGlyphAndColor()
    {
        foreach (var action in new[] { ServiceAction.Start, ServiceAction.Stop, ServiceAction.Pause, ServiceAction.Continue, ServiceAction.Restart })
        {
            Assert.False(string.IsNullOrWhiteSpace(ServiceCommandGlyphs.Glyph(action)));
            Assert.False(string.IsNullOrWhiteSpace(ServiceCommandGlyphs.ColorHex(action)));
        }

        // Цвета соответствуют кнопкам панели окна «Управление службами».
        Assert.Equal("#16A34A", ServiceCommandGlyphs.ColorHex(ServiceAction.Start));
        Assert.Equal("#DC2626", ServiceCommandGlyphs.ColorHex(ServiceAction.Stop));
        Assert.Equal("#D97706", ServiceCommandGlyphs.ColorHex(ServiceAction.Pause));
        Assert.Equal("#0EA5E9", ServiceCommandGlyphs.ColorHex(ServiceAction.Continue));
        Assert.Equal("#2563EB", ServiceCommandGlyphs.ColorHex(ServiceAction.Restart));
    }
}