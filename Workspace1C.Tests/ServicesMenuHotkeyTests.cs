using Workspace1C.Models;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #11, пункт 6 комментария 7OH «Добавить возможность вызова меню служб и
/// хоткей настраиваемый для этого, чтобы как подменю избранного показывалось»:
/// настраиваемый хоткей «Меню служб» (HotkeyServicesMenu, по умолчанию
/// Ctrl+Shift+Alt+S) показывает подменю служб у курсора (как меню закладок по
/// Ctrl+B). Сочетание вынесено в настройки, участвует в проверке дублей вместе
/// с остальными хоткеями и разбирается тем же парсером жестов, что и остальные.
/// </summary>
public sealed class ServicesMenuHotkeyTests
{
    // ---- Дефолты и согласование хоткеев ----

    [Fact]
    public void AppSettings_ServicesMenuHotkey_DefaultIsCtrlShiftAltS()
    {
        var settings = new AppSettings();

        Assert.Equal("Ctrl+Shift+Alt+S", settings.HotkeyServicesMenu);
    }

    [Fact]
    public void AppSettings_Defaults_NoConflictBetweenServicesAndServicesMenu()
    {
        // «Управление службами» — Ctrl+Shift+S (окно), «Меню служб» — Ctrl+Shift+Alt+S
        // (подменю у курсора): дефолты не конфликтуют.
        var settings = new AppSettings();

        Assert.Equal("Ctrl+Shift+S", settings.HotkeyServices);
        Assert.NotEqual(
            settings.HotkeyServices.Trim(),
            settings.HotkeyServicesMenu.Trim());
    }

    [Fact]
    public void AppSettings_Defaults_NoConflictBetweenServerConsoleAndServicesMenu()
    {
        // «Серверы 1С» — Ctrl+Alt+S (монитор), «Меню служб» — Ctrl+Shift+Alt+S:
        // дефолты не конфликтуют.
        var settings = new AppSettings();

        Assert.Equal("Ctrl+Alt+S", settings.HotkeyServerConsole);
        Assert.NotEqual(
            settings.HotkeyServerConsole.Trim(),
            settings.HotkeyServicesMenu.Trim());
    }

#if !LINUX
    [Fact]
    public void FindDuplicateHotkeys_DetectsServicesMenuConflict()
    {
        // Сценарий: Ctrl+Shift+Alt+S назначено и «Меню служб», и другому действию —
        // окно настроек обязано предупредить о конфликте.
        var duplicates = SettingsViewModel.FindDuplicateHotkeys(new (string Name, string Key)[]
        {
            ("Меню служб", "Ctrl+Shift+Alt+S"),
            ("Управление службами", "Ctrl+Shift+Alt+S"),
            ("Найти в списке", "Ctrl+T")
        }).ToList();

        var duplicateKey = Assert.Single(duplicates);
        Assert.Equal("Ctrl+Shift+Alt+S", duplicateKey.Key, ignoreCase: true);
        Assert.Equal(2, duplicateKey.Count());
    }

    [Fact]
    public void FindDuplicateHotkeys_EmptyServicesMenu_IsNotConflict()
    {
        var duplicates = SettingsViewModel.FindDuplicateHotkeys(new (string Name, string Key)[]
        {
            ("Меню служб", ""),
            ("Управление службами", "Ctrl+Shift+S"),
            ("Найти в списке", "Ctrl+T")
        }).ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void TryParseKeyGesture_ServicesMenuDefault_Resolves()
    {
        // Жест по умолчанию обязан разбираться тем же парсером, которым регистрируются
        // хоткеи главного окна (MainWindow.TryParseKeyGesture): иначе сочетание
        // сохранится в настройках, но не сработает.
        var ok = Workspace1C.MainWindow.TryParseKeyGesture(
            "Ctrl+Shift+Alt+S", out var key, out var modifiers);

        Assert.True(ok);
        Assert.Equal(System.Windows.Input.Key.S, key);
        Assert.Equal(
            System.Windows.Input.ModifierKeys.Control
            | System.Windows.Input.ModifierKeys.Shift
            | System.Windows.Input.ModifierKeys.Alt,
            modifiers);
    }

    [Fact]
    public void TryParseKeyGesture_EmptyServicesMenu_IsNotResolved()
    {
        // Пустое значение («Нет») не должно регистрироваться как жест.
        Assert.False(Workspace1C.MainWindow.TryParseKeyGesture(
            "", out _, out _));
    }
#endif
}