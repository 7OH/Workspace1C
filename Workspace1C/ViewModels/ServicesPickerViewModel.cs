using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Строка чеклиста окна «Настроить» (issue #11): служба с галочкой
/// <see cref="IsChecked"/> (уже выбранные отмечены при открытии).
/// </summary>
public sealed class ServicePickRow : ViewModelBase
{
    private readonly Action _checkChanged;
    private bool _isChecked;

    public ServicePickRow(string serviceName, string displayName, bool isChecked, Action checkChanged)
    {
        ServiceName = serviceName ?? string.Empty;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? ServiceName : displayName;
        _isChecked = isChecked;
        _checkChanged = checkChanged;
    }

    /// <summary>Имя службы (Windows: ServiceName; Linux: unit *.service).</summary>
    public string ServiceName { get; }

    /// <summary>Отображаемое имя службы.</summary>
    public string DisplayName { get; }

    /// <summary>Отмечена ли служба (добавлена в список «Управление службами»).</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (!SetProperty(ref _isChecked, value))
                return;
            _checkChanged?.Invoke();
        }
    }
}

/// <summary>
/// ViewModel модального окна «Настроить» (issue #11): список ВСЕХ доступных служб
/// системы с галочками (уже сохранённые отмечены), поиск-фильтр (отмеченные не
/// скрываются), «Выбрать все/Сбросить» и результат OK — <see cref="SelectedNames"/>.
/// Чистый .NET без платформенных зависимостей.
/// </summary>
public sealed class ServicesPickerViewModel : ViewModelBase
{
    private readonly IServiceControllerService _services;
    private readonly IServicesStore _store;
    private readonly Action<Action>? _dispatchToUi;

    private string _searchText = string.Empty;
    private bool _okPressed;
    private IReadOnlyList<string> _selectedNames = Array.Empty<string>();

    private ICommand? _selectAllCommand;
    private ICommand? _selectNoneCommand;
    private ICommand? _okCommand;

    /// <param name="services">Платформенный сервис (список всех служб системы).</param>
    /// <param name="store">Хранилище уже выбранных служб (services.json) — для проставления галочек.</param>
    /// <param name="dispatchToUi">Доставка применения результатов в UI-поток (окно); null — тесты.</param>
    public ServicesPickerViewModel(
        IServiceControllerService services,
        IServicesStore store,
        Action<Action>? dispatchToUi = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _dispatchToUi = dispatchToUi;
        BuildRows();
    }

    /// <summary>Все службы системы (видимые и отмеченные).</summary>
    public ObservableCollection<ServicePickRow> AllServices { get; } = new();

    /// <summary>
    /// Поиск по имени/отображаемому имени. Отмеченные службы не скрываются,
    /// даже если не проходят фильтр (иначе список невозможно «пробрать»).
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? string.Empty))
                return;
            OnPropertyChanged(nameof(FilteredServices));
        }
    }

    /// <summary>Строки для списка: отмеченные + совпадающие с поиском.</summary>
    public IReadOnlyList<ServicePickRow> FilteredServices => BuildFiltered();

    /// <summary>Сколько служб отмечено (сводка «выбрано N из M»).</summary>
    public int CheckedCount { get; private set; }

    /// <summary>Сводка «Выбрано: N из M».</summary>
    public string CountText => AllServices.Count == 0
        ? string.Empty
        : string.Format(LocalizationManager.T("Services.Picker.CountFormat"), CheckedCount, AllServices.Count);

    /// <summary>«Выбрать все» (по видимому в фильтре списку).</summary>
    public ICommand SelectAllCommand =>
        _selectAllCommand ??= new RelayCommand(() => SetAllChecked(true));

    /// <summary>«Сбросить» (снять все галочки).</summary>
    public ICommand SelectNoneCommand =>
        _selectNoneCommand ??= new RelayCommand(() => SetAllChecked(false));

    /// <summary>OK — зафиксировать выбор (<see cref="SelectedNames"/>).</summary>
    public ICommand OkCommand =>
        _okCommand ??= new RelayCommand(Ok);

    /// <summary>Результат OK: отмеченные службы в порядке списка.</summary>
    public IReadOnlyList<string> SelectedNames => _selectedNames;

    /// <summary>Список подтверждён (OK) — окно должно закрыться с DialogResult = true.</summary>
    public bool OkPressed => _okPressed;

    /// <summary>Строит строки: все службы системы, галочка — для уже сохранённых.</summary>
    private void BuildRows()
    {
        var saved = new HashSet<string>(
            _store.Load().Select(n => n.Trim()),
            StringComparer.OrdinalIgnoreCase);

        Action? rowCheckChanged = null;
        rowCheckChanged = OnAnyRowCheckedChanged;

        // По алфавиту (issue #11, 7OH 17.10): в чеклисте так проще найти службу.
        var comparer = StringComparer.Create(System.Globalization.CultureInfo.CurrentCulture, ignoreCase: true);
        foreach (var info in _services.GetAllServices()
                     .OrderBy(i => string.IsNullOrWhiteSpace(i.DisplayName) ? i.ServiceName : i.DisplayName, comparer))
        {
            AllServices.Add(new ServicePickRow(
                info.ServiceName,
                info.DisplayName,
                saved.Contains(info.ServiceName.Trim()),
                () => rowCheckChanged?.Invoke()));
        }

        OnAnyRowCheckedChanged();
    }

    /// <summary>Пересчитывает сводку и список (после смены галочки).</summary>
    private void OnAnyRowCheckedChanged()
    {
        CheckedCount = AllServices.Count(r => r.IsChecked);
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(FilteredServices));
    }

    private List<ServicePickRow> BuildFiltered()
    {
        var query = (SearchText ?? string.Empty).Trim();
        if (query.Length == 0)
            return new List<ServicePickRow>(AllServices);

        return AllServices
            .Where(r => r.IsChecked
                        || r.ServiceName.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || r.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void SetAllChecked(bool value)
    {
        foreach (var row in FilteredServices)
            row.IsChecked = value;
    }

    private void Ok()
    {
        _okPressed = true;
        _selectedNames = AllServices
            .Where(r => r.IsChecked)
            .Select(r => r.ServiceName)
            .ToList();
    }
}