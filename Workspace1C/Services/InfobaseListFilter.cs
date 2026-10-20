using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Чистая логика фильтрации/сортировки списков «Избранное»/«Недавние» с кэшированием
/// (issue #21). Кэш пересчитывается ТОЛЬКО при явной инвалидации (<see cref="Invalidate"/>),
/// которую вызывают точки изменения данных (добавление/удаление базы, смена
/// <see cref="Infobase.IsFavorite"/>/<see cref="Infobase.LastLaunchDate"/>, поиск, теги).
/// При переключении вкладки без изменения данных возвращается готовый кэш — фильтрация
/// и сортировка не повторяются.
/// </summary>
/// <remarks>
/// Кэш хранит ссылки на <see cref="Infobase"/> (не копии) — объекты в памяти, как и в
/// дереве; риск рассинхрона минимален, т.к. <see cref="Infobase"/> — INPC-модель, а строки
/// дерева привязаны к свойствам.
/// </remarks>
public sealed class InfobaseListFilter
{
    private readonly Func<IReadOnlyList<Infobase>> _favoritesProvider;
    private readonly Func<IReadOnlyList<Infobase>> _recentProvider;
    private IReadOnlyList<Infobase>? _favoritesCache;
    private IReadOnlyList<Infobase>? _recentCache;

    /// <summary>
    /// Создаёт фильтр с кэшем. Провайдеры возвращают ПОЛНЫЙ отфильтрованный и
    /// отсортированный список для соответствующего режима (с учётом приватности,
    /// поиска, тегов и выбранной сортировки) — результат кэшируется до инвалидации.
    /// </summary>
    /// <param name="favoritesProvider">Провайдер списка «Избранное».</param>
    /// <param name="recentProvider">Провайдер списка «Недавние».</param>
    public InfobaseListFilter(
        Func<IReadOnlyList<Infobase>> favoritesProvider,
        Func<IReadOnlyList<Infobase>> recentProvider)
    {
        _favoritesProvider = favoritesProvider ?? throw new ArgumentNullException(nameof(favoritesProvider));
        _recentProvider = recentProvider ?? throw new ArgumentNullException(nameof(recentProvider));
    }

    /// <summary>Отфильтрованный список «Избранное» (с кэшем).</summary>
    public IReadOnlyList<Infobase> GetFavorites() => _favoritesCache ??= _favoritesProvider();

    /// <summary>Отфильтрованный список «Недавние» (с кэшем).</summary>
    public IReadOnlyList<Infobase> GetRecent() => _recentCache ??= _recentProvider();

    /// <summary>
    /// Сбрасывает кэш. Вызывается при изменении данных: добавление/удаление базы,
    /// смена <see cref="Infobase.IsFavorite"/>/<see cref="Infobase.LastLaunchDate"/>,
    /// поиск, теги, смена сортировки.
    /// </summary>
    public void Invalidate()
    {
        _favoritesCache = null;
        _recentCache = null;
    }

    /// <summary>Чистый фильтр «Избранное»: только базы с <see cref="Infobase.IsFavorite"/>.</summary>
    public static IReadOnlyList<Infobase> FilterFavorites(IEnumerable<Infobase> source) =>
        source.Where(i => i.IsFavorite).ToList();

    /// <summary>
    /// Чистый фильтр «Недавние»: базы с датой последнего запуска, отсортированные
    /// по <see cref="Infobase.LastLaunchDate"/> по убыванию (имя — стабильный второй ключ).
    /// </summary>
    public static IReadOnlyList<Infobase> FilterRecent(IEnumerable<Infobase> source) =>
        source.Where(i => i.LastLaunchDate.HasValue)
              .OrderByDescending(i => i.LastLaunchDate)
              .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
              .ToList();
}