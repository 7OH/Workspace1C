using System;
using System.Globalization;
using Workspace1C.Localization;

namespace Workspace1C.ViewModels;

/// <summary>
/// Строка вкладки «Информация о кластере» окна «Серверы 1С» в виде «свойство — значение»
/// (issue #324, C3: свойства кластера показываются в виде, доступном для правки, а не
/// просто текстом). Редактируется ОГРАНИЧЕННЫЙ безопасный набор параметров
/// (см. <see cref="RacClusterUpdate"/>): имя, таймауты, лимиты памяти, уровень
/// безопасности, пинг, параметры аутентификации. Остальные свойства — только чтение.
/// С 1.0.0.9 (issue #3, комментарий 7OH от 2026-10-17) значения показываются
/// ЧЕЛОВЕКОЧИТАЕМЫМИ: таймауты — «1 ч 30 мин», память — «512 МБ», булевы — «Да/Нет»;
/// строки сгруппированы в секции («Основные», «Безопасность», «Сеансы и процессы»).
/// В поле правки по-прежнему редактируется ИСХОДНОЕ значение rac (число), чтобы
/// команда «cluster update» получила корректный параметр.
/// </summary>
public sealed class RacClusterPropertyRow : ViewModelBase
{
    /// <summary>Исходный ключ свойства из вывода rac (например «expiration-timeout»).</summary>
    public string RawKey { get; }

    /// <summary>Локализованное имя свойства для показа пользователю.</summary>
    public string DisplayName { get; }

    /// <summary>Локализованное имя секции, в которую сгруппировано свойство.</summary>
    public string Group { get; }

    /// <summary>Можно ли править значение (безопасный набор параметров).</summary>
    public bool IsEditable { get; }

    /// <summary>Значение из вывода rac до правки (эталон для поиска изменений).</summary>
    public string OriginalValue { get; }

    /// <summary>Человекочитаемое значение для отображения (issue #3): таймауты — «1 ч»,
    /// память — «512 МБ», булевы — «Да/Нет»; для правки используется исходное значение.</summary>
    public string DisplayValue { get; }

    private string _editValue;

    public RacClusterPropertyRow(string rawKey, string value, bool editable)
    {
        RawKey = rawKey ?? string.Empty;
        DisplayName = LocalizeKey(RawKey);
        Group = LocalizeGroup(GroupKey(RawKey));
        IsEditable = editable;
        OriginalValue = value ?? string.Empty;
        DisplayValue = FormatValue(RawKey, OriginalValue);
        _editValue = OriginalValue;
    }

    /// <summary>Текущее значение в поле правки.</summary>
    public string EditValue
    {
        get => _editValue;
        set
        {
            if (SetProperty(ref _editValue, value ?? string.Empty))
                OnPropertyChanged(nameof(IsChanged));
        }
    }

    /// <summary>Значение изменено относительно вывода rac (и строка вообще правится).</summary>
    public bool IsChanged =>
        IsEditable && !string.Equals(_editValue, OriginalValue, StringComparison.Ordinal);

    /// <summary>
    /// Локализованное имя свойства по ключу rac (дефисы/подчёркивания нормализуются:
    /// «expiration-timeout» → «ServerMonitor.ClusterProperty.ExpirationTimeout»);
    /// для неизвестных ключей показывается исходный ключ rac.
    /// </summary>
    private static string LocalizeKey(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
            return rawKey ?? string.Empty;

        var normalized = Normalize(rawKey);
        var key = "ServerMonitor.ClusterProperty." + normalized;
        var localized = LocalizationManager.T(key);
        // LocalizationManager.T возвращает сам ключ при отсутствии перевода —
        // в этом случае показываем исходный ключ rac (свойство «как есть»).
        return string.Equals(localized, key, StringComparison.Ordinal) ? rawKey : localized;
    }

    /// <summary>Каноническая форма ключа: без дефисов/подчёркиваний/точек, PascalCase.</summary>
    internal static string Normalize(string rawKey)
    {
        var normalized = new System.Text.StringBuilder(rawKey.Length);
        var upperNext = true;
        foreach (var ch in rawKey)
        {
            if (ch is '-' or '_' or '.')
            {
                upperNext = true;
                continue;
            }
            normalized.Append(upperNext ? char.ToUpperInvariant(ch) : ch);
            upperNext = false;
        }
        return normalized.ToString();
    }

    /// <summary>Ключ секции для группировки (issue #3, комментарий 7OH): обобщённые группы.</summary>
    internal static string GroupKey(string rawKey) => Normalize(rawKey) switch
    {
        "Name" or "HostName" or "Port" or "LoadBalancingMode" => "General",
        "SecurityLevel" or "SecurityProfileName" or "SessionFaultToleranceLevel" or
        "AllowAccessRightAuditEventsRecording" or "MaxAuthAttempts" or "AuthLockDuration" => "Security",
        "ExpirationTimeout" or "LifetimeLimit" or "MaxMemorySize" or "MaxMemoryTimeLimit" or
        "KillProblemProcesses" or "KillByMemoryWithDump" or "ErrorsCountThreshold" or
        "SessionIdleTimeout" or "SessionMaxMemorySize" or "SessionMaxTimeLimit" or
        "RestartSchedule" or "PingPeriod" or "PingTimeout" => "Sessions",
        _ => "Other"
    };

    /// <summary>Локализованное имя секции (в настройках/словаре, иначе исходный ключ группы).</summary>
    internal static string LocalizeGroup(string group) => group switch
    {
        "General" => LocalizationManager.T("ServerMonitor.ClusterGroup.General"),
        "Security" => LocalizationManager.T("ServerMonitor.ClusterGroup.Security"),
        "Sessions" => LocalizationManager.T("ServerMonitor.ClusterGroup.Sessions"),
        _ => LocalizationManager.T("ServerMonitor.ClusterGroup.Other")
    };

    /// <summary>
    /// Человекочитаемое значение свойства (issue #3): булевы «0/1» → «Нет/Да»,
    /// таймауты (секунды) → «1 ч 30 мин», память (байты) → «512 МБ»,
    /// миллисекунды → «1000 мс»; прочие значения — как есть (числа rac).
    /// </summary>
    internal static string FormatValue(string rawKey, string rawValue)
    {
        var value = (rawValue ?? string.Empty).Trim();
        if (value.Length == 0)
            return "—";

        switch (Normalize(rawKey))
        {
            case "KillProblemProcesses" or "KillByMemoryWithDump" or "AllowAccessRightAuditEventsRecording":
                return FormatBool(value);
            case "ExpirationTimeout" or "LifetimeLimit" or "MaxMemoryTimeLimit" or
                 "PingTimeout" or "AuthLockDuration" or "SessionIdleTimeout" or "SessionMaxTimeLimit":
                return FormatDurationSeconds(value);
            case "MaxMemorySize" or "SessionMaxMemorySize":
                return FormatBytes(value);
            case "PingPeriod":
                return AppendUnit(value, "ServerMonitor.MsUnit");
            default:
                return value;
        }
    }

    /// <summary>«0»/«false» → «Нет»; «1»/«true» → «Да»; иначе исходное значение.</summary>
    internal static string FormatBool(string value) => value.ToLowerInvariant() switch
    {
        "0" or "false" or "no" => LocalizationManager.T("Common.No"),
        "1" or "true" or "yes" => LocalizationManager.T("Common.Yes"),
        _ => value
    };

    /// <summary>Секунды → «1 ч 30 мин», «45 мин», «12 с» (инвариантный разбор).</summary>
    internal static string FormatDurationSeconds(string value)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds < 0)
            return AppendUnit(value, "ServerMonitor.SecondsUnit");

        if (seconds == 0)
            return "0 " + LocalizationManager.T("ServerMonitor.SecondsUnit");

        var hours = seconds / 3600;
        var minutes = seconds % 3600 / 60;
        var restSeconds = seconds % 60;

        var sb = new System.Text.StringBuilder();
        if (hours > 0)
        {
            sb.Append(hours).Append(' ').Append(LocalizationManager.T("ServerMonitor.HoursUnit"));
            if (minutes > 0)
                sb.Append(' ').Append(minutes).Append(' ').Append(LocalizationManager.T("ServerMonitor.MinutesUnit"));
        }
        else if (minutes > 0)
        {
            sb.Append(minutes).Append(' ').Append(LocalizationManager.T("ServerMonitor.MinutesUnit"));
            if (restSeconds > 0)
                sb.Append(' ').Append(restSeconds).Append(' ').Append(LocalizationManager.T("ServerMonitor.SecondsUnit"));
        }
        else
        {
            sb.Append(restSeconds).Append(' ').Append(LocalizationManager.T("ServerMonitor.SecondsUnit"));
        }
        return sb.ToString();
    }

    /// <summary>Байты → «512 МБ», «1,5 ГБ», «1 КБ» (округление до одной десятой).</summary>
    internal static string FormatBytes(string value)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes) || bytes < 0)
            return AppendUnit(value, "ServerMonitor.BytesUnit");

        const double kb = 1024.0;
        const double mb = kb * 1024.0;
        const double gb = mb * 1024.0;

        if (bytes >= gb)
            return FormatBytesValue(bytes / gb) + " " + LocalizationManager.T("ServerMonitor.GbUnit");
        if (bytes >= mb)
            return FormatBytesValue(bytes / mb) + " " + LocalizationManager.T("ServerMonitor.MbUnit");
        if (bytes >= kb)
            return FormatBytesValue(bytes / kb) + " " + LocalizationManager.T("ServerMonitor.KbUnit");
        return bytes + " " + LocalizationManager.T("ServerMonitor.BytesUnit");
    }

    private static string FormatBytesValue(double value)
    {
        var rounded = Math.Round(value, 1);
        // Целое число — без дробной части («2 ГБ», а не «2,0 ГБ»).
        return rounded == Math.Floor(rounded)
            ? rounded.ToString("0", CultureInfo.InvariantCulture)
            : rounded.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static string AppendUnit(string value, string unitKey)
        => value + " " + LocalizationManager.T(unitKey);
}
