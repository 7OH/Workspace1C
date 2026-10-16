using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.ViewModels;

/// <summary>
/// Строка вкладки «Базы» окна «Серверы 1С» (issue #3): отображение
/// <see cref="RacInfobaseSummary"/> (команда rac «infobase summary list») —
/// имя, описание, тип СУБД, сервер СУБД, имя БД, пользователь СУБД, локаль,
/// уровень безопасности и признак лицензирования. Чистый .NET — без
/// платформенных зависимостей.
/// </summary>
public sealed class RacInfobaseRow
{
    private readonly RacInfobaseSummary _info;

    public RacInfobaseRow(RacInfobaseSummary info)
    {
        _info = info ?? throw new System.ArgumentNullException(nameof(info));
    }

    /// <summary>Идентификатор информационной базы в кластере.</summary>
    public System.Guid InfobaseId => _info.InfobaseId;

    /// <summary>Имя информационной базы (значение Ref строки подключения).</summary>
    public string Name => _info.Name;

    /// <summary>Описание информационной базы («—», если пусто).</summary>
    public string Descr => string.IsNullOrWhiteSpace(_info.Descr)
        ? "—"
        : _info.Descr;

    /// <summary>Человекочитаемый тип СУБД («Файловая» для FILE, иначе имя СУБД).</summary>
    public string DbmsText => LocalizeDbms(_info.Dbms);

    /// <summary>Сервер СУБД (или путь каталога файловой базы).</summary>
    public string DbServer => _info.DbServer;

    /// <summary>Имя базы данных на сервере СУБД.</summary>
    public string DbName => _info.DbName;

    /// <summary>Пользователь СУБД.</summary>
    public string DbUser => string.IsNullOrWhiteSpace(_info.DbUser)
        ? "—"
        : _info.DbUser;

    /// <summary>Локаль информационной базы.</summary>
    public string Locale => string.IsNullOrWhiteSpace(_info.Locale)
        ? "—"
        : _info.Locale;

    /// <summary>Уровень безопасности ИБ (0/1/2).</summary>
    public int SecurityLevel => _info.SecurityLevel;

    /// <summary>«Да»/«Нет» для колонки «Лицензирована».</summary>
    public string LicensedText => _info.Licensed
        ? LocalizationManager.T("Common.Yes")
        : LocalizationManager.T("Common.No");

    /// <summary>Сырое значение типа СУБД rac (для передачи/сортировки по типу).</summary>
    public string Dbms => _info.Dbms;

    private static string LocalizeDbms(string dbms) => dbms.Trim().ToLowerInvariant() switch
    {
        "file" => LocalizationManager.T("ServerMonitor.Infobase.Dbms.File"),
        "mssqlserver" => LocalizationManager.T("ServerMonitor.Infobase.Dbms.Mssql"),
        "postgresql" => LocalizationManager.T("ServerMonitor.Infobase.Dbms.Postgre"),
        "ibmdb2" => LocalizationManager.T("ServerMonitor.Infobase.Dbms.Db2"),
        "oracledatabase" => LocalizationManager.T("ServerMonitor.Infobase.Dbms.Oracle"),
        "sqlite" => LocalizationManager.T("ServerMonitor.Infobase.Dbms.Sqlite"),
        "serverodbc" => LocalizationManager.T("ServerMonitor.Infobase.Dbms.Odbc"),
        _ => string.IsNullOrWhiteSpace(dbms) ? "—" : dbms
    };
}