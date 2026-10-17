#if LINUX
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Команда «Управление службами» (issue #11) для Avalonia/Linux: немодальное окно
/// со списком служб (services.json), управление через systemctl. Пауза/восстановление
/// не поддерживаются; системного диалога нет. Частичный класс <see cref="MainViewModel"/>.
/// </summary>
public partial class MainViewModel
{
    private ICommand? _showServicesCommand;

    /// <summary>
    /// Открытое окно «Управление службами» (issue #11): немодальное, при повторном
    /// вызове команды активируется уже открытое (паттерн монитора серверов).
    /// </summary>
    private static Workspace1C.ServicesWindow? _servicesWindow;

    /// <summary>Горячая клавиша «Управление службами» (по умолчанию Ctrl+Shift+S).</summary>
    public string HotkeyServices => _settings.HotkeyServices;

    /// <summary>Команда «Управление службами…»: окно служб Linux (не модальное).</summary>
    public ICommand ShowServicesCommand =>
        _showServicesCommand ??= new RelayCommand(_ => ExecuteShowServices());

    /// <summary>Показывает окно «Управление службами» НЕМОДАЛЬНО (паттерн ExecuteServerMonitor).</summary>
    private void ExecuteShowServices()
    {
        var existing = _servicesWindow;
        if (existing is { IsVisible: true })
        {
            if (existing.WindowState == Avalonia.Controls.WindowState.Minimized)
                existing.WindowState = Avalonia.Controls.WindowState.Normal;
            existing.Activate();
            existing.Topmost = true;
            existing.Topmost = false;
            return;
        }

        // Пока окно инструмента модальное (issue #11, 7OH 17.10).
        var window = new Workspace1C.ServicesWindow();
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_servicesWindow, window))
                _servicesWindow = null;
        };
        _servicesWindow = window;
        window.ShowSync(OwnerWindow());
    }
}
#endif