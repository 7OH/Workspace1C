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
}