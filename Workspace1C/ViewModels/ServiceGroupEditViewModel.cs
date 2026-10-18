using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна редактирования группы служб (issue #11, итерация 2 — группы служб,
/// релиз 1.0.0.12; Задача 3). Форма по требованию 7OH (комментарий к issue #11 от 13:25):
/// сверху «Сохранить»/«Отмена», ниже «Описание» (имя группы), ниже два списка —
/// «Управляемый список» (члены группы) и «Все службы» (доступные для включения),
/// между списками кнопки-стрелки «Влево» (в члены) и «Вправо» (обратно), над
/// «Управляемым списком» кнопки «Вверх/Вниз» (порядок членов).
/// Логика «во Все службы видны… не добавленные в другие группы» НЕ дублируется: входной
/// список уже отфильтрован вызывающей стороной
/// (<see cref="ServicesWindowViewModel.AvailableServicesForGroup"/>).
/// Чистый .NET без платформенных зависимостей — используется обеими платформами
/// (WPF и Avalonia).
/// </summary>
public sealed class ServiceGroupEditViewModel : ViewModelBase
{
    private string _groupName;
    private string? _selectedAvailable;
    private string? _selectedMember;

    private RelayCommand? _moveLeftCommand;
    private RelayCommand? _moveRightCommand;
    private RelayCommand? _moveUpCommand;
    private RelayCommand? _moveDownCommand;
    private RelayCommand? _saveCommand;
    private RelayCommand? _cancelCommand;

    /// <param name="group">Редактируемая группа; null — создание новой.</param>
    /// <param name="availableServices">
    /// Службы общего списка, допустимые для включения в группу (уже отфильтрованы
    /// вызывающей стороной: службы, не занятые ДРУГИМИ группами; свои члены тоже
    /// доступны). Они попадают в «Все службы».
    /// </param>
    public ServiceGroupEditViewModel(ServiceGroup? group, IReadOnlyList<string> availableServices)
    {
        _groupName = group?.Name?.Trim() ?? string.Empty;

        // Члены группы — «Управляемый список» (порядок пользователя сохраняется).
        if (group is not null)
        {
            foreach (var name in group.Services ?? new List<string>())
            {
                var normalized = (name ?? string.Empty).Trim();
                if (normalized.Length > 0)
                    MemberServices.Add(normalized);
            }
        }

        // «Все службы»: входной список минус уже состоящие в группе члены (они и так
        // в «Управляемом списке»). Порядок входного списка сохраняется.
        var memberSet = new HashSet<string>(MemberServices, StringComparer.OrdinalIgnoreCase);
        foreach (var name in availableServices ?? Array.Empty<string>())
        {
            var normalized = (name ?? string.Empty).Trim();
            if (normalized.Length == 0 || memberSet.Contains(normalized))
                continue;
            AvailableServices.Add(normalized);
        }
    }

    /// <summary>«Все службы» (доступные для включения) — правый список формы.</summary>
    public ObservableCollection<string> AvailableServices { get; } = new();

    /// <summary>«Управляемый список» (члены группы, порядок задаёт пользователь) — левый список.</summary>
    public ObservableCollection<string> MemberServices { get; } = new();

    /// <summary>
    /// Имя (описание) группы. Пустое/только пробелы имя блокирует «Сохранить»
    /// (<see cref="CanSave"/>) — группа без описания не сохраняется (валидация в VM,
    /// решение по Задаче 3: кнопка неактивна, диалог не нужен).
    /// </summary>
    public string GroupName
    {
        get => _groupName;
        set
        {
            if (SetProperty(ref _groupName, value ?? string.Empty))
                _saveCommand?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Выбранная служба в «Все службы» (то, что переносится кнопкой «Влево»).</summary>
    public string? SelectedAvailable
    {
        get => _selectedAvailable;
        set
        {
            if (SetProperty(ref _selectedAvailable, value))
                RaiseMoveCommandsCanExecuteChanged();
        }
    }

    /// <summary>Выбранный член «Управляемого списка» (кнопки «Вправо/Вверх/Вниз»).</summary>
    public string? SelectedMember
    {
        get => _selectedMember;
        set
        {
            if (SetProperty(ref _selectedMember, value))
                RaiseMoveCommandsCanExecuteChanged();
        }
    }

    /// <summary>«Сохранить» доступно только при непустом (после Trim) имени группы.</summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(GroupName);

    /// <summary>«Влево»: перенос выбранной службы из «Все службы» в «Управляемый список».</summary>
    public ICommand MoveLeftCommand => _moveLeftCommand ??=
        new RelayCommand(MoveLeft, () => SelectedAvailable is not null);

    /// <summary>«Вправо»: перенос выбранного члена обратно в «Все службы» (удаление из группы).</summary>
    public ICommand MoveRightCommand => _moveRightCommand ??=
        new RelayCommand(MoveRight, () => SelectedMember is not null);

    /// <summary>«Вверх»: переместить выбранного члена выше в «Управляемом списке».</summary>
    public ICommand MoveUpCommand => _moveUpCommand ??=
        new RelayCommand(MoveUp, CanMoveUp);

    /// <summary>«Вниз»: переместить выбранного члена ниже в «Управляемом списке».</summary>
    public ICommand MoveDownCommand => _moveDownCommand ??=
        new RelayCommand(MoveDown, CanMoveDown);

    /// <summary>«Сохранить»: зафиксировать группу (<see cref="Result"/>, <see cref="IsSaved"/>) и закрыть окно.</summary>
    public ICommand SaveCommand => _saveCommand ??=
        new RelayCommand(Save, () => CanSave);

    /// <summary>«Отмена»: закрыть окно без сохранения (<see cref="IsSaved"/> == false).</summary>
    public ICommand CancelCommand => _cancelCommand ??= new RelayCommand(Cancel);

    /// <summary>
    /// Запрос на закрытие окна (событийный аналог DialogResult чистых ViewModel):
    /// вызывается по «Сохранить» и «Отмена»; исход сохранения — <see cref="IsSaved"/>.
    /// Окна (WPF/Avalonia) подписываются и закрываются.
    /// </summary>
    public event Action? CloseRequested;

    /// <summary>Группа сохранена («Сохранить»), окно закрывается с положительным результатом.</summary>
    public bool IsSaved { get; private set; }

    /// <summary>
    /// Результат сохранения: готовый <see cref="ServiceGroup"/> (имя после Trim,
    /// члены в порядке «Управляемого списка») или null, пока окно не сохранено.
    /// </summary>
    public ServiceGroup? Result { get; private set; }

    /// <summary>
    /// «Влево»: служба из «Все службы» — в конец «Управляемого списка». Перенесённый
    /// элемент становится выбранным в «Управляемом списке» (чтобы сразу можно было
    /// двигать его «Вверх/Вниз»); выделение в «Все службы» сбрасывается (элемент
    /// оттуда удалён).
    /// </summary>
    private void MoveLeft()
    {
        var name = SelectedAvailable;
        if (name is null)
            return;
        AvailableServices.Remove(name);
        MemberServices.Add(name);
        SelectedAvailable = null;
        SelectedMember = name;
    }

    /// <summary>
    /// «Вправо»: член — обратно в «Все службы» (в конец), из группы удаляется.
    /// Возвращённый элемент становится выбранным в «Все службы»; выделение
    /// «Управляемого списка» сбрасывается.
    /// </summary>
    private void MoveRight()
    {
        var name = SelectedMember;
        if (name is null)
            return;
        MemberServices.Remove(name);
        AvailableServices.Add(name);
        SelectedMember = null;
        SelectedAvailable = name;
    }

    /// <summary>Можно ли переместить выбранного члена выше.</summary>
    private bool CanMoveUp()
    {
        var index = IndexOfMember();
        return index > 0;
    }

    /// <summary>Можно ли переместить выбранного члена ниже.</summary>
    private bool CanMoveDown()
    {
        var index = IndexOfMember();
        return index >= 0 && index < MemberServices.Count - 1;
    }

    private int IndexOfMember()
    {
        var name = SelectedMember;
        if (name is null)
            return -1;
        var index = MemberServices.IndexOf(name);
        if (index < 0)
            return -1;
        return index;
    }

    /// <summary>«Вверх»: переместить члена на позицию выше (порядок — только кнопками, issue #11).</summary>
    private void MoveUp()
    {
        var index = IndexOfMember();
        if (index <= 0)
            return;
        MemberServices.Move(index, index - 1);
        RaiseMoveCommandsCanExecuteChanged();
    }

    /// <summary>«Вниз»: переместить члена на позицию ниже.</summary>
    private void MoveDown()
    {
        var index = IndexOfMember();
        if (index < 0 || index >= MemberServices.Count - 1)
            return;
        MemberServices.Move(index, index + 1);
        RaiseMoveCommandsCanExecuteChanged();
    }

    /// <summary>
    /// «Сохранить»: формирует <see cref="ServiceGroup"/> (имя после Trim, члены в порядке
    /// списка), помечает <see cref="IsSaved"/> и запрашивает закрытие окна. Пустое имя
    /// невозможно — кнопка неактивна при <see cref="CanSave"/> == false.
    /// </summary>
    private void Save()
    {
        if (!CanSave)
            return;
        Result = new ServiceGroup
        {
            Name = GroupName.Trim(),
            Services = MemberServices.ToList()
        };
        IsSaved = true;
        CloseRequested?.Invoke();
    }

    /// <summary>«Отмена»: закрыть без сохранения.</summary>
    private void Cancel()
    {
        IsSaved = false;
        CloseRequested?.Invoke();
    }

    private void RaiseMoveCommandsCanExecuteChanged()
    {
        _moveLeftCommand?.RaiseCanExecuteChanged();
        _moveRightCommand?.RaiseCanExecuteChanged();
        _moveUpCommand?.RaiseCanExecuteChanged();
        _moveDownCommand?.RaiseCanExecuteChanged();
    }
}