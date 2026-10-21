using System.Collections.Generic;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Применение результата редактирования свойств информационной базы к существующему
/// объекту (единый источник для WPF и Avalonia, issue #35). Диалог свойств возвращает
/// свежий экземпляр <see cref="Infobase"/>, из которого переносятся только
/// редактируемые поля — так сохраняются история запусков, порядок сортировки,
/// номер горячей клавиши избранного и служебные значения (<see cref="Infobase.IsRunning"/>).
/// После переноса поднимается <see cref="Infobase.NotifyAllPropertiesChanged"/>: поля
/// переносятся прямым присваиванием без уведомлений, а при активном отборе контейнеры
/// строк не пересоздаются (плоский узел переиспользуется), поэтому без явного
/// уведомления привязки не перечитывали обновлённые значения.
/// </summary>
public static class InfobaseEditor
{
    /// <summary>
    /// Применяет редактируемые поля из <paramref name="result"/> к <paramref name="target"/>,
    /// сохраняя ссылку на существующий объект, и уведомляет привязки обо всех изменениях.
    /// Нередактируемые поля (история запусков <see cref="Infobase.LaunchHistory"/>,
    /// <see cref="Infobase.IsRunning"/>, <see cref="Infobase.SortOrder"/> и др.) не затрагиваются.
    /// </summary>
    /// <param name="target">Существующий объект базы (на него ссылаются список и представление).</param>
    /// <param name="result">Результат диалога свойств с обновлёнными значениями.</param>
    public static void ApplyEdits(Infobase target, Infobase result)
    {
        target.Id = result.Id;
        target.Name = result.Name;
        target.Group = result.Group;
        target.Description = result.Description;
        target.PlatformVersion = result.PlatformVersion;
        // Поля конфигурации переносим явно, иначе введённые вручную имя/версия
        // конфигурации не сохранялись бы (issue #164).
        target.ConfigurationName = result.ConfigurationName;
        target.ConfigurationVersion = result.ConfigurationVersion;
        target.Architecture = result.Architecture;
        target.LaunchMode = result.LaunchMode;
        target.LaunchParameters = result.LaunchParameters;
        target.DefaultLaunchMode = result.DefaultLaunchMode;
        // Внешняя обработка при запуске и действие по двойному щелчку (Этап 7).
        target.DoubleClickAction = result.DoubleClickAction;
        target.ExternalProcessingPath = result.ExternalProcessingPath;
        target.ExternalProcessingData = result.ExternalProcessingData;
        target.ClientType = result.ClientType;
        target.IsFavorite = result.IsFavorite;
        target.IsPinned = result.IsPinned;
        target.LastLaunchDate = result.LastLaunchDate;
        target.Tags = result.Tags;
        target.MetadataRoot = result.MetadataRoot;
        // Ручной размер базы переносим явно, иначе введённый вручную размер терялся
        // при сохранении (поле редактируется в окне подключения, issue #243).
        target.ManualSizeBytes = result.ManualSizeBytes;
        target.Connection = result.Connection;
        target.EnterpriseAuth = result.EnterpriseAuth;
        target.ConfiguratorAuth = result.ConfiguratorAuth;
        target.Repository = result.Repository;
        // Произвольные поля базы (issue #6): переносим явно с копированием списка,
        // иначе отредактированные строки таблицы «Произвольные поля» терялись при
        // сохранении и %Additional_N% оставался бы без подстановки в сценариях.
        target.AdditionalFields = new List<string>(result.AdditionalFields ?? new List<string>());

        // Поля переносятся прямым присваиванием (auto-свойства) без уведомлений на каждое
        // из них; при активном отборе строки не пересоздаются — поднимаем «все свойства»
        // один раз, чтобы привязки перечитали значения (issue #35).
        target.NotifyAllPropertiesChanged();
    }
}