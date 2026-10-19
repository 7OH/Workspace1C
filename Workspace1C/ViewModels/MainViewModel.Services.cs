#if WINDOWS
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Команда «Управление службами» (issue #11) для Windows/WPF: немодальное окно со
/// списком служб (services.json), управление через ServiceController и системный
/// диалог services.msc. Частичный класс <see cref="MainViewModel"/>.
/// </summary>
public partial class MainViewModel
{
    private ICommand? _showServicesCommand;
    private string _hotkeyServices = "Ctrl+Shift+S";
    private string _hotkeyServicesMenu = "Ctrl+Shift+Alt+S";

    /// <summary>
    /// Открытое окно «Управление службами» (issue #11): немодальное, при повторном
    /// вызове команды активируется уже открытое. Static — как окно монитора
    /// <see cref="_serverMonitorWindow"/>: MainViewModel transient, окно живёт
    /// независимо от пересоздания ViewModel.
    /// </summary>
    private static ServicesWindow? _servicesWindow;

    /// <summary>Горячая клавиша «Управление службами» (по умолчанию Ctrl+Shift+S).</summary>
    public string HotkeyServices
    {
        get => _hotkeyServices;
        set
        {
            if (SetProperty(ref _hotkeyServices, NormalizeHotkey(value, "Ctrl+Shift+S")))
                ScheduleSaveSettings();
        }
    }

    /// <summary>
    /// Горячая клавиша «Меню служб» (issue #11, пункт 6): показывает подменю служб
    /// у курсора (как меню закладок по Ctrl+B). По умолчанию Ctrl+Shift+Alt+S.
    /// </summary>
    public string HotkeyServicesMenu
    {
        get => _hotkeyServicesMenu;
        set
        {
            if (SetProperty(ref _hotkeyServicesMenu, NormalizeHotkey(value, "Ctrl+Shift+Alt+S")))
                ScheduleSaveSettings();
        }
    }

    /// <summary>Команда «Управление службами…»: окно служб Windows (не модальное).</summary>
    public ICommand ShowServicesCommand =>
        _showServicesCommand ??= new RelayCommand(_ => ExecuteShowServices());

    /// <summary>
    /// Показывает окно «Управление службами» НЕМОДАЛЬНО (issue #11): при повторном
    /// вызове команды активируется уже открытое окно (в т.ч. свёрнутое), новое
    /// не создаётся (паттерн <see cref="ExecuteServerMonitor"/>).
    /// </summary>
    private void ExecuteShowServices()
    {
        var existing = _servicesWindow;
        if (existing is { IsLoaded: true })
        {
            if (existing.WindowState == System.Windows.WindowState.Minimized)
                existing.WindowState = System.Windows.WindowState.Normal;
            existing.Activate();
            existing.Topmost = true;
            existing.Topmost = false;
            existing.Focus();
            return;
        }

        // Пока окно инструмента модальное (issue #11, 7OH 17.10).
        var window = new ServicesWindow();
        var main = System.Windows.Application.Current.MainWindow;
        if (main is { IsLoaded: true, IsVisible: true })
            window.Owner = main;
        else
            window.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_servicesWindow, window))
                _servicesWindow = null;
        };
        _servicesWindow = window;
        window.ShowDialog();
    }
}
#endif