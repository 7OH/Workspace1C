using System.Collections.Generic;
using System.Linq;

namespace Workspace1C.Services;

/// <summary>
/// Операционный статус службы (Windows: <c>ServiceControllerStatus</c>;
/// Linux: состояние unit'а systemd). Промежуточные состояния «перезапуска/паузы»
/// отображаются на <see cref="Starting"/>/<see cref="Stopping"/> — в этих состояниях
/// все команды недоступны (см. <see cref="ServiceActionAvailability"/>).
/// </summary>
public enum ServiceStatus
{
    /// <summary>Статус неизвестен (например, не удалось опросить службу).</summary>
    Unknown,

    /// <summary>Запущена.</summary>
    Running,

    /// <summary>Остановлена.</summary>
    Stopped,

    /// <summary>Запускается (StartPending / activating).</summary>
    Starting,

    /// <summary>Останавливается (StopPending / deactivating).</summary>
    Stopping,

    /// <summary>На паузе (Paused; только Windows, systemd паузу не поддерживает).</summary>
    Paused,

    /// <summary>Служба с таким именем не найдена в системе.</summary>
    NotFound
}

/// <summary>
/// Операция над службой из окна «Управление службами» (issue #11).
/// </summary>
public enum ServiceAction
{
    Start,
    Stop,
    Pause,
    Continue,
    Restart
}

/// <summary>
/// Платформа для выбора «мапы» доступности команд (issue #11, раздел 3.1 плана):
/// на Linux <c>systemctl restart</c> допустим и для остановленных unit'ов, поэтому
/// «Перезапустить» разрешён и для Stopped; на Windows — только для Running.
/// </summary>
public enum ServicePlatform
{
    Windows,
    Linux
}

/// <summary>
/// Лёгкое представление службы (то, что показываем в списке «Управление службами»).
/// Windows: <c>ServiceName</c> — имя службы, <c>DisplayName</c> — отображаемое имя.
/// Linux: <c>ServiceName</c> — имя unit'а (<c>*.service</c>), <c>DisplayName</c> — описание.
/// </summary>
public sealed class ServiceInfo
{
    /// <summary>Windows: ServiceName; Linux: имя unit'а (например <c>postgresql.service</c>).</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Windows: DisplayName; Linux: Description/имя.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Операционный статус.</summary>
    public ServiceStatus Status { get; set; }

    /// <summary>Поддерживает ли служба паузу и продолжение (Windows: код 0x102).
    /// На Linux всегда false — systemd не умеет pause/continue для обычных служб.</summary>
    public bool CanPauseAndContinue { get; set; }

    /// <summary>Можно ли остановить службу (Windows: <c>CanStop</c>; Linux: true).</summary>
    public bool CanStop { get; set; } = true;

    /// <summary>Копия для безопасного переноса между потоками.</summary>
    public ServiceInfo Clone() => new()
    {
        ServiceName = ServiceName,
        DisplayName = DisplayName,
        Status = Status,
        CanPauseAndContinue = CanPauseAndContinue,
        CanStop = CanStop
    };
}

/// <summary>
/// Чистый вычислитель доступности команд служб (issue #11, таблица истинности из 3.1):
/// каждая операция допустима только при определённом статусе и возможностях службы.
/// Тестируется исчерпывающей таблицей (<see cref="Workspace1C.Tests.ServiceActionAvailabilityTests"/>).
/// </summary>
public static class ServiceActionAvailability
{
    /// <summary>
    /// Доступна ли операция <paramref name="action"/> для службы с указанным статусом
    /// и возможностями (платформозависимое правило перезапуска остановленной службы).
    /// </summary>
    public static bool CanExecute(
        ServiceAction action,
        ServiceStatus status,
        bool canPauseAndContinue,
        bool canStop,
        ServicePlatform platform)
    {
        switch (action)
        {
            case ServiceAction.Start:
                return status == ServiceStatus.Stopped;

            case ServiceAction.Stop:
                return (status == ServiceStatus.Running || status == ServiceStatus.Paused) && canStop;

            case ServiceAction.Pause:
                return status == ServiceStatus.Running && canPauseAndContinue;

            case ServiceAction.Continue:
                return status == ServiceStatus.Paused && canPauseAndContinue;

            case ServiceAction.Restart:
                // Windows: System.ServiceProcess.ServiceController.Restart() требует Running.
                // Linux: systemctl restart сам запускает остановленный unit, поэтому остановка
                // не мешает — «Перезапустить» допустим и для Stopped (раздел 3.1 плана).
                if (status == ServiceStatus.Running)
                    return true;
                return platform == ServicePlatform.Linux && status == ServiceStatus.Stopped;

            default:
                return false;
        }
    }

    /// <summary>Доступна ли операция хотя бы для одной службы набора (подменю «Все сервисы»).</summary>
    public static bool CanExecuteAny(
        IEnumerable<ServiceInfo> services,
        ServiceAction action,
        ServicePlatform platform)
    {
        if (services is null)
            return false;
        return services.Any(s => CanExecute(action, s.Status, s.CanPauseAndContinue, s.CanStop, platform));
    }
}

/// <summary>
/// Агрегированный статус группы служб (issue #11, итерация 2 — группы служб,
/// релиз 1.0.0.12): результат <see cref="ServiceGroupStatusResolver.Resolve"/>.
/// </summary>
public enum GroupStatusKind
{
    /// <summary>Неизвестно (пустая группа или нет данных о членах).</summary>
    Unknown,

    /// <summary>Все члены группы запущены.</summary>
    AllRunning,

    /// <summary>Запущена не вся группа, но хотя бы один член запущен.</summary>
    Partial,

    /// <summary>Ни один член группы не запущен.</summary>
    None
}

/// <summary>
/// Чистый вычислитель статуса группы служб (issue #11, итерация 2 — группы служб,
/// релиз 1.0.0.12): агрегирует статусы членов группы в <see cref="GroupStatusKind"/>.
/// Правила (комментарий 7OH к issue #11, 13:25/16:54):
/// — все члены Running → <see cref="GroupStatusKind.AllRunning"/> (зелёный кружок);
/// — хотя бы один Running, но не все → <see cref="GroupStatusKind.Partial"/> (жёлтый
///   кружок) — обязательное требование «жёлтый, если запущены не все службы списка,
///   но хоть одна запущена»;
/// — ни одного Running → <see cref="GroupStatusKind.None"/> (серый кружок);
/// — пустой список → <see cref="GroupStatusKind.Unknown"/> (серый кружок).
/// Решение по NotFound (7OH оставил на усмотрение): член со статусом NotFound считается
/// «не запущен» — группа с исчезнувшей службой не может считаться рабочей, итог — None
/// (серый). Красный (#EF4444) остаётся за строкой самой службы (NotFound), чтобы не
/// смешивать два уровня индикации. Тестируется в Задаче 5 (полный набор).
/// </summary>
public static class ServiceGroupStatusResolver
{
    /// <summary>Агрегирует статусы членов группы (порядок не важен).</summary>
    public static GroupStatusKind Resolve(IReadOnlyList<ServiceStatus> statuses)
    {
        if (statuses is null || statuses.Count == 0)
            return GroupStatusKind.Unknown;

        var running = 0;
        foreach (var status in statuses)
        {
            if (status == ServiceStatus.Running)
                running++;
        }

        if (running == statuses.Count)
            return GroupStatusKind.AllRunning;
        return running > 0 ? GroupStatusKind.Partial : GroupStatusKind.None;
    }
}

/// <summary>
/// Группа служб окна «Управление службами» (issue #11, итерация 2 — группы служб,
/// релиз 1.0.0.12): именованная подборка служб, отображаемая в списке отдельным
/// блоком. Имя группы — описание (непустое после нормализации), состав — имена
/// служб в порядке пользователя.
/// </summary>
public sealed class ServiceGroup
{
    /// <summary>Описание группы (непустое после нормализации).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Имена служб группы (порядок задаёт пользователь).</summary>
    public List<string> Services { get; set; } = new();
}

/// <summary>
/// Содержимое хранилища служб окна «Управление службами» (issue #11, итерация 2 —
/// группы служб, релиз 1.0.0.12): службы вне групп (общий список), группы служб и
/// общий порядок строк списка (<see cref="Order"/>). Обратная совместимость: старый
/// формат <c>services.json</c> (массив строк, итерация 1) читается как
/// <see cref="Services"/> без групп — см. <see cref="FromLegacy"/>; объект без
/// <see cref="Order"/> (итерация 2, до этой задачи) получает порядок автоматически:
/// все службы, затем группы.
/// </summary>
public sealed class ServiceStoreData
{
    /// <summary>Префикс ключа группы в <see cref="Order"/> (например <c>group:База 1С</c>).</summary>
    public const string GroupKeyPrefix = "group:";

    /// <summary>Службы вне групп (общий список, порядок задаёт пользователь).</summary>
    public List<string> Services { get; set; } = new();

    /// <summary>Группы служб (порядок задаёт пользователь).</summary>
    public List<ServiceGroup> Groups { get; set; } = new();

    /// <summary>
    /// Общий порядок строк списка «Управление службами» (issue #11, итерация 2):
    /// смешанная последовательность ключей — имя службы (строка-служба) или
    /// <c>group:имя</c> (строка-группа). При чтении старого объекта (без Order)
    /// порядок строится автоматически: все службы, затем группы. Дубли/пропущенные
    /// ключи нормализуются (<see cref="NormalizeOrder"/>).
    /// </summary>
    public List<string> Order { get; set; } = new();

    /// <summary>
    /// Контейнер из старого формата (итерация 1): массив имён служб — службы
    /// без групп. Используется при миграции <c>services.json</c> и в тестах.
    /// </summary>
    public static ServiceStoreData FromLegacy(IReadOnlyList<string>? services) => new()
    {
        Services = services?.ToList() ?? new List<string>()
    };

    /// <summary>Ключ группы в <see cref="Order"/>.</summary>
    public static string GroupKey(string groupName) => GroupKeyPrefix + groupName;

    /// <summary>Является ли ключ <see cref="Order"/> ключом группы (префикс <c>group:</c>).</summary>
    public static bool IsGroupKey(string key) =>
        key is not null && key.StartsWith(GroupKeyPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Приводит содержимое к каноническому виду (issue #11, итерация 2):
    /// Trim имён, пропуск пустых, удаление дубликатов (OrdinalIgnoreCase) — для
    /// служб вне групп, имён групп и служб внутри групп; у групп не должно быть
    /// дубликатов имён. Порядок (<see cref="Order"/>) нормализуется: дубли/пропущенные
    /// ключи удаляются, недостающие службы/группы дописываются в конец. Порядок
    /// сохраняется. Возвращает новый объект, исходный не меняет.
    /// </summary>
    public static ServiceStoreData Normalize(ServiceStoreData? source)
    {
        var result = new ServiceStoreData();
        if (source is null)
            return result;
        result.Groups = NormalizeGroups(source.Groups);
        // Инвариант «служба ровно в одном месте» (1.0.0.14, issue #11): служба,
        // попавшая в группу, не должна оставаться в корневом списке Services.
        // Приоритет — группе (служба видна внутри папки-группы).
        var inGroups = new HashSet<string>(
            result.Groups.SelectMany(g => g.Services), StringComparer.OrdinalIgnoreCase);
        result.Services = NormalizeNames(source.Services)
            .Where(n => !inGroups.Contains(n))
            .ToList();
        // Порядок нормализуем по ИСХОДНОМУ Order (у result.Order — пустой список по
        // умолчанию, иначе «порядок не задан» не отличить от «порядок пуст»).
        result.Order = NormalizeOrder(result.Services, result.Groups, source.Order);
        return result;
    }

    /// <summary>
    /// Нормализует общий порядок строк: Trim ключей, пропуск пустых, удаление
    /// дубликатов (OrdinalIgnoreCase), отбрасывание ключей, не ссылающихся на
    /// существующие службы/группы; недостающие службы и группы дописываются в конец
    /// (сначала службы, затем группы — в их порядке). Возвращает новый список.
    /// </summary>
    public static List<string> NormalizeOrder(ServiceStoreData? data)
    {
        if (data is null)
            return new List<string>();
        return NormalizeOrder(data.Services, data.Groups, data.Order);
    }

    /// <summary>Реализация нормализации порядка по нормализованным службам/группам и исходному Order.</summary>
    private static List<string> NormalizeOrder(
        IReadOnlyList<string> services,
        IReadOnlyList<ServiceGroup> groups,
        IReadOnlyList<string>? order)
    {
        var result = new List<string>();
        var serviceSet = new HashSet<string>(services, System.StringComparer.OrdinalIgnoreCase);
        var groupSet = new HashSet<string>(groups.Select(g => g.Name), System.StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        if (order is not null)
        {
            foreach (var raw in order)
            {
                var key = (raw ?? string.Empty).Trim();
                if (key.Length == 0 || !seen.Add(key))
                    continue;
                if (ResolveKey(key, serviceSet, groupSet))
                    result.Add(key);
            }
        }

        // Дополняем пропущенные: сначала службы, затем группы (в их порядке).
        foreach (var name in services)
        {
            if (seen.Add(name))
                result.Add(name);
        }
        foreach (var group in groups)
        {
            var key = GroupKey(group.Name);
            if (seen.Add(key))
                result.Add(key);
        }
        return result;
    }

    /// <summary>
    /// Проверяет, ссылается ли ключ на существующую службу или группу. Ключ с
    /// префиксом <c>group:</c> считается ключом группы, только если группа с таким
    /// именем существует; иначе (и если имя совпадает со службой) — ключом службы.
    /// </summary>
    private static bool ResolveKey(string key, HashSet<string> services, HashSet<string> groups)
    {
        if (key.StartsWith(GroupKeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var groupName = key.Substring(GroupKeyPrefix.Length).Trim();
            if (groupName.Length > 0 && groups.Contains(groupName))
                return true;
        }
        return services.Contains(key);
    }

    /// <summary>Нормализует список имён: Trim, пропуск пустых, удаление дубликатов (порядок сохраняется).</summary>
    private static List<string> NormalizeNames(IReadOnlyList<string>? source)
    {
        var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        if (source is null)
            return result;

        foreach (var name in source)
        {
            var normalized = (name ?? string.Empty).Trim();
            if (normalized.Length == 0)
                continue;
            if (seen.Add(normalized))
                result.Add(normalized);
        }
        return result;
    }

    /// <summary>Нормализует группы: Trim имени, пропуск пустых, удаление дубликатов
    /// имён (OrdinalIgnoreCase). Члены группы: Trim, пропуск пустых, удаление дубликатов
    /// внутри группы И между группами — служба не может состоять в двух группах
    /// (инвариант «служба ровно в одном месте», 1.0.0.14, issue #11); приоритет —
    /// первой группе по порядку списка.</summary>
    private static List<ServiceGroup> NormalizeGroups(IReadOnlyList<ServiceGroup>? source)
    {
        var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var seenMembers = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var result = new List<ServiceGroup>();
        if (source is null)
            return result;

        foreach (var group in source)
        {
            if (group is null)
                continue;
            var name = (group.Name ?? string.Empty).Trim();
            if (name.Length == 0)
                continue;
            if (!seen.Add(name))
                continue;
            var members = new List<string>();
            foreach (var raw in group.Services ?? new List<string>())
            {
                var normalized = (raw ?? string.Empty).Trim();
                if (normalized.Length == 0)
                    continue;
                if (seenMembers.Add(normalized))
                    members.Add(normalized);
            }
            result.Add(new ServiceGroup
            {
                Name = name,
                Services = members
            });
        }
        return result;
    }
}