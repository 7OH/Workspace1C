namespace Workspace1C.Models;

/// <summary>
/// Значения параметра rac «--dbms» команды «infobase create» (issue #3, вкладка
/// «Базы» монитора серверов 1С). В кластере бывают только клиент-серверные базы
/// (комментарий 7OH от 2026-10-18 20:24), поэтому диалоги предлагают ровно четыре
/// значения rac «--dbms»: MSSQLServer, PostgreSQL, IBMDB2, OracleDatabase. Константы
/// FILE/SQLite/ServerODBC оставлены для разбора вывода rac и тестов.
/// </summary>
public static class RacDbms
{
    /// <summary>Файловая информационная база (в кластере не бывает).</summary>
    public const string File = "FILE";

    /// <summary>Microsoft SQL Server.</summary>
    public const string MssqlServer = "MSSQLServer";

    /// <summary>PostgreSQL.</summary>
    public const string PostgreSql = "PostgreSQL";

    /// <summary>IBM DB2.</summary>
    public const string IbmDb2 = "IBMDB2";

    /// <summary>Oracle Database.</summary>
    public const string OracleDatabase = "OracleDatabase";

    /// <summary>SQLite.</summary>
    public const string Sqlite = "SQLite";

    /// <summary>ServerODBC (универсальный ODBC-доступ).</summary>
    public const string ServerOdbc = "ServerODBC";
}