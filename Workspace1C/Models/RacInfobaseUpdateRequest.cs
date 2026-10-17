namespace Workspace1C.Models;

/// <summary>
/// Параметры изменения информационной базы в кластере серверов 1С (issue #3, кнопка
/// «Изменить» монитора серверов): поля диалога → команда rac «infobase update
/// --cluster=... --infobase=... --name=... --desc=... --dbms=... --db-server=...
/// --db-name=... --db-user=... --db-pwd=... --locale=... --security-level=...
/// --scheduled-jobs-deny=...». В отличие от создания (<see cref="RacInfobaseCreateRequest"/>),
/// параметр «создать базу на сервере СУБД» не применяется — база уже существует.
/// Для ФАЙЛОВОЙ базы <see cref="Dbms"/> = «FILE», <see cref="DbServer"/> — путь к каталогу,
/// <see cref="DbName"/> — имя файла (обычно «1Cv8.1CD»).
/// </summary>
public sealed class RacInfobaseUpdateRequest
{
    /// <summary>Имя информационной базы в кластере (параметр rac «--name»).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Описание информационной базы (параметр rac «--desc», необязательный).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Признак файловой информационной базы (<see cref="Dbms"/> = «FILE»).</summary>
    public bool IsFile { get; set; }

    /// <summary>
    /// Тип СУБД (параметр rac «--dbms»): MSSQLServer, PostgreSQL, IBMDB2, OracleDatabase,
    /// SQLite, ServerODBC или FILE (файловая база).
    /// </summary>
    public string Dbms { get; set; } = "MSSQLServer";

    /// <summary>
    /// Сервер СУБД (параметр rac «--db-server»); для файловой базы — путь к каталогу.
    /// </summary>
    public string DbServer { get; set; } = string.Empty;

    /// <summary>
    /// Имя базы данных на сервере СУБД (параметр rac «--db-name»); для файловой базы —
    /// имя файла (обычно «1Cv8.1CD»).
    /// </summary>
    public string DbName { get; set; } = string.Empty;

    /// <summary>Пользователь СУБД (параметр rac «--db-user»; для файловой базы не нужен).</summary>
    public string DbUser { get; set; } = string.Empty;

    /// <summary>Пароль пользователя СУБД (параметр rac «--db-pwd»; маскируется в журнале).</summary>
    public string DbPassword { get; set; } = string.Empty;

    /// <summary>Локаль информационной базы (параметр rac «--locale»; пусто — по умолчанию сервера).</summary>
    public string Locale { get; set; } = string.Empty;

    /// <summary>Уровень безопасности (параметр rac «--security-level», 0/1/2); null — по умолчанию.</summary>
    public int? SecurityLevel { get; set; }

    /// <summary>Запретить выполнение регламентных заданий (параметр rac «--scheduled-jobs-deny»).</summary>
    public bool ScheduledJobsDeny { get; set; }
}