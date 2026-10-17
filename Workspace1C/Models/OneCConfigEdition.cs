using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Workspace1C.Models;

/// <summary>
/// Редакция / каталог релизов типовой конфигурации 1С. Описывает сегменты web-адреса
/// обновлений по правилу 1С <c>Configs/<Конфигурация>/<Ред>/<Подред>/</c>:
/// имя редакции для отображения и значения сегментов <c>Red</c>/<c>SubRed</c>,
/// которые подставляются в URL. При необходимости можно переопределить полный адрес
/// целиком (ручная ссылка, необязательно).
/// </summary>
public class OneCConfigEdition
{
    /// <summary>Отображаемое имя редакции (например «Релиз 3.0»).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Сегмент URL «Ред» (например «3.0»). Может быть пустым — тогда сегмент опускается.</summary>
    public string Red { get; set; } = string.Empty;

    /// <summary>Сегмент URL «Подред» (например «142»). Может быть пустым — тогда сегмент опускается.</summary>
    public string SubRed { get; set; } = string.Empty;

    /// <summary>
    /// Полностью переопределённая ссылка на каталог релизов. Если задана — используется
    /// вместо автоматического формирования из сегментов (ручная корректировка).
    /// </summary>
    public string UrlOverride { get; set; } = string.Empty;

    /// <summary>
    /// Основная редакция конфигурации (issue #14): используется, когда редакцию не удалось
    /// подобрать по версии базы (окно F9, планировщик, связывание базы). В конфигурации
    /// основной может быть не более одной редакции; если флаг не стоит ни у одной —
    /// основной считается первая. В JSON пишется только значение <c>true</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsDefault { get; set; }

    /// <summary>Отметка основной редакции для таблицы редакций («★» либо пусто).</summary>
    [JsonIgnore]
    public string DefaultMark => IsDefault ? "★" : string.Empty;

    /// <summary>Возвращает true, если задана ручная ссылка каталога релизов.</summary>
    [JsonIgnore]
    public bool HasUrlOverride => !string.IsNullOrWhiteSpace(UrlOverride);

    /// <summary>Отображаемое имя редакции (для ComboBox и списков).</summary>
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Red : Name;
}