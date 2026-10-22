using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #38 «Не сохраняет хоткей» (автор 7OH): горячая клавиша «Окно обновления
/// платформы» (Настройки → Клавиши, HotkeyPlatformUpdate, по умолчанию Ctrl+F9)
/// должна сохраняться в AppSettings, переживать перезапуск, отображаться при
/// открытии настроек и разбираться тем же парсером жестов, которым регистрируется
/// (иначе сочетание сохранится в настройках, но не сработает). На Windows баг был
/// в окне настроек: поле не заполнялось текущим значением (пустое поле при «ОК»
/// молча сбрасывало назначение на дефолт) и не было перерегистрации после «ОК».
/// </summary>
public sealed class PlatformUpdateHotkeyTests
{
    // ---- Дефолты и согласование с соседними хоткеями ----

    [Fact]
    public void AppSettings_PlatformUpdateHotkey_DefaultIsCtrlF9()
    {
        var settings = new AppSettings();

        Assert.Equal("Ctrl+F9", settings.HotkeyPlatformUpdate);
    }

    [Fact]
    public void AppSettings_Defaults_NoConflictWithCheckUpdateAndActualReleases()
    {
        // F9 — «Проверить обновления» (выбранная ИБ), Alt+F9 — «Актуальные релизы»,
        // Ctrl+F9 — «Окно обновления платформы»: дефолты не конфликтуют.
        var settings = new AppSettings();

        Assert.Equal("F9", settings.HotkeyCheckUpdate);
        Assert.Equal("Alt+F9", settings.HotkeyActualReleases);
        Assert.Equal("Ctrl+F9", settings.HotkeyPlatformUpdate);
        Assert.NotEqual(settings.HotkeyCheckUpdate.Trim(), settings.HotkeyPlatformUpdate.Trim());
        Assert.NotEqual(settings.HotkeyActualReleases.Trim(), settings.HotkeyPlatformUpdate.Trim());
    }

    // ---- Round-trip AppSettings: значение переживает сериализацию ----

    [Fact]
    public void AppSettings_PlatformUpdateHotkey_RoundTrip_PreservesCustomValue()
    {
        var settings = new AppSettings { HotkeyPlatformUpdate = "Shift+F9" };

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Equal("Shift+F9", loaded.HotkeyPlatformUpdate);
    }

    [Fact]
    public void AppSettings_PlatformUpdateHotkey_RoundTrip_DoesNotClobberNeighbours()
    {
        // Сценарий «присвоение хоткея платформы не перетирается соседними хоткеями
        // и попадает в сохраняемые настройки»: правка одного поля не должна менять
        // соседние (проверка обновлений, актуальные релизы, серверы, службы).
        var settings = new AppSettings
        {
            HotkeyPlatformUpdate = "Shift+F9",
            HotkeyCheckUpdate = "Ctrl+Shift+F9",
            HotkeyActualReleases = "Ctrl+Alt+F9",
            HotkeyServerConsole = "Ctrl+Alt+S",
            HotkeyServices = "Ctrl+Shift+S"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Equal("Shift+F9", loaded.HotkeyPlatformUpdate);
        Assert.Equal("Ctrl+Shift+F9", loaded.HotkeyCheckUpdate);
        Assert.Equal("Ctrl+Alt+F9", loaded.HotkeyActualReleases);
        Assert.Equal("Ctrl+Alt+S", loaded.HotkeyServerConsole);
        Assert.Equal("Ctrl+Shift+S", loaded.HotkeyServices);
    }

    [Fact]
    public void AppSettings_PlatformUpdateHotkey_EmptyJson_UsesDefault()
    {
        // Запись из старого/неполного settings.json: поле отсутствует — остаётся
        // дефолт Ctrl+F9 (инициализатор свойства AppSettings).
        var loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!;

        Assert.Equal("Ctrl+F9", loaded.HotkeyPlatformUpdate);
    }

    // ---- Разбор жестов тем же парсером, что и регистрация ----

#if !LINUX
    [Theory]
    [InlineData("Ctrl+F9", System.Windows.Input.ModifierKeys.Control)]
    [InlineData("Shift+F9", System.Windows.Input.ModifierKeys.Shift)]
    [InlineData("Alt+F9", System.Windows.Input.ModifierKeys.Alt)]
    [InlineData("Ctrl+Shift+F9", System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift)]
    public void TryParseKeyGesture_PlatformUpdateGestures_Resolve(string gesture, System.Windows.Input.ModifierKeys expected)
    {
        // Жест обязан разбираться парсером главного окна (MainWindow.TryParseKeyGesture):
        // иначе сочетание сохранится в настройках, но не сработает (issue #38).
        var ok = Workspace1C.MainWindow.TryParseKeyGesture(gesture, out var key, out var modifiers);

        Assert.True(ok);
        Assert.Equal(System.Windows.Input.Key.F9, key);
        Assert.Equal(expected, modifiers);
    }

    [Fact]
    public void TryParseKeyGesture_EmptyPlatformUpdate_IsNotResolved()
    {
        // Пустое значение («Нет») не должно регистрироваться как жест.
        Assert.False(Workspace1C.MainWindow.TryParseKeyGesture(
            "", out _, out _));
    }
#else
    [Theory]
    [InlineData("Ctrl+F9", Avalonia.Input.KeyModifiers.Control)]
    [InlineData("Shift+F9", Avalonia.Input.KeyModifiers.Shift)]
    [InlineData("Alt+F9", Avalonia.Input.KeyModifiers.Alt)]
    [InlineData("Ctrl+Shift+F9", Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift)]
    public void HotkeyBox_TryParse_PlatformUpdateGestures_Resolve(string gesture, Avalonia.Input.KeyModifiers expected)
    {
        // Avalonia-парсер контрола ввода (Controls.HotkeyBox.TryParse) используется
        // регистрацией хоткеев Linux-версии (MainWindow.Avalonia.Hotkeys.cs): сочетание
        // «Окно обновления платформы» обязано разбираться и на этой платформе.
        var ok = Workspace1C.Controls.HotkeyBox.TryParse(gesture, out var parsed);

        Assert.True(ok);
        Assert.NotNull(parsed);
        Assert.Equal(Avalonia.Input.Key.F9, parsed!.Key);
        Assert.Equal(expected, parsed.KeyModifiers);
    }

    [Fact]
    public void HotkeyBox_TryParse_EmptyPlatformUpdate_IsNotResolved()
    {
        Assert.False(Workspace1C.Controls.HotkeyBox.TryParse("", out _));
    }
#endif
}