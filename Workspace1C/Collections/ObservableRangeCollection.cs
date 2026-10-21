using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Workspace1C.Collections;

/// <summary>
/// <see cref="ObservableCollection{T}"/> с массовыми операциями, порождающими ОДНО
/// событие <see cref="NotifyCollectionChangedAction.Reset"/> вместо тысяч событий
/// на каждый элемент. Используется для <c>Items</c>/<c>FlatItems</c>/<c>GroupNodes</c>
/// при пересборке дерева баз (issue #21): массовое обновление с одним Reset позволяет
/// TreeView не сбрасывать ItemsSource и переиспользовать контейнеры (виртуализация
/// Recycling), а не пересоздавать их на каждое добавление.
/// </summary>
/// <typeparam name="T">Тип элементов коллекции.</typeparam>
public class ObservableRangeCollection<T> : ObservableCollection<T>
{
    public ObservableRangeCollection()
    {
    }

    public ObservableRangeCollection(IEnumerable<T> items)
        : base(items)
    {
    }

    /// <summary>
    /// Заменяет содержимое коллекции переданным набором элементов одним событием
    /// <see cref="NotifyCollectionChangedAction.Reset"/> (без промежуточных событий
    /// на каждый элемент).
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        if (items is null)
            throw new ArgumentNullException(nameof(items));

        var list = items as IList<T> ?? items.ToList();
        Items.Clear();
        foreach (var item in list)
            Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Заменяет содержимое коллекции переданным набором элементов ПОШТУЧНО (issue #21,
    /// Этап A): вместо одного Reset эмитятся только реально изменившиеся позиции
    /// (Add/Remove), а элементы, оставшиеся в коллекции, не трогаются — их контейнеры
    /// в TreeView/ItemsControl не пересоздаются (в Avalonia без виртуализации это
    /// устраняет пересоздание всех 500+ карточек строк на каждое переключение вкладки).
    /// Если состав и порядок совпадают по <see cref="object.ReferenceEquals"/> — событий
    /// не порождается вовсе (повторный клик на вкладку, переход с неизменным составом
    /// корней). Если изменения затрагивают большинство элементов — дешевле один Reset
    /// (<see cref="ReplaceAll"/>), как раньше.
    /// </summary>
    public void ReplaceItemWise(IReadOnlyList<T> next)
    {
        if (next is null)
            throw new ArgumentNullException(nameof(next));

        // Быстрый путь: состав и порядок совпадают — ничего не делаем.
        if (Count == next.Count)
        {
            var same = true;
            for (var i = 0; i < Count; i++)
            {
                if (!ReferenceEquals(Items[i], next[i]))
                {
                    same = false;
                    break;
                }
            }
            if (same)
                return;
        }

        // Сколько текущих элементов останется в новом составе (по ReferenceEquals).
        var nextSet = new HashSet<T>(next, ReferenceComparer<T>.Instance);
        var kept = 0;
        for (var i = 0; i < Count; i++)
        {
            if (nextSet.Contains(Items[i]))
                kept++;
        }

        // Reset (ReplaceAll) — ТОЛЬКО когда не сохраняется ни одного элемента (issue #21):
        // при переключении вкладок «Все»↔«Недавние» составы пересекаются (недавние базы
        // есть и в «Все»), и прежний порог «меняется большинство» (kept*2 < total) уводил
        // в полный Reset — WPF пересоздавал контейнеры всех строк, а память росла с каждым
        // переключением. Поштучные события при частичном пересечении переиспользуют
        // контейнеры оставшихся строк (виртуализация Recycling), что и было целью
        // исправлений 1.0.0.26–1.0.0.29.
        var total = Math.Max(Count, next.Count);
        if (total > 0 && kept == 0)
        {
            ReplaceAll(next);
            return;
        }

        // 1) Удаляем элементы, которых нет в новом составе (с конца, чтобы индексы не сбивались).
        for (var i = Count - 1; i >= 0; i--)
        {
            if (!nextSet.Contains(Items[i]))
                RemoveAt(i);
        }

        // 2) Выравниваем порядок и добавляем недостающие: для каждой позиции next[i]
        //    элемент должен стоять на i. Уже стоящие на месте не трогаем.
        for (var i = 0; i < next.Count; i++)
        {
            var wanted = next[i];
            if (i < Count && ReferenceEquals(Items[i], wanted))
                continue;

            var existing = IndexOfReference(wanted, i);
            if (existing >= 0)
            {
                // Элемент есть, но не на своём месте — переносим (Remove + Add).
                RemoveAt(existing);
                Insert(i, wanted);
            }
            else
            {
                Insert(i, wanted);
            }
        }
    }

    /// <summary>Ищет элемент по <see cref="object.ReferenceEquals"/> начиная с указанной позиции.</summary>
    private int IndexOfReference(T item, int startIndex)
    {
        for (var i = startIndex; i < Count; i++)
        {
            if (ReferenceEquals(Items[i], item))
                return i;
        }
        return -1;
    }

    /// <summary>Компаратор по ссылке для <see cref="HashSet{T}"/> (не зависит от Equals/GetHashCode элемента).</summary>
    private sealed class ReferenceComparer<TItem> : IEqualityComparer<TItem>
    {
        public static readonly ReferenceComparer<TItem> Instance = new();

        public bool Equals(TItem? x, TItem? y) => ReferenceEquals(x, y);

        public int GetHashCode(TItem obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    /// <summary>
    /// Добавляет диапазон элементов одним событием
    /// <see cref="NotifyCollectionChangedAction.Add"/>.
    /// </summary>
    public void AddRange(IEnumerable<T> items)
    {
        if (items is null)
            throw new ArgumentNullException(nameof(items));

        var list = items as IList<T> ?? items.ToList();
        if (list.Count == 0)
            return;

        var startIndex = Count;
        foreach (var item in list)
            Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, (IList)list, startIndex));
    }
}