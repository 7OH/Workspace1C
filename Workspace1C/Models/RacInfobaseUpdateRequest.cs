using System;

namespace Workspace1C.Models;

/// <summary>
/// Параметры изменения информационной базы в кластере серверов 1С (issue #3, кнопка
/// «Изменить» монитора серверов): поля диалога → команда rac «infobase update
/// --cluster=... --infobase=... --name=... --desc=... --dbms=... --db-server=...
/// --db-name=... --db-user=... --db-pwd=... --locale=... --security-level=...
/// --scheduled-jobs-deny=...». В отличие от создания (<see cref="RacInfobaseCreateRequest"/>),
/// параметр «создать базу на сервере СУБД» не применяется — база уже существует.
/// В кластере бывают только клиент-серверные базы (комментарий 7OH от 2026-10-18 20:24),
/// поэтому <see cref="Dbms"/> — одно из значений rac «--dbms»: MSSQLServer, PostgreSQL,
/// IBMDB2, OracleDatabase.
/// </summary>
public sealed class RacInfobaseUpdateRequest
{
    /// <summary>Имя информационной базы в кластере (параметр rac «--name»).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Описание информационной базы (параметр rac «--desc», необязательный).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Тип СУБД (параметр rac «--dbms»): MSSQLServer, PostgreSQL, IBMDB2, OracleDatabase.
    /// </summary>
    public string Dbms { get; set; } = "MSSQLServer";

    /// <summary>Сервер СУБД (параметр rac «--db-server»).</summary>
    public string DbServer { get; set; } = string.Empty;

    /// <summary>Имя базы данных на сервере СУБД (параметр rac «--db-name»).</summary>
    public string DbName { get; set; } = string.Empty;

    /// <summary>Пользователь СУБД (параметр rac «--db-user»; для файловой базы не нужен).</summary>
    public string DbUser { get; set; } = string.Empty;

    /// <summary>Пароль пользователя СУБД (параметр rac «--db-pwd»; маскируется в журнале).</summary>
    public string DbPassword { get; set; } = string.Empty;

    /// <summary>Локаль информационной базы (параметр rac «--locale»; пусто — по умолчанию сервера).</summary>
    public string Locale { get; set; } = string.Empty;

    /// <summary>Уровень безопасности (параметр rac «--security-level», 0/1/2); null — по умолчанию.</summary>
    public int? SecurityLevel { get; set; }

    /// <summary>Запретить выполнение регламентных заданий (параметр rac «--scheduled-jobs-deny=on|off»).</summary>
    public bool ScheduledJobsDeny { get; set; }

    // ---- Учётные данные администратора ИБ (issue #3, 7OH 17.10) ----

    /// <summary>Администратор информационной базы (параметр rac «--infobase-user»); пусто — без него.</summary>
    public string InfobaseUser { get; set; } = string.Empty;

    /// <summary>Пароль администратора ИБ (параметр rac «--infobase-pwd»; маскируется в журнале).</summary>
    public string InfobasePassword { get; set; } = string.Empty;

    // ---- Полные свойства (issue #3): заполняются, только когда известны из «infobase info».
    // null — параметр не передаётся (текущее значение в кластере не меняется). ----

    /// <summary>Блокировка начала сеансов («--sessions-deny=on|off»).</summary>
    public bool? SessionsDeny { get; set; }

    /// <summary>Начало блокировки сеансов («--denied-from»).</summary>
    public DateTime? DeniedFrom { get; set; }

    /// <summary>Окончание блокировки сеансов («--denied-to»).</summary>
    public DateTime? DeniedTo { get; set; }

    /// <summary>Сообщение блокировки («--denied-message»).</summary>
    public string? DeniedMessage { get; set; }

    /// <summary>Параметр блокировки («--denied-parameter»).</summary>
    public string? DeniedParameter { get; set; }

    /// <summary>Код разрешения («--permission-code»).</summary>
    public string? PermissionCode { get; set; }

    /// <summary>Разрешить выдачу лицензий сервером 1С («--license-distribution=allow|deny»).</summary>
    public bool? LicenseDistributionAllowed { get; set; }

    /// <summary>Внешнее управление сеансами («--external-session-manager-connection-string»).</summary>
    public string? ExternalSessionManagerConnectionString { get; set; }

    /// <summary>Обязательное использование внешнего управления («--external-session-manager-required=yes|no»).</summary>
    public bool? ExternalSessionManagerRequired { get; set; }

    /// <summary>Профиль безопасности («--security-profile-name»).</summary>
    public string? SecurityProfileName { get; set; }

    /// <summary>Профиль безопасности безопасного режима («--safe-mode-security-profile-name»).</summary>
    public string? SafeModeSecurityProfileName { get; set; }
}