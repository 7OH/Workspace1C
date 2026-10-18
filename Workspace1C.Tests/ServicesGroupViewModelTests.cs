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
        // Позиция группы сохранена; ключ Order обновлён при переименовании.
        Assert.Equal(new[] { "group:Новая", "a" }, env.Store.Stored.Order);
        Assert.Equal("Новая", ((ServiceGroupRow)env.Vm.Rows[0]).Name);
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
    public void AvailableServicesForGroup_ExcludesOtherGroupsMembers_KeepsOwnMembers()
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

        // Правка Г1: свои члены (s1, g1x) доступны, члены Г2 (g2x) исключены.
        var g1 = env.Store.Stored.Groups[0];
        Assert.Equal(new[] { "s1", "s2", "g1x" }, env.Vm.AvailableServicesForGroup(g1));

        // Правка Г2: свои члены (g2x) доступны, члены Г1 (s1, g1x) исключены.
        var g2 = env.Store.Stored.Groups[1];
        Assert.Equal(new[] { "s2", "g2x" }, env.Vm.AvailableServicesForGroup(g2));
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

        Assert.Equal(new[] { "group:Г1", "a", "group:Г2 обновлена", "b" }, env.Store.Stored.Order);
        Assert.Equal("Г2 обновлена", env.Store.Stored.Groups[1].Name);
        Assert.Equal("Г2 обновлена", ((ServiceGroupRow)env.Vm.Rows[2]).Name);
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

    // ===================== Вспомогательное =====================

    private static ServiceInfo Info(string name, string display, ServiceStatus status, bool canPause = false, bool canStop = true) => new()
    {
        ServiceName = name,
        DisplayName = display,
        Status = status,
        CanPauseAndContinue = canPause,
        CanStop = canStop
    };

    private static TestEnv Create()
    {
        var fake = new ServicesWindowViewModelTests.FakeServiceController();
        var store = new ServicesWindowViewModelTests.FakeServicesStore();
        var dialogs = new ServicesWindowViewModelTests.FakeDialogService();
        var vm = new ServicesWindowViewModel(fake, store, dialogs, dispatchToUi: null);
        return new TestEnv(vm, fake, dialogs, store);
    }

    /// <summary>Сборка окружения теста: VM + фейковые сервисы (переиспользуются фейки из ServicesWindowViewModelTests).</summary>
    private sealed record TestEnv(
        ServicesWindowViewModel Vm,
        ServicesWindowViewModelTests.FakeServiceController Fake,
        ServicesWindowViewModelTests.FakeDialogService Dialogs,
        ServicesWindowViewModelTests.FakeServicesStore Store);
}