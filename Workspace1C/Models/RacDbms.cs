namespace Workspace1C.Models;

/// <summary>
/// Значения параметра rac «--dbms» команды «infobase create» (issue #3, вкладка
/// «Базы» монитора серверов 1С). FILE — файловая информационная база (в этом случае
/// «--db-server» = путь к каталогу, «--db-name» = имя файла, обычно «1Cv8.1CD»);
/// остальные — клиент-серверные СУБД.
/// </summary>
public static class RacDbms
{
    /// <summary>Файловая информационная база.</summary>
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