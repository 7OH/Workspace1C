using System;
using System.Globalization;

namespace Workspace1C.Models;

/// <summary>
/// Полные свойства информационной базы кластера — вывод rac «infobase info» (issue #3,
/// комментарий 7OH 17.10: диалог правки показывал слишком мало сведений по сравнению с
/// консолью кластера). Команда требует учётных данных администратора ИБ
/// (<c>--infobase-user</c>/<c>--infobase-pwd</c>), если в базе есть пользователи.
/// Пароль пользователя СУБД rac не возвращает.
/// </summary>
public sealed class RacInfobaseInfo
{
    /// <summary>Идентификатор базы в кластере («infobase»).</summary>
    public Guid InfobaseId { get; set; }

    /// <summary>Имя базы («name»).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Описание («descr»).</summary>
    public string Descr { get; set; } = string.Empty;

    /// <summary>Тип СУБД («dbms»).</summary>
    public string Dbms { get; set; } = string.Empty;

    /// <summary>Сервер баз данных («db-server»).</summary>
    public string DbServer { get; set; } = string.Empty;

    /// <summary>Имя базы данных («db-name»).</summary>
    public string DbName { get; set; } = string.Empty;

    /// <summary>Пользователь сервера БД («db-user»).</summary>
    public string DbUser { get; set; } = string.Empty;

    /// <summary>Защищённое соединение / уровень безопасности («security-level», 0/1/2).</summary>
    public int SecurityLevel { get; set; }

    /// <summary>Разрешить выдачу лицензий сервером 1С:Предприятия («license-distribution» = allow).</summary>
    public bool LicenseDistributionAllowed { get; set; }

    /// <summary>Блокировка начала сеансов включена («sessions-deny» = on).</summary>
    public bool SessionsDeny { get; set; }

    /// <summary>Начало блокировки сеансов («denied-from»); null — не задано.</summary>
    public DateTime? DeniedFrom { get; set; }

    /// <summary>Окончание блокировки сеансов («denied-to»); null — не задано.</summary>
    public DateTime? DeniedTo { get; set; }

    /// <summary>Сообщение блокировки («denied-message»).</summary>
    public string DeniedMessage { get; set; } = string.Empty;

    /// <summary>Параметр блокировки («denied-parameter»).</summary>
    public string DeniedParameter { get; set; } = string.Empty;

    /// <summary>Код разрешения («permission-code»).</summary>
    public string PermissionCode { get; set; } = string.Empty;

    /// <summary>Блокировка регламентных заданий включена («scheduled-jobs-deny» = on).</summary>
    public bool ScheduledJobsDeny { get; set; }

    /// <summary>Внешнее управление сеансами («external-session-manager-connection-string»).</summary>
    public string ExternalSessionManagerConnectionString { get; set; } = string.Empty;

    /// <summary>Обязательное использование внешнего управления («external-session-manager-required» = yes).</summary>
    public bool ExternalSessionManagerRequired { get; set; }

    /// <summary>Профиль безопасности («security-profile-name»).</summary>
    public string SecurityProfileName { get; set; } = string.Empty;

    /// <summary>Профиль безопасности безопасного режима («safe-mode-security-profile-name»).</summary>
    public string SafeModeSecurityProfileName { get; set; } = string.Empty;

    /// <summary>Резервировать рабочие процессы («reserve-working-processes» = yes).</summary>
    public bool ReserveWorkingProcesses { get; set; }

    /// <summary>Локаль («locale», если rac её выводит).</summary>
    public string Locale { get; set; } = string.Empty;

    /// <summary>
    /// Разбор даты rac («2026-10-17T10:00:00»; пустая строка и «0001-01-01T00:00:00» —
    /// дата не задана).
    /// </summary>
    public static DateTime? ParseRacDate(string? value)
    {
        var v = (value ?? string.Empty).Trim().Trim('"');
        if (v.Length == 0)
            return null;
        if (!DateTime.TryParse(v, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var date))
            return null;
        return date.Year <= 1 ? null : date;
    }

    /// <summary>Дата в формате параметра rac (<c>yyyy-MM-ddTHH:mm:ss</c>).</summary>
    public static string FormatRacDate(DateTime value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
}
