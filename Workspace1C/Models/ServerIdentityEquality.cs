namespace Workspace1C.Models;

/// <summary>
/// Чистое правило равенства записей справочника «Сервера управления» по
/// <see cref="ManagementServer.Id"/> (issue #3, комментарий 7OH 19.10): строка
/// подключённого сервера подсвечивается жирным, если её Id совпадает с Id записи
/// <c>ConnectedServer</c>. Общий источник правила для WPF (MultiBinding в DataTrigger,
/// <c>Converters.ServerIdentityEqualityConverter</c>), Avalonia (Converters/Avalonia)
/// и юнит-тестов — без платформенных зависимостей.
/// </summary>
public static class ServerIdentityEquality
{
    /// <summary>
    /// Совпадают ли записи по идентификатору. Любой null — «не совпадает» (без
    /// активного подключения к справочнику ничего не должно подсвечиваться);
    /// пустой идентификатор записи тоже не считается совпадением.
    /// </summary>
    public static bool EqualsById(ManagementServer? connected, ManagementServer? candidate) =>
        connected is not null && candidate is not null &&
        !string.IsNullOrEmpty(connected.Id) &&
        string.Equals(connected.Id, candidate.Id, StringComparison.Ordinal);
}