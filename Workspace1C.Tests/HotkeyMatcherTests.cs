using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #32 «Уведомлять о том, что комбинация горячих клавиш уже занята»,
/// комментарий 7OH: «горячие клавиши с Shift+любая кнопка не работают вообще;
/// с Shift+ещё один модификатор (Ctrl+Shift, Alt+Shift) работают;
/// просто Shift+кнопка — нет». Исправление: чистый матчер
/// <see cref="HotkeyMatcher"/>, сравнивающий сохранённый жест с фактическим
/// нажатием по имени ФИЗИЧЕСКОЙ клавиши (Key.ToString() в WPF/Avalonia) и
/// набору модификаторов — без нюансов платформенного KeyBinding/KeyGesture.
/// </summary>
public sealed class HotkeyMatcherTests
{
    // ---- Разбор жеста (формат HotkeyBox) ----

    [Theory]
    [InlineData("Shift+F5", "F5", HotkeyModifiers.Shift)]
    [InlineData("Shift+A", "A", HotkeyModifiers.Shift)]
    [InlineData("Shift+9", "D9", HotkeyModifiers.Shift)]
    [InlineData("Shift+0", "D0", HotkeyModifiers.Shift)]
    [InlineData("Ctrl+Shift+A", "A", HotkeyModifiers.Control | HotkeyModifiers.Shift)]
    [InlineData("Alt+Shift+F", "F", HotkeyModifiers.Alt | HotkeyModifiers.Shift)]
    [InlineData("Shift+Insert", "Insert", HotkeyModifiers.Shift)]
    [InlineData("Shift+Ins", "Insert", HotkeyModifiers.Shift)]
    [InlineData("Ctrl+F9", "F9", HotkeyModifiers.Control)]
    [InlineData("Ctrl+NumPad+", "Add", HotkeyModifiers.Control)]
    [InlineData("Ctrl++", "OemPlus", HotkeyModifiers.Control)]
    [InlineData("Ctrl+-", "OemMinus", HotkeyModifiers.Control)]
    public void TryParse_ParsesGestures(string text, string expectedKey, HotkeyModifiers expectedMods)
    {
        var ok = HotkeyMatcher.TryParse(text, out var key, out var mods);

        Assert.True(ok);
        Assert.Equal(expectedKey, key, ignoreCase: true);
        Assert.Equal(expectedMods, mods);
    }

    [Fact]
    public void TryParse_IsCaseInsensitive()
    {
        var ok = HotkeyMatcher.TryParse("shift+f5", out var key, out var mods);

        Assert.True(ok);
        Assert.Equal("F5", key, ignoreCase: true);
        Assert.Equal(HotkeyModifiers.Shift, mods);
    }

    [Theory]
    [InlineData("")]
    [InlineData("—")]
    [InlineData("-")]
    [InlineData("Нет")]
    [InlineData("None")]
    [InlineData("Shift+П")]   // символ русской раскладки — не имя клавиши
    [InlineData("Shift+Ф")]
    [InlineData("Ctrl+В")]
    [InlineData("Ctrl")]      // только модификатор — жеста нет
    [InlineData("Shift")]
    [InlineData("Alt+Часы")]
    public void TryParse_RejectsInvalidOrLayoutSymbols(string text)
    {
        Assert.False(HotkeyMatcher.TryParse(text, out _, out _));
    }

    // ---- Матчинг по физической клавише ----

    [Fact]
    public void Matches_ShiftCombinations_FindMatch()
    {
        Assert.True(HotkeyMatcher.Matches("Shift+F5", "F5", HotkeyModifiers.Shift));
        Assert.True(HotkeyMatcher.Matches("Shift+A", "A", HotkeyModifiers.Shift));
        Assert.True(HotkeyMatcher.Matches("Shift+9", "D9", HotkeyModifiers.Shift));
        Assert.True(HotkeyMatcher.Matches("Ctrl+Shift+A", "A", HotkeyModifiers.Control | HotkeyModifiers.Shift));
        Assert.True(HotkeyMatcher.Matches("Alt+Shift+F", "F", HotkeyModifiers.Alt | HotkeyModifiers.Shift));
        Assert.True(HotkeyMatcher.Matches("Shift+Insert", "Insert", HotkeyModifiers.Shift));
    }

    [Fact]
    public void Matches_WrongModifiers_DoNotMatch()
    {
        // Ctrl не совпадает с Ctrl+Shift, Alt+Shift — с Shift и т.п.:
        // матчинг строго по набору модификаторов.
        Assert.False(HotkeyMatcher.Matches("Ctrl+Shift+A", "A", HotkeyModifiers.Control));
        Assert.False(HotkeyMatcher.Matches("Ctrl+Shift+A", "A", HotkeyModifiers.Shift));
        Assert.False(HotkeyMatcher.Matches("Alt+Shift+F", "F", HotkeyModifiers.Shift));
        Assert.False(HotkeyMatcher.Matches("Alt+Shift+F", "F", HotkeyModifiers.Alt));
        Assert.False(HotkeyMatcher.Matches("Shift+A", "A", HotkeyModifiers.None));
        Assert.False(HotkeyMatcher.Matches("Shift+F5", "F6", HotkeyModifiers.Shift));
    }

    [Fact]
    public void Matches_NormalizesNamesAndIsCaseInsensitive()
    {
        // Цифры и NumPad-имена нормализуются; регистр имени клавиши не важен.
        Assert.True(HotkeyMatcher.Matches("Shift+0", "D0", HotkeyModifiers.Shift));
        Assert.True(HotkeyMatcher.Matches("shift+f5", "f5", HotkeyModifiers.Shift));
        Assert.True(HotkeyMatcher.Matches("Shift+F5", "F5", HotkeyModifiers.Shift));
        Assert.True(HotkeyMatcher.Matches("Ctrl+NumPad+", "Add", HotkeyModifiers.Control));
        Assert.True(HotkeyMatcher.Matches("Shift+?", "OemQuestion", HotkeyModifiers.Shift));
    }

    [Fact]
    public void Matches_LayoutSymbolGesture_NeverMatches()
    {
        // «Shift+П» (кириллица) — невалидная запись: не разбирается и совпадения
        // не даёт даже при правильной физической клавише. Поведение зафиксировано:
        // HotkeyBox хранит имя клавиши, а не символ раскладки.
        Assert.False(HotkeyMatcher.Matches("Shift+П", "P", HotkeyModifiers.Shift));
        Assert.False(HotkeyMatcher.Matches("Shift+Ф", "A", HotkeyModifiers.Shift));
    }

    [Fact]
    public void Matches_EmptyOrNoneGesture_NeverMatches()
    {
        Assert.False(HotkeyMatcher.Matches("", "F5", HotkeyModifiers.Shift));
        Assert.False(HotkeyMatcher.Matches("Нет", "F5", HotkeyModifiers.Shift));
        Assert.False(HotkeyMatcher.Matches("None", "F5", HotkeyModifiers.Shift));
        Assert.False(HotkeyMatcher.Matches(null, "F5", HotkeyModifiers.Shift));
    }

    // ---- Нормализация имён ----

    [Theory]
    [InlineData("Del", "Delete")]
    [InlineData("Ins", "Insert")]
    [InlineData("Esc", "Escape")]
    [InlineData("1", "D1")]
    [InlineData("9", "D9")]
    [InlineData("+", "OemPlus")]
    [InlineData("=", "OemPlus")]
    [InlineData("-", "OemMinus")]
    [InlineData("NumPad+", "Add")]
    [InlineData("NumPad-", "Subtract")]
    [InlineData("NumPad5", "NumPad5")]
    [InlineData(",", "OemComma")]
    [InlineData("?", "OemQuestion")]
    public void NormalizeKeyName_MapsDisplayNamesToKeyNames(string input, string expected)
    {
        var normalized = HotkeyMatcher.NormalizeKeyName(input);

        Assert.NotNull(normalized);
        Assert.Equal(expected, normalized, ignoreCase: true);
    }

    [Theory]
    [InlineData("П")]
    [InlineData("Ф")]
    [InlineData("Ctrl")]
    [InlineData("Shift")]
    [InlineData("None")]
    [InlineData("")]
    [InlineData(null)]
    public void NormalizeKeyName_RejectsInvalidNames(string? input)
    {
        Assert.Null(HotkeyMatcher.NormalizeKeyName(input));
    }

    // ---- Печатающие клавиши (не отбираем ввод у полей) ----

    [Theory]
    [InlineData("A", true)]
    [InlineData("P", true)]
    [InlineData("D5", true)]
    [InlineData("OemPlus", true)]
    [InlineData("Space", true)]
    [InlineData("F9", false)]
    [InlineData("F24", false)]
    [InlineData("Delete", false)]
    [InlineData("Insert", false)]
    [InlineData("Escape", false)]
    [InlineData("Left", false)]
    [InlineData("Back", false)]
    public void IsTextProducingKey_DetectsPrintableKeys(string keyName, bool expected)
    {
        Assert.Equal(expected, HotkeyMatcher.IsTextProducingKey(keyName));
    }

    // ---- Согласование с WPF-парсером регистрации (MainWindow.TryParseKeyGesture) ----

#if !LINUX
    [Theory]
    [InlineData("Shift+F9", System.Windows.Input.Key.F9, System.Windows.Input.ModifierKeys.Shift)]
    [InlineData("Shift+A", System.Windows.Input.Key.A, System.Windows.Input.ModifierKeys.Shift)]
    [InlineData("Ctrl+F9", System.Windows.Input.Key.F9, System.Windows.Input.ModifierKeys.Control)]
    [InlineData("Ctrl+Shift+F9", System.Windows.Input.Key.F9,
        System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift)]
    public void WpfTryParseKeyGesture_ShiftAndModifierCombinations_Resolve(
        string text, System.Windows.Input.Key key, System.Windows.Input.ModifierKeys mods)
    {
        var ok = Workspace1C.MainWindow.TryParseKeyGesture(text, out var parsedKey, out var parsedMods);

        Assert.True(ok);
        Assert.Equal(key, parsedKey);
        Assert.Equal(mods, parsedMods);
    }

    [Fact]
    public void WpfTryParseKeyGesture_RejectsLayoutSymbol()
    {
        // «Shift+П» (символ русской раскладки) не является именем клавиши WPF —
        // такая запись не регистрируется и не должна ломать остальные хоткеи.
        Assert.False(Workspace1C.MainWindow.TryParseKeyGesture("Shift+П", out _, out _));
    }
#endif

    // ---- Avalonia-парсер для хранения жестов (HotkeyBox.TryParse) ----

#if LINUX
    [Fact]
    public void AvaloniaHotkeyBoxTryParse_ShiftF5_ReturnsKeyGesture()
    {
        var ok = Workspace1C.Controls.HotkeyBox.TryParse("Shift+F5", out var gesture);

        Assert.True(ok);
        Assert.NotNull(gesture);
        Assert.Equal(Avalonia.Input.Key.F5, gesture.Key);
        Assert.Equal(Avalonia.Input.KeyModifiers.Shift, gesture.KeyModifiers);
    }

    [Fact]
    public void AvaloniaHotkeyBoxTryParse_RejectsLayoutSymbol()
    {
        Assert.False(Workspace1C.Controls.HotkeyBox.TryParse("Shift+П", out _));
    }
#endif
}
