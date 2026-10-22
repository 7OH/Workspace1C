using System.ComponentModel;
using System.Runtime.CompilerServices;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Строка чеклиста окна «Импорт из кластера 1С» (0.3.9.173, цикл 0.3.9.172–0.3.9.175):
/// информационная база кластера с флажком импорта. Чистая модель без платформенных
/// зависимостей — обе платформы (WPF и Avalonia); UI только привязывается.
/// Дубликаты уже существующих в списке баз приходят со снятым флажком и причиной
/// пропуска; повторный чек не приводит к импорту (фильтр выполняется на этапе
/// «Импортировать», см. <see cref="ClusterImportViewModel"/>). Файловых баз в кластере
/// не бывает (комментарий 7OH от 2026-10-18 20:24).
/// С версии 1.0.0.51 (issue #41) строка несёт и исходную запись rac
/// (<see cref="Source"/>) — она нужна миграции баз между кластерами (идентификатор
/// базы для удаления и параметры создания в новом кластере).
/// </summary>
public sealed class ClusterImportRow : INotifyPropertyChanged
{
    private bool _isChecked;

    public ClusterImportRow(
        string name,
        string subtitle,
        string clusterName,
        string connectionString,
        bool isChecked,
        bool isDuplicate,
        string? skipReason,
        Infobase tag,
        RacInfobaseSummary source)
    {
        Name = name;
        Subtitle = subtitle;
        ClusterName = clusterName;
        ConnectionString = connectionString;
        IsDuplicate = isDuplicate;
        SkipReason = skipReason;
        Tag = tag;
        Source = source;
        _isChecked = isChecked;
    }

    /// <summary>Имя информационной базы (совпадает с <c>Ref</c> строки подключения).</summary>
    public string Name { get; }

    /// <summary>Подпись строки: описание / тип СУБД и сервер / имя БД в СУБД.</summary>
    public string Subtitle { get; }

    /// <summary>Имя кластера, из которого импортируется база (колонка «Кластер»).</summary>
    public string ClusterName { get; }

    /// <summary>Строка подключения базы 1С (<c>Srvr="host[:port]";Ref="name"</c>) — колонка «Подключение».</summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Признак дубликата: база уже присутствует в списке приложения (совпадение по
    /// строке подключения, <see cref="RacInfobaseMapper.IsDuplicate"/>).
    /// </summary>
    public bool IsDuplicate { get; }

    /// <summary>
    /// Причина, по которой база не импортируется: «уже есть в списке»
    /// (ключ <c>ClusterImport.AlreadyExists</c>); null — базу можно импортировать.
    /// </summary>
    public string? SkipReason { get; }

    /// <summary>Смапленная база списка приложения (результат импорта для MainViewModel).</summary>
    public Infobase Tag { get; }

    /// <summary>
    /// Исходная запись базы кластера («infobase summary list», issue #41): нужна миграции
    /// — идентификатор базы для удаления из старого кластера и параметры создания
    /// в новом (см. <see cref="ClusterMigrationService.BuildCreateRequest"/>).
    /// </summary>
    public RacInfobaseSummary Source { get; }

    /// <summary>Отмечена ли строка для импорта.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked != value)
            {
                _isChecked = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName!));
}