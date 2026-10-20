namespace Workspace1C.Models;

/// <summary>
/// Запись справочника «Сервера управления» (issue #3): сервер 1С для встроенного
/// монитора (rac). Хранится в отдельном читаемом JSON-файле <c>server_list.json</c>
/// (см. Services.ServerListStore). В списке окна монитора выводится только
/// <see cref="Description"/>; при активации строки по адресу/порту/логину/паролю
/// выполняется подключение к кластеру.
/// </summary>
public class ManagementServer
{
    /// <summary>Идентификатор записи (GUID, по образцу ItsAccount/ScriptScenario).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Описание сервера — единственное поле, выводимое в списке справочника.</summary>
    public string Description { get; set; } = "";

    /// <summary>Адрес сервера 1С (host или IP; допускается «host:port» — порт из адреса приоритетнее).</summary>
    public string Address { get; set; } = "localhost";

    /// <summary>Порт по умолчанию для новой записи справочника (issue #3, комментарий 7OH 20.10):
    /// 1545 — порт сервера администрирования RAS (порт агента ragent — 1540).</summary>
    public const int DefaultPort = 1545;

    /// <summary>Порт сервера 1С; по умолчанию 1545 (RAS); для ragent — 1540.</summary>
    public int Port { get; set; } = DefaultPort;

    /// <summary>Логин администратора кластера (пустая строка — без аутентификации).</summary>
    public string User { get; set; } = "";

    /// <summary>Пароль администратора кластера (хранится открыто в файле справочника,
    /// маскируется при журналировании — см. <see cref="Services.SensitiveDataMasker"/>).</summary>
    public string Password { get; set; } = "";

    /// <summary>Отображаемое имя (для списков и подписей): описание, иначе адрес.</summary>
    public override string ToString() =>
        string.IsNullOrWhiteSpace(Description) ? Address : Description;
}
