using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты исправления issue #26 «Акцентные цвета и контраст HEX-полей»:
/// (1) сохранённые правки акцентов встроенных тем возвращаются при повторном чтении
/// (а не дефолтные #FDBF00/#E0A800), (2) контрастный цвет HEX-текста выбирается по
/// luminance фона палитры и в тёмной палитре ≥ 4.5:1, (3) чистая логика без UI.
/// </summary>
public sealed class AccentColorIssue26Tests
{
    // ===================== Контраст HEX-текста =====================

    [Fact]
    public void DarkScheme_HexTextIsLightAndMeetsWcag()
    {
        // Тёмная палитра по умолчанию: фон формы #0F172A. Цвет HEX-текста должен
        // быть светлым (белый), а не чёрным, и контрастировать ≥ 4.5:1.
        var scheme = ColorScheme.CreateDark();
        var background = scheme.PaletteValue(true, "ContentBackgroundColor");
        var textHex = ColorScheme.ContrastTextHex(background);

        Assert.Equal("#FFFFFF", textHex);
        Assert.NotEqual("#000000", textHex);
        Assert.True(ColorScheme.ContrastRatio(textHex, background) >= 4.5,
            $"Контраст текста {textHex} на фоне {background} должен быть ≥ 4.5:1");
    }

    [Fact]
    public void DarkBackground_DarkTextWouldFailButWhitePasses()
    {
        // Контрольный замер: чёрный текст на тёмном фоне — это и была жалоба issue #26
        // (текст «практически сливается»), белый — достаточный контраст.
        const string darkBackground = "#0F172A";
        Assert.True(ColorScheme.ContrastRatio("#000000", darkBackground) < 4.5,
            "Чёрный текст на тёмном фоне обязан давать малый контраст (баг до исправления)");
        Assert.True(ColorScheme.ContrastRatio("#FFFFFF", darkBackground) >= 4.5);
    }

    [Fact]
    public void LightScheme_HexTextIsDarkAndMeetsWcag()
    {
        // Светлая палитра по умолчанию: фон формы #F1F5F9. Цвет HEX-текста — тёмный,
        // контраст ≥ 4.5:1 (контраст должен читаться и в светлой теме).
        var scheme = ColorScheme.CreateLight();
        var background = scheme.PaletteValue(false, "ContentBackgroundColor");
        var textHex = ColorScheme.ContrastTextHex(background);

        Assert.Equal("#000000", textHex);
        Assert.True(ColorScheme.ContrastRatio(textHex, background) >= 4.5,
            $"Контраст текста {textHex} на фоне {background} должен быть ≥ 4.5:1");
    }

    [Theory]
    [InlineData("#000000", "#FFFFFF")]  // чёрный фон → белый текст
    [InlineData("#0F172A", "#FFFFFF")]  // тёмно-синий фон → белый текст
    [InlineData("#FFFFFF", "#000000")]  // белый фон → чёрный текст
    [InlineData("#F1F5F9", "#000000")]  // светло-серый фон → чёрный текст
    public void ContrastTextHex_PicksMaximumContrast(string background, string expected)
    {
        Assert.Equal(expected, ColorScheme.ContrastTextHex(background));
        // Выбранный текст контрастен фону (≥ 4.5:1) — не сливается с ним.
        Assert.True(ColorScheme.ContrastRatio(expected, background) >= 4.5);
    }

    [Fact]
    public void RelativeLuminance_ExtremesAreZeroAndOne()
    {
        Assert.InRange(ColorScheme.RelativeLuminance("#000000"), 0.0, 0.02);
        Assert.InRange(ColorScheme.RelativeLuminance("#FFFFFF"), 0.98, 1.0);
    }

    [Fact]
    public void ContrastRatio_OrderOfArgumentsDoesNotMatter()
    {
        var ratio12 = ColorScheme.ContrastRatio("#FFFFFF", "#000000");
        var ratio21 = ColorScheme.ContrastRatio("#000000", "#FFFFFF");
        Assert.Equal(ratio12, ratio21);
        Assert.True(ratio12 >= 21 - 0.01);
    }

    // ===================== Сохранение/повторное чтение правок акцентов =====================

    [Fact]
    public void EditedAccents_SurviveJsonRoundTrip()
    {
        // Сценарий: пользователь изменил «Акцентный цвет» и «Акцент (наведение)»
        // встроенной «Тёмной» темы → схема сохраняется в AppSettings (JSON).
        var edited = ColorScheme.CreateDark();
        edited.DarkColors["AccentColor"] = "#FF4500";
        edited.DarkColors["AccentHoverColor"] = "#E03400";
        edited.DarkColors["AccentPressedColor"] = "#C22E00";
        edited.AccentColor = "#FF4500";

        var restored = ColorScheme.FromJson(edited.ToJson());

        Assert.NotNull(restored);
        Assert.Equal("Тёмная", restored!.Name);
        // Повторное чтение возвращает отредактированные значения, а не дефолты #FFB300/#FFCA28.
        Assert.Equal("#FF4500", restored.DarkColors["AccentColor"]);
        Assert.Equal("#E03400", restored.DarkColors["AccentHoverColor"]);
        Assert.Equal("#FF4500", restored.AccentColor);
    }

    [Fact]
    public void RestoredScheme_IsNotOverwrittenByBuiltInDefaults()
    {
        // Редактор цветов берёт значения через PaletteValue(dark, key): сохранённые
        // правки должны перекрывать дефолтную палитру встроенной темы.
        var edited = ColorScheme.CreateDark();
        edited.DarkColors["AccentColor"] = "#22D3EE";

        var restored = ColorScheme.FromJson(edited.ToJson());
        restored!.Normalize();

        Assert.Equal("#22D3EE", restored.PaletteValue(true, "AccentColor"));
        Assert.NotEqual("#FFB300", restored.PaletteValue(true, "AccentColor"));
    }

    [Fact]
    public void FromLegacyActive_KeepsEditedAccents()
    {
        // Путь при старте приложения (App.OnStartup): активная схема с правками
        // мигрируется в единую схему — правки акцентов не должны теряться.
        var active = ColorScheme.CreateDark();
        active.DarkColors["AccentColor"] = "#3B82F6";
        active.DarkColors["AccentHoverColor"] = "#2563EB";

        var merged = ColorScheme.FromLegacy(active, light: null, dark: null);

        Assert.Equal("#3B82F6", merged.PaletteValue(true, "AccentColor"));
        Assert.Equal("#2563EB", merged.PaletteValue(true, "AccentHoverColor"));
    }

    [Fact]
    public void FullThemeScheme_KeepsDefaultsButEditedActiveWins()
    {
        // «Чистая» схема полноценной темы (каталог) не содержит пользовательских правок,
        // а отредактированная активная схема — содержит: при повторном входе редактор
        // обязан показать сохранённые значения (issue #26).
        var themeDark = ColorScheme.CreateDark();
        Assert.Equal("#FFB300", themeDark.DarkColors["AccentColor"]); // дефолт темы

        var editedActive = ColorScheme.CreateDark();
        editedActive.DarkColors["AccentColor"] = "#F43F5E";
        editedActive.DarkColors["AccentHoverColor"] = "#E11D48";
        editedActive.AccentColor = "#F43F5E";
        editedActive.Normalize();

        Assert.Equal("#F43F5E", editedActive.PaletteValue(true, "AccentColor"));
        Assert.NotEqual(themeDark.DarkColors["AccentColor"], editedActive.DarkColors["AccentColor"]);
    }

    [Fact]
    public void EditedLightAccent_IsNotYellowDefault()
    {
        // Жалоба ArtyoZ после 1.0.0.30: образцы «Акцентный цвет»/«Акцент (наведение)»
        // встроенной «Светлой» снова показывали дефолтные #FDBF00/#E0A800 после
        // установки своих цветов. Правки светлой палитры должны перекрывать дефолты.
        var edited = ColorScheme.CreateLight();
        edited.LightColors["AccentColor"] = "#2563EB";
        edited.LightColors["AccentHoverColor"] = "#1D4ED8";
        edited.AccentColor = "#2563EB";

        var restored = ColorScheme.FromJson(edited.ToJson());
        restored!.Normalize();

        Assert.Equal("#2563EB", restored.PaletteValue(false, "AccentColor"));
        Assert.NotEqual("#FDBF00", restored.PaletteValue(false, "AccentColor"));
        Assert.Equal("#1D4ED8", restored.PaletteValue(false, "AccentHoverColor"));
        Assert.NotEqual("#E0A800", restored.PaletteValue(false, "AccentHoverColor"));
    }

#if !LINUX
    // ===================== Единый источник правок встроенных тем (SettingsViewModel) =====================

    [Fact]
    public void TryGetActiveEditsFor_ReturnsSavedEditsForMatchingBuiltInName()
    {
        // Активная схема — встроенная «Тёмная» с правками акцента: повторное открытие
        // редактора должно вернуть эти правки, а не «чистую» встроенную схему.
        var active = ColorScheme.CreateDark();
        active.DarkColors["AccentColor"] = "#7C3AED";
        active.DarkColors["AccentHoverColor"] = "#6D28D9";
        active.AccentColor = "#7C3AED";

        var edits = ViewModels.SettingsViewModel.TryGetActiveEditsFor(active, "Тёмная");

        Assert.NotNull(edits);
        Assert.Equal("#7C3AED", edits!.DarkColors["AccentColor"]);
        Assert.Equal("#6D28D9", edits.DarkColors["AccentHoverColor"]);
    }

    [Fact]
    public void TryGetActiveEditsFor_ReturnedCloneIsIndependent()
    {
        var active = ColorScheme.CreateDark();
        active.DarkColors["AccentColor"] = "#22C55E";

        var edits = ViewModels.SettingsViewModel.TryGetActiveEditsFor(active, "Тёмная");
        edits!.DarkColors["AccentColor"] = "#FF0000";

        // Правка рабочей копии не меняет активную схему и наоборот.
        Assert.Equal("#22C55E", active.DarkColors["AccentColor"]);
        Assert.Equal("#FF0000", edits.DarkColors["AccentColor"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Светлая")]
    [InlineData("Синяя")]
    public void TryGetActiveEditsFor_ReturnsNullWhenActiveIsDifferent(string? name)
    {
        // Активная «Тёмная» — а просят другую тему: для неё загружается чистая
        // встроенная/каталоговая схема (правок в этой теме нет).
        var active = ColorScheme.CreateDark();
        Assert.Null(ViewModels.SettingsViewModel.TryGetActiveEditsFor(active, name!));
    }
#endif
}
