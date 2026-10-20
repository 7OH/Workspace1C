using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты списка локалей информационных баз 1С (<see cref="RacLocales"/>, issue #3,
/// комментарий 7OH от 2026-10-19): двухбуквенные коды ISO 639-1 для rac
/// «infobase create --locale», нормализация полных кодов вида «ru_RU» и значение
/// по умолчанию «ru» (Русский).
/// </summary>
public sealed class RacLocalesTests
{
    [Theory]
    [InlineData("ru_RU", "ru")]
    [InlineData("en_US", "en")]
    [InlineData("uk_UA", "uk")]
    [InlineData("kk_KZ", "kk")]
    public void Normalize_FullCode_ReturnsLanguage(string locale, string expected)
    {
        Assert.Equal(expected, RacLocales.Normalize(locale));
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("kk")]
    [InlineData("ja")]
    public void Normalize_ShortCode_ReturnsAsIs(string locale)
    {
        Assert.Equal(locale, RacLocales.Normalize(locale));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Normalize_EmptyOrNull_ReturnsDefault(string? locale)
    {
        Assert.Equal(RacLocales.Default, RacLocales.Normalize(locale));
    }

    [Theory]
    [InlineData("zh-CN", "zh")]
    [InlineData("pt-BR", "pt")]
    public void Normalize_HyphenCode_ReturnsLanguage(string locale, string expected)
    {
        Assert.Equal(expected, RacLocales.Normalize(locale));
    }

    [Fact]
    public void Normalize_UnknownCode_ReturnsAsIs()
    {
        // Незнакомый код не подменяется — диалог добавит его в список выбора.
        Assert.Equal("zz", RacLocales.Normalize("zz"));
    }

    [Fact]
    public void Values_ContainsDefaultFirst()
    {
        Assert.Equal(RacLocales.Default, RacLocales.Values[0]);
        Assert.Contains(RacLocales.Default, RacLocales.Values);
    }

    [Fact]
    public void Values_AllTwoLetterCodes()
    {
        Assert.All(RacLocales.Values, v =>
        {
            Assert.Equal(2, v.Length);
            Assert.DoesNotContain('_', v);
            Assert.DoesNotContain('-', v);
        });
    }
}
