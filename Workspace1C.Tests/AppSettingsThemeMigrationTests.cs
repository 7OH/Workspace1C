using Workspace1C.Models;
using Workspace1C.Themes;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты миграции старого выбора темы (устаревшее поле <see cref="AppSettings.Theme"/>
/// со значениями "Light"/"Dark") в идентификатор полноценной темы
/// (<see cref="AppSettings.FullTheme"/>, Задача 1) и обратной совместимости после
/// добавления четырёх новых тем (Задача 4): новые строковые id
/// (material/macos/windowsxp/glass) сохраняются и восстанавливаются, а имена новых
/// встроенных тем (Material Design/macOS/Windows XP/Glass) защищены от конфликта
/// с пользовательскими цветовыми схемами.
/// </summary>
public sealed class AppSettingsThemeMigrationTests
{
    [Fact]
    public void NormalizeThemeSelection_MigratesDarkTheme()
    {
        var settings = new AppSettings { Theme = "Dark" };

        var id = settings.NormalizeThemeSelection();

        Assert.Equal("dark", id);
        Assert.Equal("dark", settings.FullTheme);
    }

    [Fact]
    public void NormalizeThemeSelection_MigratesLightTheme()
    {
        var settings = new AppSettings { Theme = "Light" };

        var id = settings.NormalizeThemeSelection();

        Assert.Equal("light", id);
        Assert.Equal("light", settings.FullTheme);
    }

    [Fact]
    public void NormalizeThemeSelection_DarkComparisonIsCaseInsensitive()
    {
        var settings = new AppSettings { Theme = "dark" };

        Assert.Equal("dark", settings.NormalizeThemeSelection());
        Assert.Equal("dark", settings.FullTheme);
    }

    [Fact]
    public void NormalizeThemeSelection_EmptyThemeFallsBackToLight()
    {
        // Пустой выбор темы (например, у новых пользователей) трактуется как светлая.
        var settings = new AppSettings { Theme = string.Empty };

        Assert.Equal("light", settings.NormalizeThemeSelection());
        Assert.Equal("light", settings.FullTheme);
    }

    [Fact]
    public void NormalizeThemeSelection_KeepsExistingFullTheme()
    {
        // Полноценная тема уже выбрана — устаревшее поле Theme её не перетирает.
        var settings = new AppSettings { Theme = "Dark", FullTheme = "blue" };

        Assert.Equal("blue", settings.NormalizeThemeSelection());
        Assert.Equal("blue", settings.FullTheme);
    }

    [Fact]
    public void NormalizeThemeSelection_IsIdempotent()
    {
        var settings = new AppSettings { Theme = "Dark" };

        settings.NormalizeThemeSelection();
        settings.NormalizeThemeSelection();

        Assert.Equal("dark", settings.FullTheme);
    }

    [Theory]
    [InlineData("material")]
    [InlineData("macos")]
    [InlineData("windowsxp")]
    [InlineData("glass")]
    public void NormalizeThemeSelection_KeepsNewThemeIds(string id)
    {
        // Новые темы (Задача 3) — обычные строковые id: нормализация не должна их
        // трогать, даже если устаревшее поле Theme заполнено (обратная совместимость).
        var settings = new AppSettings { Theme = "Dark", FullTheme = id };

        Assert.Equal(id, settings.NormalizeThemeSelection());
        Assert.Equal(id, settings.FullTheme);
    }

    [Theory]
    [InlineData("Material Design")]
    [InlineData("macOS")]
    [InlineData("Windows XP")]
    [InlineData("Glass")]
    public void IsBuiltInThemeName_RecognizesNewThemeNames(string name)
    {
        // Имена новых встроенных тем защищены от конфликта с пользовательскими схемами.
        Assert.True(ThemeFeatureMap.IsBuiltInThemeName(name));
    }

    [Theory]
    [InlineData("material design")]
    [InlineData("MACOS")]
    [InlineData("windows xp")]
    [InlineData(" glass ")]
    public void IsBuiltInThemeName_NewThemeNamesAreCaseInsensitiveAndTrimmed(string name)
    {
        Assert.True(ThemeFeatureMap.IsBuiltInThemeName(name));
    }

    [Theory]
    [InlineData("Material Design", "Material Design (пользовательская)")]
    [InlineData("macOS", "macOS (пользовательская)")]
    [InlineData("Windows XP", "Windows XP (пользовательская)")]
    [InlineData("Glass", "Glass (пользовательская)")]
    public void ResolveCustomSchemeName_AppendsSuffixForNewThemeNames(string name, string expected)
    {
        var resolved = ThemeFeatureMap.ResolveCustomSchemeName(name);

        Assert.Equal(expected, resolved);
        // Разрешённое имя больше не конфликтует со встроенной темой.
        Assert.False(ThemeFeatureMap.IsBuiltInThemeName(resolved));
    }

    [Fact]
    public void Resolve_OldBlueIdStillAppliesBlueTheme()
    {
        // Старый settings.json с FullTheme=blue открывается и применяет «Синюю».
        var theme = ThemeFeatureMap.Resolve("blue");

        Assert.Equal("blue", theme.Id);
        Assert.Equal("Синяя", theme.Name);
    }

    [Theory]
    [InlineData("material", "Material Design")]
    [InlineData("macos", "macOS")]
    [InlineData("windowsxp", "Windows XP")]
    [InlineData("glass", "Glass")]
    public void Resolve_NewThemeIdsResolveToNewThemes(string id, string name)
    {
        var theme = ThemeFeatureMap.Resolve(id);

        Assert.Equal(id, theme.Id);
        Assert.Equal(name, theme.Name);
    }

    [Theory]
    [InlineData("Dark", true)]
    [InlineData("dark", true)]
    [InlineData("DARK", true)]
    [InlineData(" Dark ", true)]
    [InlineData("Light", false)]
    [InlineData("light", false)]
    [InlineData("LIGHT", false)]
    public void ResolveDarkVariant_RecognizesSavedVariant(string theme, bool expectedDark)
    {
        // Устаревшее поле Theme ("Light"/"Dark", без учёта регистра) задаёт базовый
        // вариант поверх полноценной темы — он применяется при старте отдельно.
        Assert.Equal(expectedDark, ThemeFeatureMap.ResolveDarkVariant(theme));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Синяя")]
    public void ResolveDarkVariant_EmptyOrUnknown_ReturnsNull(string? theme)
    {
        // Вариант не задан (пусто или неизвестное значение): ApplyTheme не вызывается,
        // и полноценная тема остаётся со своим вариантом.
        Assert.Null(ThemeFeatureMap.ResolveDarkVariant(theme));
    }

    [Fact]
    public void StartupResolution_FullThemeLight_ThemeDark_AppliesDarkVariantAndKeepsFullTheme()
    {
        // Регрессия «не сохраняется тёмная тема при перезапуске (в т.ч. после
        // автообновления)»: тумблер «Светлая/Тёмная» пишет Theme="Dark", не меняя
        // FullTheme="light". Нормализация не перетирает FullTheme — тёмный вариант
        // применяется отдельно (ResolveDarkVariant → ApplyTheme(true)).
        var settings = new AppSettings { Theme = "Dark", FullTheme = "light" };

        var fullTheme = settings.NormalizeThemeSelection();
        var darkVariant = ThemeFeatureMap.ResolveDarkVariant(settings.Theme);

        Assert.Equal("light", fullTheme);
        Assert.Equal("light", settings.FullTheme);  // полноценная тема сохранена как была
        Assert.True(darkVariant == true);           // применяется тёмный вариант
    }

    [Fact]
    public void StartupResolution_FullThemeDark_NoSavedVariant_KeepsThemeVariant()
    {
        // FullTheme="dark" сам задаёт тёмный вариант, устаревшее поле пусто —
        // вариант не перещёлкивается обратно в светлый (ApplyTheme не вызывается).
        var settings = new AppSettings { FullTheme = "dark" };

        var fullTheme = settings.NormalizeThemeSelection();
        var darkVariant = ThemeFeatureMap.ResolveDarkVariant(settings.Theme);

        Assert.Equal("dark", fullTheme);
        Assert.Null(darkVariant);
    }
}