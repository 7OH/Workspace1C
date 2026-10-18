using System;
using System.Collections.Generic;
using System.Linq;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты ViewModel окна редактирования группы служб (issue #11, итерация 2 — группы
/// служб, релиз 1.0.0.12; Задача 3): перенос «влево/вправо» между «Все службы» и
/// «Управляемым списком», порядок членов «вверх/вниз», сохранение формирует
/// <see cref="ServiceGroup"/> с нужным именем и порядком, валидация пустого имени,
/// редактирование существующей группы (свои члены уже в «Управляемом списке»).
/// </summary>
public sealed class ServiceGroupEditViewModelTests
{
    [Fact]
    public void Constructor_NewGroup_FillsAvailableLists()
    {
        var vm = new ServiceGroupEditViewModel(group: null, new[] { "a", "b", "c" });

        Assert.Empty(vm.MemberServices);
        Assert.Equal(new[] { "a", "b", "c" }, vm.AvailableServices);
        Assert.Empty(vm.GroupName);
        Assert.Null(vm.SelectedAvailable);
        Assert.Null(vm.SelectedMember);
        Assert.False(vm.IsSaved);
        Assert.Null(vm.Result);
    }

    [Fact]
    public void MoveLeft_MovesSelectedServiceFromAvailableToMembers()
    {
        var vm = new ServiceGroupEditViewModel(group: null, new[] { "a", "b", "c" });

        // Без выбора «Влево» неактивно.
        Assert.False(vm.MoveLeftCommand.CanExecute(null));

        vm.SelectedAvailable = "b";
        Assert.True(vm.MoveLeftCommand.CanExecute(null));
        vm.MoveLeftCommand.Execute(null);

        Assert.Equal(new[] { "a", "c" }, vm.AvailableServices);
        Assert.Equal(new[] { "b" }, vm.MemberServices);
        // После переноса член становится выбранным в «Управляемом списке».
        Assert.Equal("b", vm.SelectedMember);
        Assert.Null(vm.SelectedAvailable);
    }

    [Fact]
    public void MoveRight_ReturnsSelectedMemberToAvailable()
    {
        var vm = new ServiceGroupEditViewModel(group: null, new[] { "a", "b", "c" });
        vm.SelectedAvailable = "a";
        vm.MoveLeftCommand.Execute(null);
        vm.SelectedAvailable = "c";
        vm.MoveLeftCommand.Execute(null);
        Assert.Equal(new[] { "a", "c" }, vm.MemberServices);

        // Без выбора «Вправо» неактивно.
        vm.SelectedMember = null;
        Assert.False(vm.MoveRightCommand.CanExecute(null));

        vm.SelectedMember = "a";
        Assert.True(vm.MoveRightCommand.CanExecute(null));
        vm.MoveRightCommand.Execute(null);

        Assert.Equal(new[] { "c" }, vm.MemberServices);
        // Служба возвращается в конец «Все службы» и становится выбранной там.
        Assert.Equal(new[] { "b", "a" }, vm.AvailableServices);
        Assert.Equal("a", vm.SelectedAvailable);
        Assert.Null(vm.SelectedMember);
    }

    [Fact]
    public void MoveUpDown_ReordersMembersOnlyByButtons()
    {
        var vm = new ServiceGroupEditViewModel(group: null, new[] { "a", "b", "c" });
        foreach (var name in new[] { "a", "b", "c" })
        {
            vm.SelectedAvailable = name;
            vm.MoveLeftCommand.Execute(null);
        }
        Assert.Equal(new[] { "a", "b", "c" }, vm.MemberServices);

        // «Вверх» для верхнего элемента неактивен; «Вниз» для нижнего — неактивен.
        vm.SelectedMember = "a";
        Assert.False(vm.MoveUpCommand.CanExecute(null));
        Assert.True(vm.MoveDownCommand.CanExecute(null));
        vm.SelectedMember = "c";
        Assert.True(vm.MoveUpCommand.CanExecute(null));
        Assert.False(vm.MoveDownCommand.CanExecute(null));

        vm.SelectedMember = "c";
        vm.MoveUpCommand.Execute(null);
        Assert.Equal(new[] { "a", "c", "b" }, vm.MemberServices);
        Assert.Equal("c", vm.SelectedMember);

        vm.MoveUpCommand.Execute(null);
        Assert.Equal(new[] { "c", "a", "b" }, vm.MemberServices);

        vm.SelectedMember = "a";
        vm.MoveDownCommand.Execute(null);
        Assert.Equal(new[] { "c", "b", "a" }, vm.MemberServices);

        // Границы: элемент на краю не перемещается.
        vm.SelectedMember = "c";
        vm.MoveUpCommand.Execute(null);
        Assert.Equal(new[] { "c", "b", "a" }, vm.MemberServices);
        vm.SelectedMember = "a";
        vm.MoveDownCommand.Execute(null);
        Assert.Equal(new[] { "c", "b", "a" }, vm.MemberServices);
    }

    [Fact]
    public void Save_BuildsGroupWithTrimmedNameAndMemberOrder_AndRequestsClose()
    {
        var vm = new ServiceGroupEditViewModel(group: null, new[] { "x", "y", "z" });
        vm.GroupName = "  База 1С  ";
        foreach (var name in new[] { "z", "x" })
        {
            vm.SelectedAvailable = name;
            vm.MoveLeftCommand.Execute(null);
        }
        // Меняем порядок: x должен оказаться выше z.
        vm.SelectedMember = "x";
        vm.MoveUpCommand.Execute(null);

        var closeRequested = false;
        vm.CloseRequested += () => closeRequested = true;

        vm.SaveCommand.Execute(null);

        Assert.True(vm.IsSaved);
        Assert.True(closeRequested);
        Assert.NotNull(vm.Result);
        Assert.Equal("База 1С", vm.Result.Name);
        Assert.Equal(new[] { "x", "z" }, vm.Result.Services);
    }

    [Fact]
    public void Save_WithEmptyOrWhitespaceName_IsBlocked()
    {
        var vm = new ServiceGroupEditViewModel(group: null, new[] { "a" });

        Assert.False(vm.CanSave);
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.SelectedAvailable = "a";
        vm.MoveLeftCommand.Execute(null);

        // Пустая/из пробелов — сохранить нельзя (кнопка неактивна, решение Задачи 3).
        vm.GroupName = "   ";
        Assert.False(vm.CanSave);
        Assert.False(vm.SaveCommand.CanExecute(null));

        var closeRequested = false;
        vm.CloseRequested += () => closeRequested = true;
        vm.SaveCommand.Execute(null);
        Assert.False(vm.IsSaved);
        Assert.False(closeRequested);
        Assert.Null(vm.Result);

        // Непустое имя — сохранить можно.
        vm.GroupName = "Группа 1";
        Assert.True(vm.CanSave);
        Assert.True(vm.SaveCommand.CanExecute(null));
        vm.SaveCommand.Execute(null);
        Assert.True(vm.IsSaved);
        Assert.NotNull(vm.Result);
    }

    [Fact]
    public void Cancel_ClosesWithoutSaving()
    {
        var vm = new ServiceGroupEditViewModel(group: null, new[] { "a" });
        vm.GroupName = "Группа";
        vm.SelectedAvailable = "a";
        vm.MoveLeftCommand.Execute(null);

        var closeRequested = false;
        vm.CloseRequested += () => closeRequested = true;

        vm.CancelCommand.Execute(null);

        Assert.True(closeRequested);
        Assert.False(vm.IsSaved);
        Assert.Null(vm.Result);
    }

    [Fact]
    public void EditExistingGroup_OwnMembersInManagedList_AndNotDuplicatedInAvailable()
    {
        var group = new ServiceGroup { Name = "Все", Services = new[] { "x", "y" }.ToList() };
        // Входной список приходит от AvailableServicesForGroup: свои члены тоже доступны.
        var vm = new ServiceGroupEditViewModel(group, new[] { "a", "x", "y", "b" });

        Assert.Equal("Все", vm.GroupName);
        Assert.Equal(new[] { "x", "y" }, vm.MemberServices);
        Assert.Equal(new[] { "a", "b" }, vm.AvailableServices);
    }

    [Fact]
    public void EditExistingGroup_SaveKeepsPreviousMembersAndNewOnes()
    {
        var group = new ServiceGroup { Name = "Все", Services = new[] { "x" }.ToList() };
        var vm = new ServiceGroupEditViewModel(group, new[] { "a", "x" });
        vm.SelectedAvailable = "a";
        vm.MoveLeftCommand.Execute(null);
        // Порядок: x (был), a (новый, в конец).
        var closeRequested = false;
        vm.CloseRequested += () => closeRequested = true;

        vm.SaveCommand.Execute(null);

        Assert.True(vm.IsSaved);
        Assert.True(closeRequested);
        Assert.NotNull(vm.Result);
        Assert.Equal("Все", vm.Result.Name);
        Assert.Equal(new[] { "x", "a" }, vm.Result.Services);
    }

    [Fact]
    public void MoveLeft_UntilAvailableEmpty_CommandDisabledAndMembersContainAll()
    {
        // Перенос ВСЕХ служб «Влево»: «Все службы» опустошается, «Влево» становится
        // неактивным, а «Управляемый список» содержит все службы (Задача 5, issue #11).
        var vm = new ServiceGroupEditViewModel(group: null, new[] { "a", "b" });
        vm.GroupName = "Группа";

        foreach (var name in new[] { "a", "b" })
        {
            vm.SelectedAvailable = name;
            vm.MoveLeftCommand.Execute(null);
        }

        Assert.Empty(vm.AvailableServices);
        Assert.Equal(new[] { "a", "b" }, vm.MemberServices);
        Assert.False(vm.MoveLeftCommand.CanExecute(null));
    }

    [Fact]
    public void MoveRight_UntilMembersEmpty_CommandDisabledAndAvailableRestored()
    {
        // Перенос ВСЕХ членов «Вправо»: «Управляемый список» опустошается, «Вправо»
        // становится неактивным, службы возвращаются в «Все службы» (Задача 5, issue #11).
        var vm = new ServiceGroupEditViewModel(group: null, new[] { "x", "y" });
        foreach (var name in new[] { "x", "y" })
        {
            vm.SelectedAvailable = name;
            vm.MoveLeftCommand.Execute(null);
        }
        Assert.Equal(new[] { "x", "y" }, vm.MemberServices);

        foreach (var name in new[] { "x", "y" })
        {
            vm.SelectedMember = name;
            vm.MoveRightCommand.Execute(null);
        }

        Assert.Empty(vm.MemberServices);
        Assert.Equal(new[] { "x", "y" }, vm.AvailableServices);
        Assert.False(vm.MoveRightCommand.CanExecute(null));
    }

    [Fact]
    public void SingleMember_MoveUpDown_DisabledAndOrderKept()
    {
        // Вырожденный случай: в группе один член — «Вверх» и «Вниз» недоступны,
        // порядок не меняется (Задача 5, issue #11).
        var vm = new ServiceGroupEditViewModel(group: null, new[] { "only" });
        vm.SelectedAvailable = "only";
        vm.MoveLeftCommand.Execute(null);
        vm.SelectedMember = "only";

        Assert.False(vm.MoveUpCommand.CanExecute(null));
        Assert.False(vm.MoveDownCommand.CanExecute(null));

        vm.MoveUpCommand.Execute(null);
        vm.MoveDownCommand.Execute(null);
        Assert.Equal(new[] { "only" }, vm.MemberServices);
    }

    [Fact]
    public void EditExistingGroup_DoesNotMutateInputGroupUntilSave()
    {
        // Независимость объекта при редактировании: изменения в VM (имя, состав) НЕ
        // мутируют входящий ServiceGroup, пока не нажат «Сохранить»; результат —
        // новый объект (Задача 5, issue #11).
        var input = new ServiceGroup { Name = "Оригинал", Services = new[] { "x", "y" }.ToList() };
        var vm = new ServiceGroupEditViewModel(input, new[] { "a", "x", "y" });

        vm.GroupName = "Переименована";
        vm.SelectedMember = "y";
        vm.MoveRightCommand.Execute(null);

        // Пока не сохранено — входящий объект не тронут.
        Assert.Equal("Оригинал", input.Name);
        Assert.Equal(new[] { "x", "y" }, input.Services);

        vm.SaveCommand.Execute(null);

        Assert.True(vm.IsSaved);
        Assert.NotNull(vm.Result);
        Assert.NotSame(input, vm.Result);
        Assert.Equal("Переименована", vm.Result!.Name);
        Assert.Equal(new[] { "x" }, vm.Result.Services);

        // После сохранения входящий объект по-прежнему не изменён.
        Assert.Equal("Оригинал", input.Name);
        Assert.Equal(new[] { "x", "y" }, input.Services);
    }
}