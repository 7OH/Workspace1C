using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты данных встроенного набора типовых конфигураций (issue #321, часть 1):
/// поле «Имя конфигурации» (<c>OneCConfigType.ConfigName</c>) заполнено по списку
/// пользователя и НЕ участвует в построении адреса обновлений.
/// </summary>
public sealed class BuiltInConfigTypesTests
{
    [Theory]
    [InlineData("BP", "БухгалтерияПредприятия")]
    [InlineData("BPCorp", "БухгалтерияПредприятияКОРП")]
    [InlineData("ZUP", "ЗарплатаИУправлениеПерсоналом")]
    [InlineData("ZUPCORP", "ЗарплатаИУправлениеПерсоналомКОРП")]
    [InlineData("DOPROF", "ДокументооборотПРОФ")]
    [InlineData("DOCORP", "ДокументооборотКОРП")]
    [InlineData("UT", "УправлениеТорговлей")]
    [InlineData("KA", "КомплекснаяАвтоматизация")]
    [InlineData("RMK", "РМКБазовая")]
    [InlineData("Retail", "Retail")]
    [InlineData("UNF", "УправлениеНашейФирмой")]
    [InlineData("UTP", "УправлениеПроизводственнымПредприятием")]
    [InlineData("ERP", "УправлениеПредприятием")]
    [InlineData("BGU", "БухгалтерияГосударственногоУчреждения")]
    public void BuiltInConfigs_HaveConfigName_AsSpecifiedByUser(string code, string expected)
    {
        var config = GetByCode(code);

        Assert.Equal(expected, config.ConfigName);
    }

    [Fact]
    public void AllBuiltInConfigs_HaveNonEmptyConfigName()
    {
        // Список 7OH (issue #321): для всех поставляемых конфигураций имя задано.
        foreach (var config in BuiltInConfigTypes.All)
            Assert.False(string.IsNullOrWhiteSpace(config.ConfigName),
                $"ConfigName пуст у конфигурации {config.Code}");
    }

    [Fact]
    public void ConfigName_DoesNotAffectEffectiveUrlCode()
    {
        // «Имя конфигурации» служит для сопоставления и не попадает в адрес обновлений:
        // EffectiveUrlCode строится из UrlCode/Name, а не из ConfigName.
        foreach (var config in BuiltInConfigTypes.All)
        {
            Assert.NotEqual(config.ConfigName, config.EffectiveUrlCode);
            Assert.Equal(
                string.IsNullOrWhiteSpace(config.UrlCode) ? config.Name : config.UrlCode,
                config.EffectiveUrlCode);
        }
    }

    [Fact]
    public void AllBuiltInConfigs_HaveNonEmptyNick()
    {
        // Ники из списка 7OH (issue #321, PR #43): у каждой поставляемой конфигурации задан
        // каталог на releases.1c.ru (после «Восстановить типовые» колонка «НИК» не пустая).
        foreach (var config in BuiltInConfigTypes.All)
            Assert.False(string.IsNullOrWhiteSpace(config.Nick),
                $"Nick пуст у конфигурации {config.Code}");
    }

    [Fact]
    public void EditionUrls_AreShortCodes_OrFullLinks()
    {
        // PR #43, 1.0.0.52: UrlOverride редакций — конечная часть адреса каталога
        // (семантика «полная ссылка или хвост», issue #369), короткие коды допустимы.
        foreach (var config in BuiltInConfigTypes.All)
        {
            foreach (var edition in config.Editions)
            {
                if (string.IsNullOrEmpty(edition.UrlOverride))
                    continue;
                Assert.True(
                    edition.UrlOverride.Contains("://") || !edition.UrlOverride.Contains('/'),
                    $"UrlOverride редакции {config.Code}/{edition.Name} должен быть ссылкой или кодом каталога");
            }
        }
    }

    [Theory]
    [InlineData("BP", new[] { "3", "2" })]
    [InlineData("BPCorp", new[] { "3", "2" })]
    [InlineData("DOPROF", new[] { "3", "2" })]
    [InlineData("DOCORP", new[] { "3", "2" })]
    [InlineData("ZUP", new[] { "3", "2" })]
    [InlineData("ZUPCORP", new[] { "3", "2" })]
    [InlineData("UT", new[] { "11", "10.3" })]
    [InlineData("KA", new[] { "2.0", "1.1", "1.0" })]
    [InlineData("Retail", new[] { "3", "2.3" })]
    [InlineData("UNF", new[] { "3", "1.6" })]
    [InlineData("UTP", new[] { "1.3", "1.2" })]
    [InlineData("BGU", new[] { "2", "1" })]
    public void Editions_IncludeUserList(string code, string[] editions)
    {
        // Редакции/каталоги релизов по списку 7OH (issue #321, PR #43).
        var config = GetByCode(code);
        foreach (var edition in editions)
            Assert.Contains(config.Editions, e => e.Red == edition);
    }

    [Theory]
    [InlineData("BP", "3.x", "Accounting30")]
    [InlineData("BP", "2.x", "Accounting20_82")]
    [InlineData("BPCorp", "3.x", "AccountingCorp30")]
    [InlineData("DOPROF", "3.x", "DocMng30")]
    [InlineData("ZUP", "3.x", "HRM30")]
    [InlineData("UT", "11.x", "Trade110")]
    [InlineData("Retail", "2.3", "Retail23")]
    [InlineData("UNF", "3.x", "SmallBusiness30")]
    [InlineData("UTP", "1.3", "Enterprise13")]
    public void Editions_WithSeparateCatalog_HaveUrlOverride(string code, string name, string expectedUrl)
    {
        // Редакция с собственным каталогом релизов (ник из списка 7OH, issue #321, PR #43):
        // UrlOverride хранит конечную часть адреса releases.1c.ru/project/<код> —
        // полная ссылка собирается автоматически (OneCUpdatesService.ResolveOverrideUrl).
        var config = GetByCode(code);
        var edition = config.Editions.FirstOrDefault(e => e.Name == name);
        Assert.NotNull(edition);
        Assert.Equal(expectedUrl, edition!.UrlOverride);
    }

    private static OneCConfigType GetByCode(string code)
    {
        var config = BuiltInConfigTypes.All.FirstOrDefault(c => c.Code == code);
        Assert.NotNull(config);
        return config!;
    }
}