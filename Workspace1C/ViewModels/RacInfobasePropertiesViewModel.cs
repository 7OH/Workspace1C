using System;
using System.Globalization;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Диалог «Свойства информационной базы» монитора «Серверы 1С» (issue #3, комментарии
/// 7OH 17.10): набор полей как в консоли кластера — СУБД, выдача лицензий, блокировка
/// начала сеансов (начало/окончание/сообщение/код разрешения/параметр), блокировка
/// регламентных заданий, внешнее управление сеансами, профили безопасности. Полные
/// свойства читаются rac «infobase info» с учётными данными администратора ИБ: они
/// подставляются из списка баз приложения, если там сохранены, иначе вводятся здесь
/// (кнопка «Обновить» перечитывает свойства). Пока полные свойства не прочитаны,
/// дополнительные параметры в rac «infobase update» не передаются — текущие значения в
/// кластере не затираются. Чистый .NET — общая для окон WPF и Avalonia, покрыта тестами.
/// </summary>
public sealed class RacInfobasePropertiesViewModel : ViewModelBase
{
    /// <summary>Загрузчик полных свойств: (пользователь ИБ, пароль) → свойства или ошибка.</summary>
    public delegate Task<(RacInfobaseInfo? Info, string Error)> InfoLoader(string user, string password);

    private readonly InfoLoader? _loader;

    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _dbms = RacDbms.MssqlServer;
    private string _dbServer = string.Empty;
    private string _dbName = string.Empty;
    private string _dbUser = string.Empty;
    private string _dbPassword = string.Empty;
    private int _securityLevel;
    private bool _licenseDistributionAllowed;
    private bool _sessionsDeny;
    private string _deniedFromText = string.Empty;
    private string _deniedToText = string.Empty;
    private string _deniedMessage = string.Empty;
    private string _permissionCode = string.Empty;
    private string _deniedParameter = string.Empty;
    private bool _scheduledJobsDeny;
    private string _externalSessionManagerConnectionString = string.Empty;
    private bool _externalSessionManagerRequired;
    private string _securityProfileName = string.Empty;
    private string _safeModeSecurityProfileName = string.Empty;
    private string _infobaseUser = string.Empty;
    private string _infobasePassword = string.Empty;
    private string _statusText = string.Empty;
    private bool _statusIsError;
    private bool _isLoading;
    private bool _isFullyLoaded;

    /// <param name="summary">Сведения из «infobase summary list» (предзаполнение до загрузки).</param>
    /// <param name="loader">Загрузчик «infobase info»; null — только сводные сведения.</param>
    /// <param name="savedCredentials">Учётные данные администратора ИБ из списка баз приложения.</param>
    public RacInfobasePropertiesViewModel(
        RacInfobaseSummary summary, InfoLoader? loader,
        ClusterInfobaseCredentials.Found? savedCredentials = null)
    {
        _loader = loader;
        if (summary is not null)
        {
            _name = summary.Name ?? string.Empty;
            _description = summary.Descr ?? string.Empty;
            _dbms = NormalizeDbms(summary.Dbms);
            _dbServer = summary.DbServer ?? string.Empty;
            _dbName = summary.DbName ?? string.Empty;
            _dbUser = summary.DbUser ?? string.Empty;
            _securityLevel = summary.SecurityLevel;
        }

        if (savedCredentials is not null)
        {
            _infobaseUser = savedCredentials.User;
            _infobasePassword = savedCredentials.Password;
            CredentialsSourceText = string.Format(
                LocalizationManager.T("ServerMonitor.Infobase.Props.CredentialsFromList"),
                savedCredentials.SourceName);
        }
        else
        {
            CredentialsSourceText = LocalizationManager.T("ServerMonitor.Infobase.Props.CredentialsNotSaved");
        }
    }

    /// <summary>Типы СУБД для выбора. Файловая база кластера — тоже допустимое
    /// значение (issue #3, 1.0.0.14): правка файловой базы не должна подменять
    /// тип на MSSQLServer.</summary>
    public static string[] DbmsOptions { get; } =
    {
        RacDbms.MssqlServer, RacDbms.PostgreSql, RacDbms.IbmDb2, RacDbms.OracleDatabase,
        RacDbms.Sqlite, RacDbms.ServerOdbc, RacDbms.File,
    };

    public string Name { get => _name; private set => SetProperty(ref _name, value); }
    public string Description { get => _description; set => SetProperty(ref _description, value ?? string.Empty); }
    public string Dbms { get => _dbms; set => SetProperty(ref _dbms, value ?? RacDbms.MssqlServer); }
    public string DbServer { get => _dbServer; set => SetProperty(ref _dbServer, value ?? string.Empty); }
    public string DbName { get => _dbName; set => SetProperty(ref _dbName, value ?? string.Empty); }
    public string DbUser { get => _dbUser; set => SetProperty(ref _dbUser, value ?? string.Empty); }

    /// <summary>Пароль пользователя СУБД: rac его не возвращает; пусто — не меняется.</summary>
    public string DbPassword { get => _dbPassword; set => SetProperty(ref _dbPassword, value ?? string.Empty); }

    /// <summary>«Защищённое соединение» (уровень безопасности) — только чтение: rac update его не меняет.</summary>
    public string SecureConnectionText => _securityLevel switch
    {
        1 => LocalizationManager.T("ServerMonitor.Infobase.Props.Secure.Connection"),
        2 => LocalizationManager.T("ServerMonitor.Infobase.Props.Secure.Always"),
        _ => LocalizationManager.T("ServerMonitor.Infobase.Props.Secure.Off"),
    };

    public bool LicenseDistributionAllowed { get => _licenseDistributionAllowed; set => SetProperty(ref _licenseDistributionAllowed, value); }
    public bool SessionsDeny { get => _sessionsDeny; set => SetProperty(ref _sessionsDeny, value); }

    /// <summary>Начало блокировки сеансов: «дд.мм.гггг чч:мм[:сс]», пусто — не задано.</summary>
    public string DeniedFromText { get => _deniedFromText; set => SetProperty(ref _deniedFromText, value ?? string.Empty); }

    /// <summary>Окончание блокировки сеансов: «дд.мм.гггг чч:мм[:сс]», пусто — не задано.</summary>
    public string DeniedToText { get => _deniedToText; set => SetProperty(ref _deniedToText, value ?? string.Empty); }

    public string DeniedMessage { get => _deniedMessage; set => SetProperty(ref _deniedMessage, value ?? string.Empty); }
    public string PermissionCode { get => _permissionCode; set => SetProperty(ref _permissionCode, value ?? string.Empty); }
    public string DeniedParameter { get => _deniedParameter; set => SetProperty(ref _deniedParameter, value ?? string.Empty); }
    public bool ScheduledJobsDeny { get => _scheduledJobsDeny; set => SetProperty(ref _scheduledJobsDeny, value); }

    public string ExternalSessionManagerConnectionString
    {
        get => _externalSessionManagerConnectionString;
        set => SetProperty(ref _externalSessionManagerConnectionString, value ?? string.Empty);
    }

    public bool ExternalSessionManagerRequired
    {
        get => _externalSessionManagerRequired;
        set => SetProperty(ref _externalSessionManagerRequired, value);
    }

    public string SecurityProfileName { get => _securityProfileName; set => SetProperty(ref _securityProfileName, value ?? string.Empty); }

    public string SafeModeSecurityProfileName
    {
        get => _safeModeSecurityProfileName;
        set => SetProperty(ref _safeModeSecurityProfileName, value ?? string.Empty);
    }

    /// <summary>Администратор информационной базы для rac (--infobase-user).</summary>
    public string InfobaseUser { get => _infobaseUser; set => SetProperty(ref _infobaseUser, value ?? string.Empty); }

    /// <summary>Пароль администратора ИБ (--infobase-pwd); в журнал не попадает.</summary>
    public string InfobasePassword { get => _infobasePassword; set => SetProperty(ref _infobasePassword, value ?? string.Empty); }

    /// <summary>Откуда взяты учётные данные администратора ИБ (подсказка под полями).</summary>
    public string CredentialsSourceText { get; private set; }

    /// <summary>Состояние загрузки свойств / текст ошибки rac.</summary>
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    /// <summary>Строка состояния — ошибка (красным).</summary>
    public bool StatusIsError { get => _statusIsError; private set => SetProperty(ref _statusIsError, value); }

    /// <summary>Идёт чтение «infobase info».</summary>
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

    /// <summary>
    /// Полные свойства прочитаны: только тогда дополнительные параметры (блокировка сеансов,
    /// лицензии, внешнее управление, профили) передаются в rac «infobase update».
    /// </summary>
    public bool IsFullyLoaded { get => _isFullyLoaded; private set => SetProperty(ref _isFullyLoaded, value); }

    /// <summary>
    /// «Обновить»: читает полные свойства с текущими учётными данными администратора ИБ.
    /// При ошибке (например, нужен логин/пароль) поля остаются прежними, а в строке
    /// состояния — текст ошибки и подсказка указать администратора ИБ.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (_loader is null || IsLoading)
            return;

        IsLoading = true;
        StatusIsError = false;
        StatusText = LocalizationManager.T("ServerMonitor.Infobase.Props.Loading");
        try
        {
            var (info, error) = await _loader(InfobaseUser.Trim(), InfobasePassword);
            if (info is not null)
            {
                Apply(info);
                IsFullyLoaded = true;
                StatusIsError = false;
                StatusText = LocalizationManager.T("ServerMonitor.Infobase.Props.Loaded");
            }
            else
            {
                StatusIsError = true;
                StatusText = FailedText(string.IsNullOrWhiteSpace(error) ? "—" : error.Trim());
            }
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusText = FailedText(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Текст ошибки загрузки: шаблон локализации с текстом rac (текст rac — всегда).</summary>
    private static string FailedText(string error)
    {
        var template = LocalizationManager.T("ServerMonitor.Infobase.Props.LoadFailedFormat");
        return template.Contains("{0}", StringComparison.Ordinal)
            ? string.Format(template, error)
            : template + " " + error;
    }

    /// <summary>Заполняет поля из вывода «infobase info».</summary>
    public void Apply(RacInfobaseInfo info)
    {
        if (info is null)
            return;
        if (!string.IsNullOrWhiteSpace(info.Name))
            Name = info.Name;
        Description = info.Descr;
        if (!string.IsNullOrWhiteSpace(info.Dbms))
            Dbms = NormalizeDbms(info.Dbms);
        DbServer = info.DbServer;
        DbName = info.DbName;
        DbUser = info.DbUser;
        _securityLevel = info.SecurityLevel;
        OnPropertyChanged(nameof(SecureConnectionText));
        LicenseDistributionAllowed = info.LicenseDistributionAllowed;
        SessionsDeny = info.SessionsDeny;
        DeniedFromText = FormatDateText(info.DeniedFrom);
        DeniedToText = FormatDateText(info.DeniedTo);
        DeniedMessage = info.DeniedMessage;
        PermissionCode = info.PermissionCode;
        DeniedParameter = info.DeniedParameter;
        ScheduledJobsDeny = info.ScheduledJobsDeny;
        ExternalSessionManagerConnectionString = info.ExternalSessionManagerConnectionString;
        ExternalSessionManagerRequired = info.ExternalSessionManagerRequired;
        SecurityProfileName = info.SecurityProfileName;
        SafeModeSecurityProfileName = info.SafeModeSecurityProfileName;
    }

    /// <summary>
    /// Запрос rac «infobase update» из полей диалога. Ошибка проверки (пустые сервер/имя
    /// БД, неверная дата) — в <paramref name="error"/>, результат null.
    /// </summary>
    public RacInfobaseUpdateRequest? BuildRequest(out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(DbServer))
        {
            error = LocalizationManager.T("ServerMonitor.Infobase.DbServerRequired");
            return null;
        }
        if (string.IsNullOrWhiteSpace(DbName))
        {
            error = LocalizationManager.T("ServerMonitor.Infobase.DbNameRequired");
            return null;
        }
        if (!TryParseDateText(DeniedFromText, out var deniedFrom) ||
            !TryParseDateText(DeniedToText, out var deniedTo))
        {
            error = LocalizationManager.T("ServerMonitor.Infobase.Props.BadDate");
            return null;
        }

        var request = new RacInfobaseUpdateRequest
        {
            Name = Name,
            Description = Description.Trim(),
            Dbms = Dbms,
            DbServer = DbServer.Trim(),
            DbName = DbName.Trim(),
            DbUser = DbUser.Trim(),
            DbPassword = DbPassword,
            ScheduledJobsDeny = ScheduledJobsDeny,
            InfobaseUser = InfobaseUser.Trim(),
            InfobasePassword = InfobasePassword,
        };

        if (IsFullyLoaded)
        {
            request.SessionsDeny = SessionsDeny;
            request.DeniedFrom = deniedFrom;
            request.DeniedTo = deniedTo;
            request.DeniedMessage = DeniedMessage;
            request.PermissionCode = PermissionCode;
            request.DeniedParameter = DeniedParameter;
            request.LicenseDistributionAllowed = LicenseDistributionAllowed;
            request.ExternalSessionManagerConnectionString = ExternalSessionManagerConnectionString.Trim();
            request.ExternalSessionManagerRequired = ExternalSessionManagerRequired;
            request.SecurityProfileName = SecurityProfileName.Trim();
            request.SafeModeSecurityProfileName = SafeModeSecurityProfileName.Trim();
        }
        return request;
    }

    /// <summary>Текст даты для поля: «дд.мм.гггг чч:мм:сс» либо пусто.</summary>
    public static string FormatDateText(DateTime? value) =>
        value is { } v ? v.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>
    /// Разбор даты из поля: пусто — не задано (успех, null); «дд.мм.гггг», «дд.мм.гггг чч:мм»,
    /// «дд.мм.гггг чч:мм:сс» или «гггг-мм-ддTчч:мм:сс». Иначе — ошибка.
    /// </summary>
    public static bool TryParseDateText(string? text, out DateTime? value)
    {
        value = null;
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0)
            return true;
        var formats = new[]
        {
            "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy H:mm:ss", "dd.MM.yyyy HH:mm", "dd.MM.yyyy H:mm",
            "dd.MM.yyyy", "d.M.yyyy HH:mm:ss", "d.M.yyyy HH:mm", "d.M.yyyy",
            "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd",
        };
        if (DateTime.TryParseExact(t, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            value = parsed;
            return true;
        }
        return false;
    }

    /// <summary>Приводит тип СУБД из вывода rac к значению списка (регистронезависимо).
    /// Пустое значение — файловая база кластера (issue #3, 1.0.0.14): раньше пустота
    /// подменялась на MSSQLServer, и правка файловой базы портила её тип.</summary>
    public static string NormalizeDbms(string? dbms)
    {
        var value = (dbms ?? string.Empty).Trim();
        foreach (var option in DbmsOptions)
        {
            if (string.Equals(option, value, StringComparison.OrdinalIgnoreCase))
                return option;
        }
        return value.Length == 0 ? RacDbms.File : value;
    }
}
