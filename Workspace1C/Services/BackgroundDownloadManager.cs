using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Workspace1C.Services;

/// <summary>Состояние фоновой загрузки.</summary>
public enum BackgroundDownloadState
{
    /// <summary>Загрузка выполняется.</summary>
    Active,

    /// <summary>Загрузка успешно завершена.</summary>
    Completed,

    /// <summary>Загрузка завершилась ошибкой.</summary>
    Failed,

    /// <summary>Загрузка отменена пользователем.</summary>
    Cancelled,
}

/// <summary>
/// Менеджер фоновых загрузок (issue #334 п.1): реестр активных скачиваний, живущий
/// в сервисном слое и НЕ привязанный к жизни окон. Окно скачивания платформы и
/// скачивание цепочек обновлений регистрируют загрузку при старте; после закрытия
/// окна загрузка продолжается, а главное окно показывает индикатор (число активных
/// загрузок, агрегированный прогресс) и позволяет отменить загрузку.
/// Класс потокобезопасен: события могут приходить из фоновых потоков; подписчики
/// (главное окно) сами маршалируют обновление UI через <see cref="UiDispatch"/>.
/// </summary>
public sealed class BackgroundDownloadManager
{
    /// <summary>Описатель одной фоновой загрузки.</summary>
    public sealed class ActiveDownload
    {
        /// <summary>Стабильный идентификатор (например, «platform:8.3.27.2214:file.zip»).</summary>
        public string Id { get; }

        /// <summary>Нормализованный полный путь целевого файла (issue #22); null — путь
        /// неизвестен (загрузка не участвует в проверке пересечения по файлу).</summary>
        public string? TargetPath { get; }

        /// <summary>Человекочитаемый заголовок для индикатора (имя файла/цепочки);
        /// может уточняться через <see cref="BackgroundDownloadManager.UpdateTitle"/>.</summary>
        public string Title { get; internal set; }

        /// <summary>Прогресс 0..1 (обновляется через <see cref="BackgroundDownloadManager.ReportProgress"/>).</summary>
        public double Progress { get; internal set; }

        /// <summary>Состояние загрузки.</summary>
        public BackgroundDownloadState State { get; internal set; }

        /// <summary>Ключ локализации ошибки (для состояния Failed) или null.</summary>
        public string? ErrorKey { get; internal set; }

        /// <summary>Оценка оставшегося времени (issue #360): обновляется при каждом
        /// отчёте прогресса; null — данных ещё мало.</summary>
        public TimeSpan? EstimatedRemaining { get; internal set; }

        /// <summary>Источник отмены: отмена из индикатора главного окна пробрасывается
        /// в выполняющуюся загрузку через этот токен.</summary>
        public CancellationTokenSource Cancellation { get; } = new();

        /// <summary>Оценщик оставшегося времени (см. <see cref="EstimatedRemaining"/>).</summary>
        internal DownloadEtaTracker Eta { get; }

        /// <summary>True — загрузка ещё выполняется.</summary>
        public bool IsActive => State == BackgroundDownloadState.Active;

        internal ActiveDownload(string id, string title, string? targetPath, Func<DateTime> utcNow)
        {
            Id = id;
            Title = title;
            TargetPath = targetPath;
            State = BackgroundDownloadState.Active;
            Progress = 0;
            Eta = new DownloadEtaTracker(utcNow);
        }

        /// <summary>Отображаемый текст (отладка/журналы).</summary>
        public override string ToString() => $"{Title} ({Progress:P0})";
    }

    /// <summary>Событие любого изменения реестра (старт/прогресс/завершение/отмена) —
    /// источник для агрегированного индикатора главного окна.</summary>
    public event Action? Changed;

    /// <summary>Загрузка успешно завершена.</summary>
    public event Action<ActiveDownload>? Completed;

    /// <summary>Загрузка завершилась ошибкой или отменена.</summary>
    public event Action<ActiveDownload>? Failed;

    private readonly object _lock = new();
    private readonly Dictionary<string, ActiveDownload> _downloads = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _utcNow;

    /// <summary>Общий экземпляр приложения: окна регистрируют загрузки в нём,
    /// главное окно подписывается на его события.</summary>
    public static BackgroundDownloadManager Default { get; } = new();

    /// <summary>Часы для оценки оставшегося времени (issue #360) — инжектируются
    /// в тестах; по умолчанию — реальное время.</summary>
    internal BackgroundDownloadManager(Func<DateTime>? utcNow = null)
        => _utcNow = utcNow ?? (static () => DateTime.UtcNow);

    /// <summary>Число активных (выполняющихся) загрузок.</summary>
    public int ActiveCount
    {
        get
        {
            lock (_lock)
                return _downloads.Values.Count(d => d.IsActive);
        }
    }

    /// <summary>Снимок реестра загрузок (потокобезопасная копия).</summary>
    public IReadOnlyList<ActiveDownload> Snapshot()
    {
        lock (_lock)
            return _downloads.Values.ToList();
    }

    /// <summary>Регистрирует старт фоновой загрузки. Повторный старт с тем же
    /// <paramref name="id"/> при активной загрузке возвращает существующую запись
    /// (повторный вызов из окна не создаёт дубликат); завершённая запись заменяется
    /// новой (повторное скачивание того же файла). С 1.0.0.26 (issue #22) при активной
    /// загрузке того же целевого файла <paramref name="targetPath"/> (другой id) также
    /// возвращается существующая запись — фоновая и ручная закачки одного файла не
    /// создают две записи и не пишут в файл параллельно. Поведение — см.
    /// <see cref="StartIfNew"/>; здесь отбрасывается признак «создана новая запись».</summary>
    public ActiveDownload Start(string id, string title, string? targetPath = null)
        => StartIfNew(id, title, targetPath).Entry;

    /// <summary>
    /// Регистрирует старт фоновой загрузки и сообщает, создана ли НОВАЯ запись
    /// (issue #22, 1.0.0.28). Повторный старт с тем же <paramref name="id"/> при активной
    /// загрузке возвращает существующую запись с <c>IsNew == false</c>; завершённая запись
    /// заменяется новой (<c>IsNew == true</c>). При активной загрузке того же целевого
    /// файла <paramref name="targetPath"/> (другой id) также возвращается существующая
    /// запись — фоновая и ручная закачки одного файла не создают две записи и не пишут
    /// в файл параллельно. Вызывающий, получивший <c>IsNew == false</c>, НЕ должен начинать
    /// реальную загрузку: файл уже пишется другой операцией (single-writer).
    /// </summary>
    public (ActiveDownload Entry, bool IsNew) StartIfNew(string id, string title, string? targetPath = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Идентификатор загрузки обязателен", nameof(id));

        var normalizedTarget = NormalizeTargetPath(targetPath);

        ActiveDownload entry;
        bool isNew;
        lock (_lock)
        {
            if (_downloads.TryGetValue(id, out var existing) && existing.IsActive)
                return (existing, false);

            // issue #22: активная загрузка того же целевого файла (другой id) — возвращаем
            // существующую запись, чтобы повторный запуск не создавал дубликат.
            if (normalizedTarget is not null)
            {
                var byPath = _downloads.Values.FirstOrDefault(d => d.IsActive
                    && d.TargetPath is not null
                    && string.Equals(d.TargetPath, normalizedTarget, StringComparison.OrdinalIgnoreCase));
                if (byPath is not null)
                    return (byPath, false);
            }

            entry = new ActiveDownload(id, title ?? string.Empty, normalizedTarget, _utcNow);
            _downloads[id] = entry;
            isNew = true;
        }

        RaiseChanged();
        return (entry, isNew);
    }

    /// <summary>True — идёт активная загрузка в указанный целевой файл (issue #22).
    /// Пустой/невалидный путь — false (в проверке не участвует).</summary>
    public bool IsFileBeingDownloaded(string targetPath)
        => FindByTargetPath(targetPath) is not null;

    /// <summary>Находит активную загрузку по целевому файлу (issue #22). Сравнение путей
    /// без учёта регистра после нормализации через <see cref="Path.GetFullPath"/>;
    /// пустой/невалидный путь — null.</summary>
    public ActiveDownload? FindByTargetPath(string targetPath)
    {
        var normalized = NormalizeTargetPath(targetPath);
        if (normalized is null)
            return null;

        lock (_lock)
            return _downloads.Values.FirstOrDefault(d => d.IsActive
                && d.TargetPath is not null
                && string.Equals(d.TargetPath, normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Нормализует целевой путь для сравнения (issue #22): полный путь через
    /// <see cref="Path.GetFullPath"/>. Пустой/пробельный/невалидный путь — null
    /// (загрузка не участвует в проверке пересечения по файлу).</summary>
    private static string? NormalizeTargetPath(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return null;
        try
        {
            return Path.GetFullPath(targetPath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Обновляет прогресс активной загрузки (0..1, клампится). Неактивные
    /// записи игнорируются — поздние отчёты прогресса после завершения не «оживляют»
    /// запись в индикаторе.</summary>
    public void ReportProgress(string id, double progress)
    {
        lock (_lock)
        {
            if (!_downloads.TryGetValue(id, out var entry) || !entry.IsActive)
                return;
            entry.Progress = Math.Clamp(progress, 0, 1);
            entry.EstimatedRemaining = entry.Eta.Update(entry.Progress);
        }

        RaiseChanged();
    }

    /// <summary>Уточняет заголовок активной загрузки (issue #352: имя текущего файла
    /// цепочки обновлений вместо названия конфигурации). Неактивные/несуществующие
    /// записи игнорируются; пустой/пробельный заголовок не применяется.</summary>
    public void UpdateTitle(string id, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return;
        lock (_lock)
        {
            if (!_downloads.TryGetValue(id, out var entry) || !entry.IsActive)
                return;
            entry.Title = title;
        }

        RaiseChanged();
    }

    /// <summary>Помечает загрузку успешно завершённой.</summary>
    public void Complete(string id)
    {
        ActiveDownload? entry;
        lock (_lock)
        {
            if (!_downloads.TryGetValue(id, out entry))
                return;
            entry.State = BackgroundDownloadState.Completed;
            entry.Progress = 1;
            entry.EstimatedRemaining = TimeSpan.Zero;
        }

        RaiseChanged();
        Completed?.Invoke(entry);
    }

    /// <summary>Помечает загрузку завершившейся ошибкой.</summary>
    public void Fail(string id, string? errorKey = null)
    {
        ActiveDownload? entry;
        lock (_lock)
        {
            if (!_downloads.TryGetValue(id, out entry))
                return;
            entry.State = BackgroundDownloadState.Failed;
            entry.ErrorKey = errorKey;
            entry.EstimatedRemaining = null;
        }

        RaiseChanged();
        Failed?.Invoke(entry);
    }

    /// <summary>Отменяет загрузку: выставляет состояние Cancelled и пробрасывает отмену
    /// в выполняющийся запрос через токен. True — запись найдена и была активной.</summary>
    public bool Cancel(string id)
    {
        ActiveDownload? entry;
        lock (_lock)
        {
            if (!_downloads.TryGetValue(id, out entry) || !entry.IsActive)
                return false;
            entry.State = BackgroundDownloadState.Cancelled;
            entry.EstimatedRemaining = null;
        }

        try
        {
            entry.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Токен уже освобождён — состояние всё равно зафиксировано.
        }

        RaiseChanged();
        Failed?.Invoke(entry);
        return true;
    }

    /// <summary>Отменяет все активные загрузки (кнопка отмены в индикаторе).</summary>
    public void CancelAll()
    {
        List<ActiveDownload> toCancel;
        lock (_lock)
        {
            toCancel = _downloads.Values.Where(d => d.IsActive).ToList();
            foreach (var entry in toCancel)
            {
                entry.State = BackgroundDownloadState.Cancelled;
                entry.EstimatedRemaining = null;
            }
        }

        foreach (var entry in toCancel)
        {
            try
            {
                entry.Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Токен уже освобождён — состояние всё равно зафиксировано.
            }

            Failed?.Invoke(entry);
        }

        if (toCancel.Count > 0)
            RaiseChanged();
    }

    /// <summary>Удаляет завершённые записи из реестра (очистка индикатора).</summary>
    public void ClearFinished()
    {
        lock (_lock)
        {
            var finished = _downloads.Values.Where(d => !d.IsActive).ToList();
            foreach (var entry in finished)
                _downloads.Remove(entry.Id);
        }

        RaiseChanged();
    }

    private void RaiseChanged()
    {
        var handler = Changed;
        handler?.Invoke();
    }
}
