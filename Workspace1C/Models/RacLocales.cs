namespace Workspace1C.Models;

/// <summary>
/// Локали информационных баз 1С для rac «infobase create --locale» (issue #3,
/// комментарий 7OH от 2026-10-19): двухбуквенные коды ISO 639-1, которые принимает
/// rac. По умолчанию — «ru» (Русский). Полные коды вида «ru_RU», которые rac может
/// вернуть в «infobase summary list», нормализуются до двухбуквенного кода.
/// </summary>
public static class RacLocales
{
    /// <summary>Доступные локали (порядок = порядок в списке выбора).</summary>
    public static readonly string[] Values =
    {
        "ru", "en", "uk", "kk", "de", "fr", "es", "it",
        "pl", "cs", "tr", "vi", "ar", "zh", "ja"
    };

    /// <summary>Локаль по умолчанию — Русский.</summary>
    public const string Default = "ru";

    /// <summary>
    /// Приводит локаль из вывода rac к двухбуквенному коду: «ru_RU» → «ru»,
    /// «ru» → «ru». Пусто/null → <see cref="Default"/>. Незнакомый код
    /// (например «zz») возвращается как есть — диалог добавит его в список.
    /// </summary>
    public static string Normalize(string? locale)
    {
        var value = (locale ?? string.Empty).Trim();
        if (value.Length == 0)
            return Default;
        var dash = value.IndexOfAny(new[] { '_', '-' });
        return dash > 0 ? value[..dash] : value;
    }
}
