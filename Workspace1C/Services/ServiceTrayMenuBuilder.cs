using System;
using System.Collections.Generic;
using System.Linq;
using Workspace1C.Localization;

namespace Workspace1C.Services;

/// <summary>
/// Вид пункта меню служб (issue #11, итерация 2 — группы служб, релиз 1.0.0.12;
/// комментарий 7OH к issue #11 от 13:30/13:47). Плоский список верхнего уровня
/// подменю «Службы»: «Настроить…», затем группы/службы в порядке списка
/// (<see cref="ServiceStoreData.Order"/>), затем разделитель и подменю
/// «Все сервисы». Иерархия подменю описывается свойством
/// <see cref="ServiceMenuItem.Children"/> (команды службы, команды группы +
/// члены группы, команды «Все сервисы»).
/// </summary>
public enum ServiceMenuItemKind
{
    /// <summary>Пункт «Настроить…» — открывает окно «Управление службами».</summary>
    Settings,

    /// <summary>Подменю отдельной службы; в <see cref="ServiceMenuItem.Children"/> — команды службы.</summary>
    ServiceSubmenu,

    /// <summary>
    /// Подменю группы; в <see cref="ServiceMenuItem.Children"/> — сначала команды
    /// действий над всеми членами (<see cref="ServiceMenuItemKind.GroupCommand"/>),
    /// затем разделитель, затем каждый член группы как подменю
    /// (<see cref="ServiceMenuItemKind.ServiceSubmenu"/>).
    /// </summary>
    GroupSubmenu,

    /// <summary>
    /// Подменю «Все сервисы»; в <see cref="ServiceMenuItem.Children"/> — команды
    /// по всем службам списка (<see cref="ServiceMenuItemKind.AllServicesCommand"/>).
    /// </summary>
    AllServicesSubmenu,

    /// <summary>Команда над конкретной службой (Запустить/Остановить/…).</summary>
    ServiceCommand,

    /// <summary>Команда над всеми членами группы (в порядке списка группы).</summary>
    GroupCommand,

    /// <summary>Команда над всеми службами списка (подменю «Все сервисы»).</summary>
    AllServicesCommand,

    /// <summary>Разделитель (в верхнем списке — перед «Все сервисы»; внутри группы — перед членами).</summary>
    Separator
}

/// <summary>
/// Объект действия пункта-команды меню служб (issue #11): на кого направлена
/// операция <see cref="ServiceMenuItem.Action"/>. Используется адаптерами меню
/// (WinForms/Avalonia) для выполнения операции.
/// </summary>
public enum ServiceMenuItemTarget
{
    /// <summary>Не применимо (пункты-подменю, «Настроить…», разделители).</summary>
    None,

    /// <summary>Отдельная служба (<see cref="ServiceMenuItem.TargetName"/>).</summary>
    Service,

    /// <summary>Группа служб (все члены; список — <see cref="ServiceMenuItem.ServiceNames"/>).</summary>
    Group,

    /// <summary>Все службы списка (<see cref="ServiceMenuItem.ServiceNames"/>).</summary>
    All
}

/// <summary>
/// Пункт меню служб (issue #11, итерация 2 — группы служб): плоское описание,
/// пригодное для построения системного меню трея на обеих платформах (WinForms
/// и Avalonia). Никаких платформенных типов — только строки, enum и bool:
/// готовый текст <see cref="Text"/>, доступность <see cref="IsEnabled"/>,
/// цвет кружка статуса <see cref="StatusColorHex"/> и локализованный текст
/// статуса <see cref="StatusText"/> (для группы — «Все запущены»/«Частично»/…).
/// </summary>
public sealed class ServiceMenuItem
{
    /// <summary>Вид пункта (подменю, команда, разделитель).</summary>
    public ServiceMenuItemKind Kind { get; init; }

    /// <summary>Объект действия командного пункта; для остальных — <see cref="ServiceMenuItemTarget.None"/>.</summary>
    public ServiceMenuItemTarget Target { get; init; } = ServiceMenuItemTarget.None;

    /// <summary>Имя службы (Target=Service) или имя группы (Target=Group); иначе пусто.</summary>
    public string TargetName { get; init; } = string.Empty;

    /// <summary>Готовый отображаемый текст (например «Запустить», «Все сервисы», имя службы/группы).</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Доступна ли команда при текущем статусе (см. <see cref="ServiceActionAvailability"/>).</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Цвет кружка статуса #RRGGBB (служба: по статусу; группа: по <see cref="GroupStatusKind"/>); пусто — без кружка.</summary>
    public string StatusColorHex { get; init; } = string.Empty;

    /// <summary>Локализованный текст статуса («Работает» / «Все запущены» / …); используется как подпись, где нет цветных иконок.</summary>
    public string StatusText { get; init; } = string.Empty;

    /// <summary>Операция командного пункта; null для подменю/разделителей.</summary>
    public ServiceAction? Action { get; init; }

    /// <summary>
    /// Службы-цели командного пункта: для <see cref="ServiceMenuItemTarget.Group"/> —
    /// члены группы в порядке списка группы; для <see cref="ServiceMenuItemTarget.All"/> —
    /// службы общего списка; для <see cref="ServiceMenuItemTarget.Service"/> — пусто
    /// (цель — <see cref="TargetName"/>).
    /// </summary>
    public IReadOnlyList<string> ServiceNames { get; init; } = Array.Empty<string>();

    /// <summary>Дочерние пункты подменю (команды службы/группы, члены группы, команды «Все сервисы»).</summary>
    public List<ServiceMenuItem> Children { get; init; } = new();

    /// <summary>Является ли пункт разделителем.</summary>
    public bool IsSeparator => Kind == ServiceMenuItemKind.Separator;
}

/// <summary>
/// Чистый построитель данных подменю «Службы» системного трея (issue #11,
/// итерация 2 — группы служб, релиз 1.0.0.12; комментарии 7OH к issue #11 от
/// 13:30 и 13:47). Результат <see cref="Build(ServiceStoreData, Func{string, ServiceStatus}, ServicePlatform)"/> —
/// плоский список пунктов подменю «Службы»:
/// <list type="number">
/// <item>«Настроить…» (<see cref="ServiceMenuItemKind.Settings"/>) — открывает окно
/// «Управление службами»; после него — разделитель (замечание 7OH 18.10);</item>
/// <item>строки списка в порядке <c>data.Order</c>: служба → подменю с командами
/// Запустить/Остановить/Пауза/Восстановить/Перезапустить (структура и доступность —
/// как в контекстном меню окна «Управление службами», замечание 7OH 18.10: разделители
/// после Остановить и Восстановить, доступность по <see cref="ServiceActionAvailability.CanExecute"/>
/// текущего статуса); группа → подменю, где СНАЧАЛА команды действий над всеми членами
/// (тем же порядком с разделителями), затем разделитель, затем каждый член группы как
/// подменю с его командами; у пункта группы — статус группы (цвет кружка по
/// <see cref="ServiceGroupStatusResolver"/>, текст «Все запущены/Частично/Не запущены»);</item>
/// <item>разделитель и подменю «Все сервисы» с командами Запустить/Остановить/
/// Перезапустить (плюс Пауза/Восстановить, если применимо) по всем службам списка —
/// тем же порядком с разделителями, что и команды службы.</item>
/// </list>
/// Класс не имеет платформенных зависимостей (ни WinForms, ни Avalonia): меню
/// строят адаптеры в <c>MainWindow.Tray.cs</c> (WinForms) и
/// <c>MainWindow.Avalonia.Tray.cs</c> (NativeMenu).
/// </summary>
public sealed class ServiceTrayMenuBuilder
{
    private readonly Func<string, string> _t;

    /// <param name="t">Резолвер локализованного текста по ключу; по умолчанию — <see cref="LocalizationManager.T"/>.</param>
    public ServiceTrayMenuBuilder(Func<string, string>? t = null)
        => _t = t ?? (key => LocalizationManager.T(key));

    /// <summary>
    /// Строит пункты подменю «Службы» по данным хранилища и статусам служб.
    /// Перегрузка для случая, когда доступны полные сведения о службе
    /// (включая возможности паузы/остановки) — рекомендуется адаптерам.
    /// </summary>
    /// <param name="data">Данные хранилища служб (может быть null — трактуется как пустой список).</param>
    /// <param name="statusOf">Статус + возможности службы по имени (блокирующие запросы допускаются).</param>
    /// <param name="platform">Платформа для таблицы доступности команд.</param>
    public List<ServiceMenuItem> Build(
        ServiceStoreData? data,
        Func<string, ServiceInfo> statusOf,
        ServicePlatform platform)
    {
        if (statusOf is null)
            throw new ArgumentNullException(nameof(statusOf));

        data ??= new ServiceStoreData();
        var statuses = QueryStatuses(data, statusOf);
        var order = ServiceStoreData.NormalizeOrder(data);

        var menu = new List<ServiceMenuItem>
        {
            // 1) «Настроить…» — окно «Управление службами» (требование 7OH: кнопка
            // настройки сверху); после него — разделитель (замечание 7OH 18.10).
            new ServiceMenuItem
            {
                Kind = ServiceMenuItemKind.Settings,
                Text = _t("Services.TraySettings"),
                IsEnabled = true
            }
        };
        menu.Add(Separator());

        // 2) Строки списка в порядке data.Order: службы и группы. Временно отключённые
        // (issue #37) не дают пунктов: отключённая служба пропускается, отключённая
        // группа скрывается целиком вместе с членами; «Настроить…» остаётся всегда,
        // чтобы галочку можно было снять.
        var visibleKeys = new List<string>();
        foreach (var key in order)
        {
            if (ServiceStoreData.IsGroupKey(key))
            {
                var groupName = key.Substring(ServiceStoreData.GroupKeyPrefix.Length);
                var group = data.Groups.FirstOrDefault(g =>
                    string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
                if (group is not null && !data.IsGroupTemporarilyDisabled(group.Name))
                    visibleKeys.Add(key);
            }
            else if (!data.IsServiceTemporarilyDisabled(key))
            {
                visibleKeys.Add(key);
            }
        }

        var rows = new List<ServiceMenuItem>();
        foreach (var key in visibleKeys)
        {
            if (ServiceStoreData.IsGroupKey(key))
            {
                var groupName = key.Substring(ServiceStoreData.GroupKeyPrefix.Length);
                var group = data.Groups.FirstOrDefault(g =>
                    string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
                if (group is not null)
                    rows.Add(BuildGroupItem(group, statuses, platform, data));
            }
            else if (statuses.TryGetValue(key, out var info))
            {
                rows.Add(BuildServiceItem(key, info, platform));
            }
        }
        menu.AddRange(rows);

        // 3) Разделитель и подменю «Все сервисы» (как в окне «Управление службами») —
        // строится только из НЕ отключённых служб общего списка (issue #37).
        // При пустом списке дополнительный разделитель не добавляем: разделитель после
        // «Настроить…» уже есть (иначе — два разделителя подряд).
        if (rows.Count > 0)
            menu.Add(Separator());
        menu.Add(BuildAllServicesItem(visibleKeys, statuses, platform));

        return menu;
    }

    /// <summary>
    /// Строит пункты подменю «Службы» по данным хранилища и статусам служб
    /// (сигнатура из требования: <c>Func<string, ServiceStatus></c>).
    /// Возможности службы (пауза/остановка) в этой перегрузке неизвестны —
    /// они выводятся консервативно из статуса: поддержка паузы признаётся только
    /// для статуса <see cref="ServiceStatus.Paused"/> (иначе «Пауза» у служб без
    /// поддержки была бы включена), остановка — разрешённой. Адаптеры, у которых
    /// есть полный <see cref="ServiceInfo"/>, должны использовать перегрузку с
    /// <c>Func<string, ServiceInfo></c>.
    /// </summary>
    /// <param name="data">Данные хранилища служб (может быть null — трактуется как пустой список).</param>
    /// <param name="statusOf">Статус службы по имени.</param>
    /// <param name="platform">Платформа для таблицы доступности команд.</param>
    public List<ServiceMenuItem> Build(
        ServiceStoreData? data,
        Func<string, ServiceStatus> statusOf,
        ServicePlatform platform)
    {
        if (statusOf is null)
            throw new ArgumentNullException(nameof(statusOf));

        return Build(data, name =>
        {
            var status = statusOf(name);
            return new ServiceInfo
            {
                ServiceName = name,
                Status = status,
                CanPauseAndContinue = status == ServiceStatus.Paused,
                CanStop = true
            };
        }, platform);
    }

    /// <summary>
    /// Пункт «Служба»: подменю с командами Запустить/Остановить/Пауза/Восстановить/
    /// Перезапустить (структура — как контекстное меню строки окна «Управление
    /// службами»: Запустить/Остановить, разделитель, Пауза/Восстановить, разделитель,
    /// Перезапустить; замечание 7OH 18.10).
    /// </summary>
    private static ServiceMenuItem BuildServiceItem(
        string serviceName,
        ServiceInfo info,
        ServicePlatform platform)
    {
        var item = new ServiceMenuItem
        {
            Kind = ServiceMenuItemKind.ServiceSubmenu,
            Target = ServiceMenuItemTarget.Service,
            TargetName = serviceName,
            Text = string.IsNullOrWhiteSpace(info.DisplayName) ? serviceName : info.DisplayName,
            StatusColorHex = ServiceStatusHex(info.Status),
            StatusText = LocalizationManager.T(ServiceStatusTextKey(info.Status))
        };

        AddActionCommands(
            item.Children,
            ServiceMenuItemKind.ServiceCommand,
            ServiceMenuItemTarget.Service,
            serviceName,
            Array.Empty<string>(),
            action => ServiceActionAvailability.CanExecute(
                action, info.Status, info.CanPauseAndContinue, info.CanStop, platform));
        return item;
    }

    /// <summary>
    /// Пункт «Группа»: команды действий над ВСЕМИ членами (по статусам членов, в
    /// порядке списка группы; структура с разделителями — как контекстное меню
    /// группы окна «Управление службами», замечание 7OH 18.10), затем разделитель,
    /// затем каждый член группы как подменю с его командами (комментарий 7OH 13:47).
    /// </summary>
    private static ServiceMenuItem BuildGroupItem(
        ServiceGroup group,
        IReadOnlyDictionary<string, ServiceInfo> statuses,
        ServicePlatform platform,
        ServiceStoreData data)
    {
        // Временно отключённые члены группы (issue #37) не попадают ни в команды
        // группы, ни в подменю членов (но сама группа остаётся, если отключена
        // только отдельная служба).
        var visibleMembers = group.Services
            .Where(n => !data.IsServiceTemporarilyDisabled(n))
            .ToList();
        var members = visibleMembers
            .Select(n => InfoOf(statuses, n))
            .ToList();
        var groupStatus = ServiceGroupStatusResolver.Resolve(members.Select(m => m.Status).ToList());

        var item = new ServiceMenuItem
        {
            Kind = ServiceMenuItemKind.GroupSubmenu,
            Target = ServiceMenuItemTarget.Group,
            TargetName = group.Name,
            Text = group.Name,
            StatusColorHex = GroupStatusHex(groupStatus),
            StatusText = LocalizationManager.T(GroupStatusTextKey(groupStatus))
        };

        // Сначала — команды над всеми членами группы (доступность — CanExecuteAny,
        // как команды группы в окне «Управление службами»).
        AddActionCommands(
            item.Children,
            ServiceMenuItemKind.GroupCommand,
            ServiceMenuItemTarget.Group,
            group.Name,
            visibleMembers,
            action => ServiceActionAvailability.CanExecuteAny(members, action, platform));

        // Затем разделитель и все элементы группы ниже — как в общем списке.
        item.Children.Add(Separator());
        foreach (var memberName in visibleMembers)
            item.Children.Add(BuildServiceItem(memberName, InfoOf(statuses, memberName), platform));

        return item;
    }

    /// <summary>
    /// Подменю «Все сервисы»: команды по всем службам ОБЩЕГО списка (не по членам
    /// групп) — как в окне «Управление службами» (<c>Rows.OfType<ServiceInfoRow></c>),
    /// с разделителями между группами команд (Запустить/Остановить | Пауза/Восстановить |
    /// Перезапустить — как контекстное меню окна; замечание 7OH 18.10).
    /// Пауза/Восстановление добавляются, только если применимы хотя бы к одной службе.
    /// </summary>
    private static ServiceMenuItem BuildAllServicesItem(
        IReadOnlyList<string> order,
        IReadOnlyDictionary<string, ServiceInfo> statuses,
        ServicePlatform platform)
    {
        var services = order
            .Where(k => !ServiceStoreData.IsGroupKey(k))
            .Select(n => InfoOf(statuses, n))
            .ToList();

        var item = new ServiceMenuItem
        {
            Kind = ServiceMenuItemKind.AllServicesSubmenu,
            Text = LocalizationManager.T("Services.AllServices"),
            // «Все сервисы» без служб общего списка неактивно (Задача 5, issue #11):
            // пункт остаётся в меню, но команды и сам подменю недоступны.
            IsEnabled = services.Count > 0
        };

        AddActionCommands(
            item.Children,
            ServiceMenuItemKind.AllServicesCommand,
            ServiceMenuItemTarget.All,
            string.Empty,
            services.Select(s => s.ServiceName).ToList(),
            action => ServiceActionAvailability.CanExecuteAny(services, action, platform),
            allServices: true);

        return item;
    }

    /// <summary>
    /// Добавляет команды службы/группы/«Все сервисы» в порядке контекстного меню окна
    /// «Управление службами» (замечание 7OH 18.10): Запустить, Остановить, разделитель,
    /// Пауза, Восстановить, разделитель, Перезапустить — с разделителями между группами
    /// команд. Для «Все сервисы» (<paramref name="allServices"/>) тексты команд —
    /// «Запустить все/Остановить все/Перезапустить все», а сами команды включаются,
    /// только если операция применима хотя бы к одной службе списка. Пустая средняя
    /// группа (Пауза/Восстановить неприменимы) не порождает двойных разделителей.
    /// </summary>
    private static void AddActionCommands(
        List<ServiceMenuItem> target,
        ServiceMenuItemKind kind,
        ServiceMenuItemTarget menuTarget,
        string targetName,
        IReadOnlyList<string> serviceNames,
        Func<ServiceAction, bool> isEnabled,
        bool allServices = false)
    {
        // Три визуальные группы команд — как в контекстном меню окна «Управление
        // службами»: Запустить/Остановить | Пауза/Восстановить | Перезапустить.
        ServiceAction[][] groups =
        {
            new[] { ServiceAction.Start, ServiceAction.Stop },
            new[] { ServiceAction.Pause, ServiceAction.Continue },
            new[] { ServiceAction.Restart }
        };

        var groupsAdded = 0;
        foreach (var group in groups)
        {
            var items = new List<ServiceMenuItem>();
            foreach (var action in group)
            {
                // «Все сервисы»: команда добавляется, только если применима хотя бы
                // к одной службе; службы/группы — всегда (доступность — IsEnabled).
                if (allServices && !isEnabled(action))
                    continue;
                items.Add(new ServiceMenuItem
                {
                    Kind = kind,
                    Target = menuTarget,
                    TargetName = targetName,
                    Action = action,
                    Text = allServices ? LAll(action) : L(action),
                    IsEnabled = isEnabled(action),
                    ServiceNames = serviceNames
                });
            }

            if (items.Count == 0)
                continue;
            if (groupsAdded > 0)
                target.Add(Separator());
            target.AddRange(items);
            groupsAdded++;
        }
    }

    /// <summary>Опрашивает статусы всех служб списка и членов групп (с защитой от сбоев отдельных служб).</summary>
    private static Dictionary<string, ServiceInfo> QueryStatuses(
        ServiceStoreData data,
        Func<string, ServiceInfo> statusOf)
    {
        // Временно отключённые службы (issue #37) в пункты меню не попадают —
        // их статусы не нужны (не опрашиваем, чтобы не дёргать недоступные unit'ы).
        var names = data.Services
            .Where(n => !data.IsServiceTemporarilyDisabled(n))
            .Concat(data.Groups.SelectMany(g => g.Services))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, ServiceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            try
            {
                result[name] = statusOf(name);
            }
            catch
            {
                // Недоступная служба не должна ронять построение меню: статус — Unknown.
                result[name] = new ServiceInfo { ServiceName = name, Status = ServiceStatus.Unknown };
            }
        }
        return result;
    }

    private static ServiceInfo InfoOf(IReadOnlyDictionary<string, ServiceInfo> statuses, string name) =>
        statuses.TryGetValue(name, out var info)
            ? info
            : new ServiceInfo { ServiceName = name, Status = ServiceStatus.Unknown };

    private static ServiceMenuItem Separator() => new()
    {
        Kind = ServiceMenuItemKind.Separator,
        IsEnabled = false
    };

    private static string L(ServiceAction action) => LocalizationManager.T(ActionTextKey(action));

    private static string LAll(ServiceAction action) => LocalizationManager.T(AllServicesTextKey(action));

    /// <summary>Ключ локализации текста команды службы/группы.</summary>
    private static string ActionTextKey(ServiceAction action) => action switch
    {
        ServiceAction.Start => "Services.Start",
        ServiceAction.Stop => "Services.Stop",
        ServiceAction.Pause => "Services.Pause",
        ServiceAction.Continue => "Services.Continue",
        ServiceAction.Restart => "Services.Restart",
        _ => "Services.Start"
    };

    /// <summary>
    /// Ключ локализации текста команды «Все сервисы»: Запустить все/Остановить
    /// все/Перезапустить все (существующие ключи окна); для нестандартных
    /// Пауза/Восстановить — обычные команды (отдельных ключей «все» нет).
    /// </summary>
    private static string AllServicesTextKey(ServiceAction action) => action switch
    {
        ServiceAction.Start => "Services.StartAll",
        ServiceAction.Stop => "Services.StopAll",
        ServiceAction.Restart => "Services.RestartAll",
        _ => ActionTextKey(action)
    };

    /// <summary>Ключ локализации текста статуса службы (те же ключи, что у окна «Управление службами»).</summary>
    private static string ServiceStatusTextKey(ServiceStatus status) => status switch
    {
        ServiceStatus.Running => "Services.Status.Running",
        ServiceStatus.Stopped => "Services.Status.Stopped",
        ServiceStatus.Paused => "Services.Status.Paused",
        ServiceStatus.Starting => "Services.Status.Starting",
        ServiceStatus.Stopping => "Services.Status.Stopping",
        ServiceStatus.NotFound => "Services.Status.NotFound",
        _ => "Services.Status.Unknown"
    };

    /// <summary>Цвет кружка статуса службы (совпадает с ServiceStatusDisplay окна).</summary>
    private static string ServiceStatusHex(ServiceStatus status) => status switch
    {
        ServiceStatus.Running => "#22C55E",
        ServiceStatus.Starting => "#F59E0B",
        ServiceStatus.Stopping => "#F59E0B",
        ServiceStatus.NotFound => "#EF4444",
        _ => "#6B7280"
    };

    /// <summary>Ключ локализации текста статуса группы (те же ключи, что у окна «Управление службами»).</summary>
    private static string GroupStatusTextKey(GroupStatusKind kind) => kind switch
    {
        GroupStatusKind.AllRunning => "Services.Group.Status.AllRunning",
        GroupStatusKind.Partial => "Services.Group.Status.Partial",
        GroupStatusKind.None => "Services.Group.Status.None",
        _ => "Services.Status.Unknown"
    };

    /// <summary>Цвет кружка статуса группы (зелёный/жёлтый/серый — как в окне «Управление службами»).</summary>
    private static string GroupStatusHex(GroupStatusKind kind) => kind switch
    {
        GroupStatusKind.AllRunning => "#22C55E",
        GroupStatusKind.Partial => "#F59E0B",
        _ => "#6B7280"
    };

    /// <summary>
    /// Экранирует '_' для WPF-заголовков <c>MenuItem</c> (issue #11, часть 2,
    /// релиз 1.0.0.18; замечание 7OH от 19.10): WPF трактует '_' как мнемонику
    /// AccessKey — «S_DB» превращается в «SDB» с подчёркнутой «D», а сам символ
    /// пропадает. Двойное подчёркивание '__' WPF отображает как один '_' и мнемонику
    /// не создаёт, поэтому <c>text.Replace("_", "__")</c>. В WinForms-трее мнемоника —
    /// '&' (экранирование не требуется); в Avalonia '_' мнемоникой не считается.
    /// null → пустая строка (конвенция модели: <see cref="ServiceMenuItem.Text"/>
    /// по умолчанию <c>string.Empty</c>).
    /// </summary>
    public static string EscapeMenuText(string? text) => text?.Replace("_", "__") ?? string.Empty;
}