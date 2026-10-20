using System;
using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты параметров блокировки сеансов (issue #20): строка /LockIB конфигуратора,
/// финальное сообщение с плейсхолдерами {ДатаНач}/{ДатаКон} и строка файла 1cv8.cdn
/// (формат, даты ГГГГММДДЧЧММСС, экранирование кавычек, код разрешения).
/// </summary>
public sealed class SessionLockOptionsTests
{
    [Fact]
    public void BuildCdnString_FormatsFlagDatesMessageAndCode()
    {
        var options = new SessionLockOptions
        {
            StartTime = new DateTime(2026, 10, 20, 10, 0, 0),
            Duration = TimeSpan.FromMinutes(120),
            Message = "Технические работы",
            PermissionCode = "SECRET"
        };

        var cdn = options.BuildCdnString();

        Assert.Equal("{1,20261020100000,20261020120000,\"Технические работы\",\"SECRET\"}", cdn);
    }

    [Fact]
    public void BuildCdnString_EscapesQuotesInMessageAndCode()
    {
        var options = new SessionLockOptions
        {
            StartTime = new DateTime(2026, 10, 20, 10, 0, 0),
            Duration = TimeSpan.FromMinutes(30),
            Message = "Скажи \"привет\"",
            PermissionCode = "a\"b"
        };

        var cdn = options.BuildCdnString();

        Assert.Contains("\"Скажи \"\"привет\"\"\"", cdn);
        Assert.Contains("\"a\"\"b\"", cdn);
    }

    [Fact]
    public void BuildCdnString_EmptyPermissionCode_ProducesEmptyQuotedValue()
    {
        var options = new SessionLockOptions
        {
            StartTime = new DateTime(2026, 10, 20, 10, 0, 0),
            Duration = TimeSpan.FromMinutes(30),
            Message = "Работы",
            PermissionCode = ""
        };

        var cdn = options.BuildCdnString();

        Assert.EndsWith("\"\"}", cdn);
    }

    [Fact]
    public void BuildCdnString_SubstitutesDatePlaceholdersInMessage()
    {
        var options = new SessionLockOptions
        {
            StartTime = new DateTime(2026, 10, 20, 10, 0, 0),
            Duration = TimeSpan.FromMinutes(30),
            Message = "С {ДатаНач} до {ДатаКон}"
        };

        var cdn = options.BuildCdnString();

        Assert.Contains("\"С 20.10.2026 10:00 до 20.10.2026 10:30\"", cdn);
    }

    [Fact]
    public void BuildFinalMessage_ReplacesPlaceholders()
    {
        var options = new SessionLockOptions
        {
            StartTime = new DateTime(2026, 10, 20, 10, 0, 0),
            Duration = TimeSpan.FromMinutes(30),
            Message = "С {ДатаНач} до {ДатаКон}"
        };

        Assert.Equal("С 20.10.2026 10:00 до 20.10.2026 10:30", options.BuildFinalMessage());
    }

    [Fact]
    public void BuildSessionLockString_UsesDesignerFormat()
    {
        var options = new SessionLockOptions
        {
            StartTime = new DateTime(2026, 10, 20, 10, 0, 0),
            Duration = TimeSpan.FromMinutes(30),
            Message = "Работы"
        };

        Assert.Equal("20.10.2026 10:00:00;30;Работы", options.BuildSessionLockString());
    }
}