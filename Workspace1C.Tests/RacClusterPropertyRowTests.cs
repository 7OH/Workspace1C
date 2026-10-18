using System.Text.Json;
using Workspace1C.Localization;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты человекочитаемого отображения вкладки «Информация о кластере» (issue #3,
/// комментарий 7OH от 2026-10-17): таймауты — «1 ч 30 мин», память — «512 МБ»,
/// булевы — «Да/Нет», группировка по секциям.
/// </summary>
public sealed class RacClusterPropertyRowTests
{
    [Fact]
    public void FormatDurationSeconds_HoursMinutesSeconds()
    {
        Assert.Equal(
            "1 " + LocalizationManager.T("ServerMonitor.HoursUnit")
            + " 30 " + LocalizationManager.T("ServerMonitor.MinutesUnit"),
            RacClusterPropertyRow.FormatDurationSeconds("5400"));

        Assert.Equal(
            "45 " + LocalizationManager.T("ServerMonitor.MinutesUnit")
            + " 15 " + LocalizationManager.T("ServerMonitor.SecondsUnit"),
            RacClusterPropertyRow.FormatDurationSeconds("2715"));

        Assert.Equal(
            "12 " + LocalizationManager.T("ServerMonitor.SecondsUnit"),
            RacClusterPropertyRow.FormatDurationSeconds("12"));

        Assert.Equal(
            "0 " + LocalizationManager.T("ServerMonitor.SecondsUnit"),
            RacClusterPropertyRow.FormatDurationSeconds("0"));
    }

    [Fact]
    public void FormatBytes_Units()
    {
        Assert.Equal(
            "1 " + LocalizationManager.T("ServerMonitor.GbUnit"),
            RacClusterPropertyRow.FormatBytes("1073741824"));
        Assert.Equal(
            "512 " + LocalizationManager.T("ServerMonitor.MbUnit"),
            RacClusterPropertyRow.FormatBytes("536870912"));
        Assert.Equal(
            "1.5 " + LocalizationManager.T("ServerMonitor.GbUnit"),
            RacClusterPropertyRow.FormatBytes("1610612736"));
        Assert.Equal(
            "4 " + LocalizationManager.T("ServerMonitor.KbUnit"),
            RacClusterPropertyRow.FormatBytes("4096"));
    }

    [Fact]
    public void FormatBool_YesNo()
    {
        Assert.Equal(LocalizationManager.T("Common.Yes"), RacClusterPropertyRow.FormatBool("1"));
        Assert.Equal(LocalizationManager.T("Common.No"), RacClusterPropertyRow.FormatBool("0"));
        Assert.Equal(LocalizationManager.T("Common.Yes"), RacClusterPropertyRow.FormatBool("true"));
        Assert.Equal(LocalizationManager.T("Common.No"), RacClusterPropertyRow.FormatBool("false"));
    }

    [Fact]
    public void FormatValue_ByKey()
    {
        // Таймаут (секунды): ровно 1 час — «1 ч».
        Assert.Contains(
            LocalizationManager.T("ServerMonitor.HoursUnit"),
            RacClusterPropertyRow.FormatValue("sessionIdleTimeout", "3600"));
        // 1 час 30 минут.
        Assert.Contains(
            LocalizationManager.T("ServerMonitor.MinutesUnit"),
            RacClusterPropertyRow.FormatValue("sessionIdleTimeout", "5400"));
        // Память (байты).
        Assert.Contains(
            LocalizationManager.T("ServerMonitor.MbUnit"),
            RacClusterPropertyRow.FormatValue("maxMemorySize", "1048576"));
        // Миллисекунды.
        Assert.EndsWith(
            LocalizationManager.T("ServerMonitor.MsUnit"),
            RacClusterPropertyRow.FormatValue("ping-period", "1000"));
        // Неизвестный ключ — как есть.
        Assert.Equal("42", RacClusterPropertyRow.FormatValue("someUnknownKey", "42"));
    }

    [Fact]
    public void DisplayValue_And_Group_FromConstructor()
    {
        var row = new RacClusterPropertyRow("expiration-timeout", "3600", editable: true);

        Assert.Contains(LocalizationManager.T("ServerMonitor.HoursUnit"), row.DisplayValue);
        // Исходное значение для правки — число rac, а не человекочитаемый текст.
        Assert.Equal("3600", row.OriginalValue);
        Assert.Equal("3600", row.EditValue);
        Assert.Equal("Sessions", RacClusterPropertyRow.GroupKey(row.RawKey));

        Assert.Equal("General", RacClusterPropertyRow.GroupKey("name"));
        Assert.Equal("Security", RacClusterPropertyRow.GroupKey("security-level"));
        Assert.Equal("Other", RacClusterPropertyRow.GroupKey("uzzy-unknown"));
    }

    [Fact]
    public void ToDictionaryKeyForm_MatchesDictionaryKeys()
    {
        // Ключи словаря хранятся в форме «первая буква строчная» (ru.json/en.json):
        // дефисы устранены, первая буква после разделителя — заглавная.
        Assert.Equal("expirationTimeout", RacClusterPropertyRow.ToDictionaryKeyForm("expiration-timeout"));
        Assert.Equal("hostName", RacClusterPropertyRow.ToDictionaryKeyForm("hostName"));
        Assert.Equal("name", RacClusterPropertyRow.ToDictionaryKeyForm("name"));
        Assert.Equal("securitySessionIdleTimeout", RacClusterPropertyRow.ToDictionaryKeyForm("security-session-idle-timeout"));
        Assert.Equal("cluster", RacClusterPropertyRow.ToDictionaryKeyForm("cluster"));
    }

    [Fact]
    public void KnownClusterPropertyKeys_HaveRussianTranslationsInDictionary()
    {
        // issue #3 (18.10 08:57): названия параметров вкладки «Информация о кластере»
        // на русском. Словарь локализации (ru.json) хранит ключи в форме
        // «первая буква строчная» — для каждого известного rac-параметра такой ключ
        // обязан существовать и содержать перевод (не «голый» ключ).
        // В тестовой среде LocalizationManager не инициализирован (T возвращает ключ),
        // поэтому словарь читаем напрямую из файла, как тесты покрытия локализации.
        var ru = ReadRuStrings();

        foreach (var raw in new[] { "name", "hostName", "expiration-timeout", "cluster", "host", "security-session-idle-timeout" })
        {
            var key = "ServerMonitor.ClusterProperty." + RacClusterPropertyRow.ToDictionaryKeyForm(raw);
            Assert.True(ru.TryGetValue(key, out var value), $"Нет перевода «{key}» в ru.json");
            Assert.False(string.IsNullOrWhiteSpace(value), $"Пустой перевод «{key}» в ru.json");
            Assert.NotEqual(key, value);
        }
    }

    [Fact]
    public void DisplayName_UnknownKey_FallsBackToRawKey()
    {
        Assert.Equal("someUnknownKey", new RacClusterPropertyRow("someUnknownKey", "1", false).DisplayName);
    }

    /// <summary>Словарь строк ru.json (как тесты покрытия локализации) из корня репозитория.</summary>
    private static Dictionary<string, string> ReadRuStrings()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
            {
                var json = File.ReadAllText(
                    Path.Combine(dir.FullName, "Workspace1C", "Localization", "Languages", "ru.json"));
                using var doc = JsonDocument.Parse(json);
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var property in doc.RootElement.GetProperty("strings").EnumerateObject())
                    map[property.Name] = property.Value.GetString() ?? string.Empty;
                return map;
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
    }
}