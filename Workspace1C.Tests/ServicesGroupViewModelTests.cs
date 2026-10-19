using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты групп служб окна «Управление службами» (issue #11, итерация 2 — группы служб,
/// релиз 1.0.0.12): смешанные строки списка (службы + группы), агрегированный статус
/// группы (все запущены / частично / не запущены), команды группы (по всем членам
/// в порядке списка), добавление/изменение/удаление групп через провайдер
/// (<see cref="ServicesWindowViewModel.GroupEditorProvider"/>), перемещение групп
/// с сохранением порядка в <see cref="ServiceStoreData.Order"/>. Полный набор тестов
/// статусов — Задача 5.
/// </summary>
public sealed class ServicesGroupViewModelTests
{
    [Fact]
    public async Task LoadAsync_WithGroups_BuildsMixedRows_AndResolvesGroupStatus()
    {
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Running);
        env.Fake.Services["c"] = Info("c", "C", ServiceStatus.Stopped);
        env.Fake.Services["d"] = Info("d", "D", ServiceStatus.Stopped);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "a" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Все", Services = new[] { "a", "b" }.ToList() },
                new ServiceGroup { Name = "Часть", Services = new[] { "b", "c" }.ToList() },
                new ServiceGroup { Name = "Нет", Services = new[] { "c", "d" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Все", "a", "group:Часть", "group:Нет" }.ToList()
        };

        await env.Vm.LoadAsync();

        Assert.Equal(4, env.Vm.Rows.Count);
        Assert.Equal(3, env.Vm.Rows.OfType<ServiceGroupRow>().Count());

        // Все члены Running → AllRunning, зелёный кружок.
        var all = env.Vm.Rows.OfType<ServiceGroupRow>().Single(g => g.Name == "Все");
        Assert.Equal(GroupStatusKind.AllRunning, all.GroupStatus);
        Assert.Equal("#22C55E", all.StatusColorHex);

        // Не все, но хотя бы один Running → Partial, жёлтый кружок (обязательное требование 7OH).
        var partial = env.Vm.Rows.OfType<ServiceGroupRow>().Single(g => g.Name == "Часть");
        Assert.Equal(GroupStatusKind.Partial, partial.GroupStatus);
        Assert.Equal("#F59E0B", partial.StatusColorHex);

        // Ни одного Running → None, серый кружок.
        var none = env.Vm.Rows.OfType<ServiceGroupRow>().Single(g => g.Name == "Нет");
        Assert.Equal(GroupStatusKind.None, none.GroupStatus);
        Assert.Equal("#6B7280", none.StatusColorHex);

        // Порядок строк — из Order (смешанный: группа, служба, группа, группа).
        Assert.Equal("Все", ((ServiceGroupRow)env.Vm.Rows[0]).Name);
        Assert.Equal("a", ((ServiceInfoRow)env.Vm.Rows[1]).ServiceName);
        Assert.Equal("Часть", ((ServiceGroupRow)env.Vm.Rows[2]).Name);
        Assert.Equal("Нет", ((ServiceGroupRow)env.Vm.Rows[3]).Name);
    }

    [Fact]
    public void Resolver_AllRunning_Partial_None_Unknown()
    {
        Assert.Equal(GroupStatusKind.AllRunning,
            ServiceGroupStatusResolver.Resolve(new[] { ServiceStatus.Running, ServiceStatus.Running }));
        Assert.Equal(GroupStatusKind.Partial,
            ServiceGroupStatusResolver.Resolve(new[] { ServiceStatus.Running, ServiceStatus.Stopped }));
        // NotFound-член считается «не запущен» (решение по issue #11, итерация 2).
        Assert.Equal(GroupStatusKind.None,
            ServiceGroupStatusResolver.Resolve(new[] { ServiceStatus.Stopped, ServiceStatus.NotFound }));
        Assert.Equal(GroupStatusKind.Unknown,
            ServiceGroupStatusResolver.Resolve(Array.Empty<ServiceStatus>()));
    }

    [Fact]
    public async Task StartGroupCommand_ExecutesOnAllMembersInOrder()
    {
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Stopped);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Stopped);
        env.Fake.Services["c"] = Info("c", "C", ServiceStatus.Stopped);
        env.Store.Stored = new ServiceStoreData
        {
            Groups = new[]
            {
                new ServiceGroup { Name = "Группа", Services = new[] { "c", "a", "b" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Группа" }.ToList()
        };

        await env.Vm.LoadAsync();
        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceGroupRow>().Single();

        await env.Vm.ExecuteGroupCommandAsync(ServiceAction.Start);

        // Все члены запущены В ПОРЯДКЕ списка группы (c, a, b).
        Assert.Equal(new[] { ("Start", "c"), ("Start", "a"), ("Start", "b") }, env.Fake.Calls);
        Assert.False(env.Vm.IsBusy);
    }

    [Fact]
    public async Task DeleteGroupCommand_RemovesGroupFromStoreAndRows()
    {
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "a" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Группа", Services = new[] { "a" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Группа", "a" }.ToList()
        };

        await env.Vm.LoadAsync();
        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceGroupRow>().Single();

        await env.Vm.DeleteGroupAsync();

        Assert.Empty(env.Store.Stored.Groups);
        Assert.DoesNotContain("group:Группа", env.Store.Stored.Order);
        Assert.Single(env.Vm.Rows);
        Assert.IsType<ServiceInfoRow>(env.Vm.Rows[0]);
    }

    [Fact]
    public async Task AddGroup_InsertsNewGroupAtStartOfList()
    {
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Stopped);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "a" }.ToList(),
            Order = new[] { "a" }.ToList()
        };
        env.Vm.GroupEditorProvider = (group, available) =>
            new ServiceGroup { Name = "Новая", Services = new[] { "b" }.ToList() };

        await env.Vm.LoadAsync();
        await env.Vm.AddGroupAsync();

        // Новая группа — в НАЧАЛО списка (комментарий 7OH к issue #11).
        Assert.IsType<ServiceGroupRow>(env.Vm.Rows[0]);
        Assert.Equal("Новая", ((ServiceGroupRow)env.Vm.Rows[0]).Name);
        Assert.Equal("group:Новая", env.Store.Stored.Order[0]);
        Assert.Equal(2, env.Vm.Rows.Count);
    }

    [Fact]
    public async Task EditGroup_UpdatesGroupAndKeepsPosition()
    {
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Stopped);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "a" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Старая", Services = new[] { "a" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Старая", "a" }.ToList()
        };
        env.Vm.GroupEditorProvider = (group, available) =>
            new ServiceGroup { Name = "Новая", Services = new[] { "a", "b" }.ToList() };

        await env.Vm.LoadAsync();
        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceGroupRow>().Single();

        await env.Vm.EditGroupAsync();

        var group = env.Store.Stored.Groups.Single();
        Assert.Equal("Новая", group.Name);
        Assert.Equal(new[] { "a", "b" }, group.Services);
        // Позиция группы сохранена; ключ Order обновлён при переименовании. Члены группы
        // (a, b) — только внутри папки: корневых ключей «a»/«b» нет (инвариант «служба
        // ровно в одном месте», issue #11, 1.0.0.14).
        Assert.Equal(new[] { "group:Новая" }, env.Store.Stored.Order);
        Assert.Equal("Новая", ((ServiceGroupRow)env.Vm.Rows[0]).Name);
        Assert.Equal(new[] { "a", "b" }, ((ServiceGroupRow)env.Vm.Rows[0]).Children.Select(r => r.ServiceName));
    }

    [Fact]
    public async Task MoveUpDown_ForGroups_SavesOrderInStore()
    {
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "a", "b" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Г1", Services = new[] { "a" }.ToList() },
                new ServiceGroup { Name = "Г2", Services = new[] { "b" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Г1", "a", "group:Г2", "b" }.ToList()
        };

        await env.Vm.LoadAsync();

        // Перемещаем группу Г2 на одну позицию выше (строки: Г1, a, Г2, b → Г1, Г2, a, b).
        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceGroupRow>().Single(g => g.Name == "Г2");
        Assert.True(env.Vm.CanMoveUp);
        env.Vm.MoveUpCommand.Execute(null);

        Assert.Equal(new[] { "group:Г1", "group:Г2", "a", "b" }, env.Store.Stored.Order);
        Assert.Equal("Г1", ((ServiceGroupRow)env.Vm.Rows[0]).Name);
        Assert.Equal("Г2", ((ServiceGroupRow)env.Vm.Rows[1]).Name);

        // Перемещаем группу Г1 ниже — порядок в хранилище меняется.
        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceGroupRow>().Single(g => g.Name == "Г1");
        env.Vm.MoveDownCommand.Execute(null);
        Assert.Equal(new[] { "group:Г2", "group:Г1", "a", "b" }, env.Store.Stored.Order);
    }

    [Fact]
    public async Task GroupManagementAvailability_DependsOnProviderAndSelection()
    {
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "a" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Группа", Services = new[] { "a" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Группа", "a" }.ToList()
        };

        // Без провайдера «Добавить/Изменить группу» неактивны.
        Assert.False(env.Vm.CanAddGroup);
        Assert.False(env.Vm.CanEditGroup);

        env.Vm.GroupEditorProvider = (_, _) => null;
        Assert.True(env.Vm.CanAddGroup);

        await env.Vm.LoadAsync();
        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceGroupRow>().Single();
        Assert.True(env.Vm.CanEditGroup);
        Assert.True(env.Vm.CanDeleteGroup);

        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceInfoRow>().Single();
        Assert.False(env.Vm.CanEditGroup);
        Assert.False(env.Vm.CanDeleteGroup);
    }

    [Fact]
    public void AvailableServicesForGroup_ExcludesOtherGroupsAndOwnMembers()
    {
        var env = Create();
        env.Fake.Services["s1"] = Info("s1", "S1", ServiceStatus.Running);
        env.Fake.Services["s2"] = Info("s2", "S2", ServiceStatus.Running);
        env.Fake.Services["g1x"] = Info("g1x", "G1X", ServiceStatus.Running);
        env.Fake.Services["g2x"] = Info("g2x", "G2X", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "s1", "s2" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Г1", Services = new[] { "s1", "g1x" }.ToList() },
                new ServiceGroup { Name = "Г2", Services = new[] { "g2x" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Г1", "s1", "group:Г2", "s2" }.ToList()
        };

        // Новая группа: занятые ДРУГИМИ группами (все) исключаются — остаётся только s2.
        Assert.Equal(new[] { "s2" }, env.Vm.AvailableServicesForGroup(null));

        // Правка Г1: доступны ТОЛЬКО службы «не в других группах И не в этой же группе»
        // (требование 7OH, 1.0.0.14): свои члены (s1, g1x) в списке «Все службы» не дублируются.
        var g1 = env.Store.Stored.Groups[0];
        Assert.Equal(new[] { "s2" }, env.Vm.AvailableServicesForGroup(g1));

        // Правка Г2: свои члены (g2x) не дублируются, члены Г1 исключены.
        var g2 = env.Store.Stored.Groups[1];
        Assert.Equal(new[] { "s2" }, env.Vm.AvailableServicesForGroup(g2));
    }

    [Fact]
    public async Task LoadAsync_GroupWithNotFoundMember_GroupNoneGray_ServiceRowRed()
    {
        // Группа с исчезнувшей службой (NotFound): статус группы — None (серый кружок),
        // при этом строка самой службы показывает красный (#EF4444) — уровни индикации
        // группы и строки раздельны (Задача 5, issue #11).
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Stopped);
        env.Fake.Services["ghost"] = Info("ghost", "Ghost", ServiceStatus.NotFound);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "ghost" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Группа", Services = new[] { "a", "ghost" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Группа", "ghost" }.ToList()
        };

        await env.Vm.LoadAsync();

        // NotFound-член считается «не запущен» → группа None (серый, не красный).
        var group = env.Vm.Rows.OfType<ServiceGroupRow>().Single();
        Assert.Equal(GroupStatusKind.None, group.GroupStatus);
        Assert.Equal("#6B7280", group.StatusColorHex);

        // Строка исчезнувшей службы — красный (NotFound) — индикация раздельна.
        var service = env.Vm.Rows.OfType<ServiceInfoRow>().Single();
        Assert.Equal(ServiceStatus.NotFound, service.StatusKind);
        Assert.Equal("#EF4444", service.StatusColorHex);
    }

    [Fact]
    public async Task StartGroupCommand_SkipsInapplicableMembers_KeepsOrder()
    {
        // Если один из членов уже Running (команда «Запустить» неприменима), он
        // пропускается: операция выполняется только по применимым членам В ПОРЯДКЕ
        // списка группы (Задача 5, issue #11).
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Stopped);
        env.Fake.Services["c"] = Info("c", "C", ServiceStatus.Stopped);
        env.Store.Stored = new ServiceStoreData
        {
            Groups = new[]
            {
                new ServiceGroup { Name = "Группа", Services = new[] { "c", "a", "b" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Группа" }.ToList()
        };

        await env.Vm.LoadAsync();
        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceGroupRow>().Single();

        await env.Vm.ExecuteGroupCommandAsync(ServiceAction.Start);

        // c и b — остановлены (запускаем в порядке группы), a — Running (пропущен).
        Assert.Equal(new[] { ("Start", "c"), ("Start", "b") }, env.Fake.Calls);
        Assert.False(env.Vm.IsBusy);
    }

    [Fact]
    public async Task DeleteGroup_CancelConfirm_LeavesGroupAndStoreUntouched()
    {
        // Отмена в Confirm при удалении группы: группа НЕ удаляется из строк и хранилища,
        // ничего не сохраняется (Задача 5, issue #11).
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "a" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Группа", Services = new[] { "a" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Группа", "a" }.ToList()
        };

        await env.Vm.LoadAsync();
        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceGroupRow>().Single();
        env.Dialogs.ConfirmResult = false; // пользователь отменил удаление

        await env.Vm.DeleteGroupAsync();

        Assert.Single(env.Store.Stored.Groups);
        Assert.Contains("group:Группа", env.Store.Stored.Order);
        Assert.Equal(2, env.Vm.Rows.Count);
        Assert.Empty(env.Store.Saved); // сохранение не выполнялось
    }

    [Fact]
    public async Task DeleteGroup_Confirmed_RemovesGroupAndRecalculatesOrder()
    {
        // Подтверждённое удаление: группа исчезает из Rows и из финального
        // ServiceStoreData (при сохранении), а Order пересчитывается без ключа группы
        // (Задача 5, issue #11).
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Running);
        env.Fake.Services["c"] = Info("c", "C", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "a", "b", "c" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Группа", Services = new[] { "b" }.ToList() }
            }.ToList(),
            Order = new[] { "a", "group:Группа", "b", "c" }.ToList()
        };

        await env.Vm.LoadAsync();
        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceGroupRow>().Single();

        await env.Vm.DeleteGroupAsync();

        Assert.Empty(env.Store.Stored.Groups);
        Assert.Equal(new[] { "a", "b", "c" }, env.Store.Stored.Order);
        Assert.Equal(3, env.Vm.Rows.Count);
        Assert.All(env.Vm.Rows, r => Assert.IsType<ServiceInfoRow>(r));
    }

    [Fact]
    public async Task EditGroup_RenameInMiddle_ReplacesOrderKeyKeepingPosition()
    {
        // Переименование группы в СЕРЕДИНЕ смешанного списка: старый ключ
        // group:староеИмя заменяется на group:новоеИмя НА ТОЙ ЖЕ позиции Order
        // (Задача 5, issue #11).
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "a", "b" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Г1", Services = new[] { "a" }.ToList() },
                new ServiceGroup { Name = "Г2", Services = new[] { "b" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Г1", "a", "group:Г2", "b" }.ToList()
        };
        env.Vm.GroupEditorProvider = (_, _) =>
            new ServiceGroup { Name = "Г2 обновлена", Services = new[] { "b" }.ToList() };

        await env.Vm.LoadAsync();
        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceGroupRow>().Single(g => g.Name == "Г2");

        await env.Vm.EditGroupAsync();

        // Старый ключ заменён на той же позиции; ключ «b» убран из верхнего порядка —
        // b остаётся членом группы Г2 (инвариант «служба ровно в одном месте», 1.0.0.14).
        Assert.Equal(new[] { "group:Г1", "a", "group:Г2 обновлена" }, env.Store.Stored.Order);
        Assert.Equal("Г2 обновлена", env.Store.Stored.Groups[1].Name);
        Assert.Equal("Г2 обновлена", ((ServiceGroupRow)env.Vm.Rows[2]).Name);
        Assert.Equal(new[] { "b" }, ((ServiceGroupRow)env.Vm.Rows[2]).Children.Select(r => r.ServiceName));
    }

    [Fact]
    public async Task AddGroup_WithNoAvailableServices_CreatesEmptyGroupAndShows()
    {
        // Пустой список служб (нет доступных для группы): кнопка «Добавить» активна
        // (провайдер установлен), поэтому по фактическому поведению реализация создаёт
        // группу с ПУСТЫМ составом, вставляет её в начало и отображает строкой
        // (документирование фактического поведения, Задача 5, issue #11).
        var env = Create();
        env.Store.Stored = new ServiceStoreData
        {
            Services = new List<string>(),
            Groups = new List<ServiceGroup>(),
            Order = new List<string>()
        };
        env.Vm.GroupEditorProvider = (_, _) =>
            new ServiceGroup { Name = "Пустая", Services = new List<string>() };

        await env.Vm.LoadAsync();
        Assert.True(env.Vm.CanAddGroup);
        await env.Vm.AddGroupAsync();

        var row = Assert.IsType<ServiceGroupRow>(Assert.Single(env.Vm.Rows));
        Assert.Equal("Пустая", row.Name);
        Assert.Empty(row.ChildServices);

        var group = Assert.Single(env.Store.Stored.Groups);
        Assert.Equal("Пустая", group.Name);
        Assert.Empty(group.Services);
        Assert.Equal(new[] { "group:Пустая" }, env.Store.Stored.Order);
        Assert.Equal(row, env.Vm.SelectedRow);
    }

    [Fact]
    public async Task MoveDown_GroupBetweenServices_SavesNewOrder()
    {
        // Перемещение группы «Вниз» в смешанном списке (группа между службами):
        // порядок Rows и Order в хранилище меняются согласованно (Задача 5, issue #11).
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Running);
        env.Fake.Services["c"] = Info("c", "C", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "a", "b", "c" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Группа", Services = new[] { "b" }.ToList() }
            }.ToList(),
            Order = new[] { "a", "group:Группа", "b", "c" }.ToList()
        };

        await env.Vm.LoadAsync();
        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceGroupRow>().Single();
        Assert.True(env.Vm.CanMoveDown);

        env.Vm.MoveDownCommand.Execute(null);

        Assert.Equal(new[] { "a", "b", "group:Группа", "c" }, env.Store.Stored.Order);
        // Строки перестроены: a, b, группа, c (порядок Rows согласован с Order).
        Assert.Equal("a", ((ServiceInfoRow)env.Vm.Rows[0]).ServiceName);
        Assert.Equal("b", ((ServiceInfoRow)env.Vm.Rows[1]).ServiceName);
        Assert.Equal("Группа", ((ServiceGroupRow)env.Vm.Rows[2]).Name);
        Assert.Equal("c", ((ServiceInfoRow)env.Vm.Rows[3]).ServiceName);
    }

    [Fact]
    public async Task AvailableServicesForGroup_ExcludedMember_BecomesAvailableAfterRemoval()
    {
        // После исключения члена из группы через Edit (сохранение) он снова появляется
        // в доступных службах для НОВОЙ группы (Задача 5, issue #11).
        var env = Create();
        env.Fake.Services["s1"] = Info("s1", "S1", ServiceStatus.Running);
        env.Fake.Services["s2"] = Info("s2", "S2", ServiceStatus.Running);
        env.Fake.Services["s3"] = Info("s3", "S3", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "s1", "s2", "s3" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Г1", Services = new[] { "s1", "s2" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Г1", "s1", "s2", "s3" }.ToList()
        };

        // До правки: s1 и s2 заняты группой Г1 → для новой группы доступна только s3.
        Assert.Equal(new[] { "s3" }, env.Vm.AvailableServicesForGroup(null));

        env.Vm.GroupEditorProvider = (_, _) =>
            new ServiceGroup { Name = "Г1", Services = new[] { "s1" }.ToList() };
        await env.Vm.LoadAsync();
        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceGroupRow>().Single();
        await env.Vm.EditGroupAsync();

        // После исключения s2 из Г1 она снова доступна для новой группы.
        Assert.Equal(new[] { "s2", "s3" }, env.Vm.AvailableServicesForGroup(null));
    }

    [Fact]
    public async Task LoadAsync_GroupChildrenAreBuiltInsideFolder()
    {
        // Иерархический список (issue #11, 1.0.0.14): службы группы — внутри папки;
        // корневые службы — отдельные строки.
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Stopped);
        env.Fake.Services["c"] = Info("c", "C", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "c" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Г", Services = new[] { "a", "b" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Г", "c" }.ToList()
        };

        await env.Vm.LoadAsync();

        var group = env.Vm.Rows.OfType<ServiceGroupRow>().Single();
        Assert.Equal(new[] { "a", "b" }, group.Children.Select(r => r.ServiceName));
        Assert.True(group.HasChildren);
        Assert.Equal("c", env.Vm.Rows.OfType<ServiceInfoRow>().Single().ServiceName);
    }

    [Fact]
    public async Task ExpandAndCollapseAll_ToggleGroupNodes()
    {
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "a" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Г", Services = new[] { "a" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Г", "a" }.ToList()
        };
        await env.Vm.LoadAsync();

        // Группы свёрнуты по умолчанию (issue #11: без самопроизвольного разворачивания).
        var group = env.Vm.Rows.OfType<ServiceGroupRow>().Single();
        Assert.False(group.IsExpanded);

        env.Vm.CollapseAllCommand.Execute(null);
        Assert.False(group.IsExpanded);

        env.Vm.ExpandAllCommand.Execute(null);
        Assert.True(group.IsExpanded);
    }

    [Fact]
    public async Task Groups_AreCollapsedByDefault_AndSelectionDoesNotExpandThem()
    {
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "a" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Г1", Services = new[] { "a" }.ToList() },
                new ServiceGroup { Name = "Г2", Services = new[] { "b" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Г1", "group:Г2", "a" }.ToList()
        };
        await env.Vm.LoadAsync();

        // Группы свёрнуты по умолчанию (issue #11: без самопроизвольного разворачивания).
        Assert.All(env.Vm.Rows.OfType<ServiceGroupRow>(), g => Assert.False(g.IsExpanded));

        // Выделение строки службы не разворачивает папки-группы.
        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceInfoRow>().First();
        Assert.All(env.Vm.Rows.OfType<ServiceGroupRow>(), g => Assert.False(g.IsExpanded));

        // Перестроение строк (Refresh) тоже не разворачивает группы.
        await env.Vm.RefreshAsync();
        Assert.All(env.Vm.Rows.OfType<ServiceGroupRow>(), g => Assert.False(g.IsExpanded));
    }

    [Fact]
    public async Task LoadAsync_RestoresCollapsedStateFromSettings()
    {
        // Сохранённое состояние (issue #11, пункт 4): «Г1» свёрнута, «Г2» — развёрнута.
        var env = Create(collapsedServiceGroups: new[] { "Г1" });
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "a" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Г1", Services = new[] { "a" }.ToList() },
                new ServiceGroup { Name = "Г2", Services = new[] { "b" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Г1", "group:Г2", "a" }.ToList()
        };

        await env.Vm.LoadAsync();

        Assert.False(env.Vm.Rows.OfType<ServiceGroupRow>().Single(g => g.Name == "Г1").IsExpanded);
        Assert.True(env.Vm.Rows.OfType<ServiceGroupRow>().Single(g => g.Name == "Г2").IsExpanded);
    }

    [Fact]
    public async Task LoadAsync_EmptySavedState_AllGroupsExpanded()
    {
        // Пустой (но сохранённый) набор — «пользователь развернул все группы» (issue #11,
        // пункт 4): при открытии все группы развёрнуты, в отличие от первого запуска (C1).
        var env = Create(collapsedServiceGroups: Array.Empty<string>());
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Groups = new[]
            {
                new ServiceGroup { Name = "Г1", Services = new[] { "a" }.ToList() },
                new ServiceGroup { Name = "Г2", Services = new[] { "b" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Г1", "group:Г2" }.ToList()
        };

        await env.Vm.LoadAsync();

        Assert.All(env.Vm.Rows.OfType<ServiceGroupRow>(), g => Assert.True(g.IsExpanded));
        Assert.Empty(env.SavedCollapsed);
    }

    [Fact]
    public async Task GroupExpandedChanged_SavesCollapsedState()
    {
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Groups = new[]
            {
                new ServiceGroup { Name = "Г", Services = new[] { "a" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Г" }.ToList()
        };
        await env.Vm.LoadAsync();
        var group = env.Vm.Rows.OfType<ServiceGroupRow>().Single();
        Assert.False(group.IsExpanded);
        Assert.Contains("Г", env.SavedCollapsed);

        // Разворачивание (клик по стрелке/«+») убирает группу из набора свёрнутых.
        group.IsExpanded = true;
        Assert.DoesNotContain("Г", env.SavedCollapsed);

        // Повторное сворачивание возвращает группу в набор.
        group.IsExpanded = false;
        Assert.Contains("Г", env.SavedCollapsed);
    }

    [Fact]
    public async Task ExpandAllCommand_UpdatesSavedState()
    {
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Groups = new[]
            {
                new ServiceGroup { Name = "Г1", Services = new[] { "a" }.ToList() },
                new ServiceGroup { Name = "Г2", Services = new[] { "b" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Г1", "group:Г2" }.ToList()
        };
        await env.Vm.LoadAsync();
        Assert.Equal(new[] { "Г1", "Г2" }, env.SavedCollapsed.OrderBy(n => n));

        // «Развернуть все» — набор свёрнутых пуст.
        env.Vm.ExpandAllCommand.Execute(null);
        Assert.All(env.Vm.Rows.OfType<ServiceGroupRow>(), g => Assert.True(g.IsExpanded));
        Assert.Empty(env.SavedCollapsed);

        // «Свернуть все» — все группы снова в наборе.
        env.Vm.CollapseAllCommand.Execute(null);
        Assert.All(env.Vm.Rows.OfType<ServiceGroupRow>(), g => Assert.False(g.IsExpanded));
        Assert.Equal(new[] { "Г1", "Г2" }, env.SavedCollapsed.OrderBy(n => n));
    }

    [Fact]
    public async Task AddGroup_NewGroupCollapsed_AndSaved()
    {
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "a" }.ToList(),
            Order = new[] { "a" }.ToList()
        };
        env.Vm.GroupEditorProvider = (_, _) => new ServiceGroup { Name = "Новая", Services = new List<string>() };
        await env.Vm.LoadAsync();

        await env.Vm.AddGroupAsync();

        // Новая группа свёрнута по умолчанию (C1) и зафиксирована в настройках (issue #11, пункт 4).
        var newGroup = env.Vm.Rows.OfType<ServiceGroupRow>().Single(g => g.Name == "Новая");
        Assert.False(newGroup.IsExpanded);
        Assert.Contains("Новая", env.SavedCollapsed);
    }

    [Fact]
    public async Task EditGroup_Rename_PreservesCollapsedState()
    {
        var env = Create(collapsedServiceGroups: new[] { "Г1" });
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Groups = new[]
            {
                new ServiceGroup { Name = "Г1", Services = new[] { "a" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Г1" }.ToList()
        };
        env.Vm.GroupEditorProvider = (_, _) => new ServiceGroup { Name = "Г2", Services = new[] { "a" }.ToList() };
        await env.Vm.LoadAsync();
        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceGroupRow>().Single();

        await env.Vm.EditGroupAsync();

        // Состояние сворачивания следует за переименованием (issue #11, пункт 4).
        var renamed = env.Vm.Rows.OfType<ServiceGroupRow>().Single(g => g.Name == "Г2");
        Assert.False(renamed.IsExpanded);
        Assert.Contains("Г2", env.SavedCollapsed);
        Assert.DoesNotContain("Г1", env.SavedCollapsed);
    }

    [Fact]
    public async Task DeleteGroup_RemovesFromSavedState()
    {
        var env = Create(collapsedServiceGroups: new[] { "Г1" });
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Groups = new[]
            {
                new ServiceGroup { Name = "Г1", Services = new[] { "a" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Г1" }.ToList()
        };
        await env.Vm.LoadAsync();
        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceGroupRow>().Single();

        await env.Vm.DeleteGroupAsync();

        Assert.Empty(env.Vm.Rows.OfType<ServiceGroupRow>());
        Assert.DoesNotContain("Г1", env.SavedCollapsed);
    }

    [Fact]
    public async Task MoveToGroup_MovesServiceBetweenRootAndGroups()
    {
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "a", "b" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Г1", Services = new List<string>() }
            }.ToList(),
            Order = new[] { "group:Г1", "a", "b" }.ToList()
        };
        await env.Vm.LoadAsync();

        // Перенос корневой службы в группу.
        await env.Vm.MoveToGroupAsync("a", "Г1");
        Assert.Equal(new[] { "a" }, env.Store.Stored.Groups.Single(g => g.Name == "Г1").Services);
        Assert.Equal(new[] { "b" }, env.Store.Stored.Services);
        Assert.DoesNotContain("a", env.Store.Stored.Order);
        // Выделена папка-группа — новое место службы.
        Assert.Equal("Г1", ((ServiceGroupRow?)env.Vm.SelectedRow)?.Name);

        // Перенос службы из группы обратно «Без группы».
        await env.Vm.MoveToGroupAsync("a", null);
        Assert.Empty(env.Store.Stored.Groups.Single(g => g.Name == "Г1").Services);
        Assert.Equal(new[] { "b", "a" }, env.Store.Stored.Services);
        Assert.Contains("a", env.Store.Stored.Order);
        Assert.Equal("a", ((ServiceInfoRow?)env.Vm.SelectedRow)?.ServiceName);
    }

    [Fact]
    public async Task EditGroup_RemovedMember_ReturnsToRootList()
    {
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "b" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Г1", Services = new[] { "a" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Г1", "b" }.ToList()
        };

        env.Vm.GroupEditorProvider = (_, _) => new ServiceGroup { Name = "Г1", Services = new List<string>() };
        await env.Vm.LoadAsync();
        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceGroupRow>().Single();
        await env.Vm.EditGroupAsync();

        // Исключённый член возвращается в корневой список и его порядок (не «теряется»).
        Assert.Contains("a", env.Store.Stored.Services);
        Assert.Contains("a", env.Store.Stored.Order);
    }

    [Fact]
    public void Normalize_ServiceInGroup_RemovedFromRootAndSingleGroupOnly()
    {
        // Инвариант «служба ровно в одном месте» (issue #11, 1.0.0.14): служба в группе
        // не остаётся в корневом списке; в двух группах — только в первой.
        var normalized = ServiceStoreData.Normalize(new ServiceStoreData
        {
            Services = new[] { "a", "b", "dupe" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Г1", Services = new[] { "a", "dupe" }.ToList() },
                new ServiceGroup { Name = "Г2", Services = new[] { "dupe" }.ToList() }
            }.ToList()
        });

        Assert.Equal(new[] { "b" }, normalized.Services);
        Assert.Equal(new[] { "a", "dupe" }, normalized.Groups[0].Services);
        Assert.Empty(normalized.Groups[1].Services);
    }

    [Fact]
    public async Task MoveUpDown_ServiceInsideGroup_ReordersChildrenAndSaves()
    {
        // Кнопки «Вверх/Вниз» для службы ВНУТРИ группы (issue #11, пункт 5): порядок
        // Children/ChildServices группы и ServiceGroup.Services в хранилище меняются.
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Running);
        env.Fake.Services["c"] = Info("c", "C", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Groups = new[]
            {
                new ServiceGroup { Name = "Г", Services = new[] { "a", "b", "c" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Г" }.ToList()
        };
        await env.Vm.LoadAsync();

        var group = env.Vm.Rows.OfType<ServiceGroupRow>().Single();
        var children = group.Children;

        // Последняя служба: вверх доступно, вниз — нет.
        env.Vm.SelectedRow = children[2];
        Assert.True(env.Vm.CanMoveUp);
        Assert.False(env.Vm.CanMoveDown);

        env.Vm.MoveUpCommand.Execute(null);

        Assert.Equal(new[] { "a", "c", "b" }, children.Select(r => r.ServiceName));
        Assert.Equal(new[] { "a", "c", "b" }, group.ChildServices);
        Assert.Equal(new[] { "a", "c", "b" }, env.Store.Stored.Groups.Single().Services);

        // Первая служба: вверх недоступно; вниз — меняет порядок.
        env.Vm.SelectedRow = children[0];
        Assert.False(env.Vm.CanMoveUp);
        Assert.True(env.Vm.CanMoveDown);

        env.Vm.MoveDownCommand.Execute(null);

        Assert.Equal(new[] { "c", "a", "b" }, children.Select(r => r.ServiceName));
        Assert.Equal(new[] { "c", "a", "b" }, env.Store.Stored.Groups.Single().Services);
    }

    [Fact]
    public async Task MoveUpDown_ServiceInsideGroup_DoesNotAffectRootOrder()
    {
        // Перемещение службы внутри группы не трогает корневой список (Rows/Order):
        // группа и корневые службы остаются на своих местах (issue #11, пункт 5).
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Running);
        env.Fake.Services["x"] = Info("x", "X", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "x" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Г", Services = new[] { "a", "b" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Г", "x" }.ToList()
        };
        await env.Vm.LoadAsync();

        var group = env.Vm.Rows.OfType<ServiceGroupRow>().Single();
        env.Vm.SelectedRow = group.Children[1]; // b
        env.Vm.MoveUpCommand.Execute(null);

        Assert.Equal(new[] { "b", "a" }, group.Children.Select(r => r.ServiceName));
        Assert.Equal(new[] { "group:Г", "x" }, env.Store.Stored.Order);
        Assert.Equal(new[] { "x" }, env.Store.Stored.Services);
        Assert.Equal(2, env.Vm.Rows.Count);
        Assert.Equal("Г", ((ServiceGroupRow)env.Vm.Rows[0]).Name);
        Assert.Equal("x", ((ServiceInfoRow)env.Vm.Rows[1]).ServiceName);
    }

    [Fact]
    public async Task CanMoveUpDown_ForServiceInsideGroup_RespectsPosition()
    {
        // Доступность кнопок для службы внутри группы: первый — вверх нет, последний — вниз нет,
        // средний — обе доступны (issue #11, пункт 5).
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Running);
        env.Fake.Services["c"] = Info("c", "C", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Groups = new[]
            {
                new ServiceGroup { Name = "Г", Services = new[] { "a", "b", "c" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Г" }.ToList()
        };
        await env.Vm.LoadAsync();

        var children = env.Vm.Rows.OfType<ServiceGroupRow>().Single().Children;

        env.Vm.SelectedRow = children[0];
        Assert.False(env.Vm.CanMoveUp);
        Assert.True(env.Vm.CanMoveDown);

        env.Vm.SelectedRow = children[1];
        Assert.True(env.Vm.CanMoveUp);
        Assert.True(env.Vm.CanMoveDown);

        env.Vm.SelectedRow = children[2];
        Assert.True(env.Vm.CanMoveUp);
        Assert.False(env.Vm.CanMoveDown);
    }

    [Fact]
    public async Task MoveUpDown_RootService_WithGroupsPresent_StillReordersRoot()
    {
        // Корневая служба при наличии групп по-прежнему перемещается в общем списке
        // (поведение не сломано добавлением перемещения внутри групп, issue #11, пункт 5).
        var env = Create();
        env.Fake.Services["a"] = Info("a", "A", ServiceStatus.Running);
        env.Fake.Services["b"] = Info("b", "B", ServiceStatus.Running);
        env.Store.Stored = new ServiceStoreData
        {
            Services = new[] { "a", "b" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Г", Services = new List<string>() }
            }.ToList(),
            Order = new[] { "group:Г", "a", "b" }.ToList()
        };
        await env.Vm.LoadAsync();

        env.Vm.SelectedRow = env.Vm.Rows.OfType<ServiceInfoRow>().Single(r => r.ServiceName == "b");
        Assert.True(env.Vm.CanMoveUp);
        env.Vm.MoveUpCommand.Execute(null);

        Assert.Equal(new[] { "group:Г", "b", "a" }, env.Store.Stored.Order);
        Assert.Equal("b", ((ServiceInfoRow)env.Vm.Rows[1]).ServiceName);
    }

    // ===================== Вспомогательное =====================

    private static ServiceInfo Info(string name, string display, ServiceStatus status, bool canPause = false, bool canStop = true) => new()
    {
        ServiceName = name,
        DisplayName = display,
        Status = status,
        CanPauseAndContinue = canPause,
        CanStop = canStop
    };

    private static TestEnv Create(
        IReadOnlyCollection<string>? collapsedServiceGroups = null,
        Action<IReadOnlyCollection<string>>? collapsedGroupsChanged = null)
    {
        var fake = new ServicesWindowViewModelTests.FakeServiceController();
        var store = new ServicesWindowViewModelTests.FakeServicesStore();
        var dialogs = new ServicesWindowViewModelTests.FakeDialogService();
        var savedCollapsed = new List<string>();
        var vm = new ServicesWindowViewModel(fake, store, dialogs, dispatchToUi: null,
            collapsedServiceGroups: collapsedServiceGroups,
            collapsedGroupsChanged: collapsedGroupsChanged ?? (c =>
            {
                savedCollapsed.Clear();
                savedCollapsed.AddRange(c);
            }));
        return new TestEnv(vm, fake, dialogs, store, savedCollapsed);
    }

    /// <summary>Сборка окружения теста: VM + фейковые сервисы (переиспользуются фейки из ServicesWindowViewModelTests).</summary>
    private sealed record TestEnv(
        ServicesWindowViewModel Vm,
        ServicesWindowViewModelTests.FakeServiceController Fake,
        ServicesWindowViewModelTests.FakeDialogService Dialogs,
        ServicesWindowViewModelTests.FakeServicesStore Store,
        List<string> SavedCollapsed);
}