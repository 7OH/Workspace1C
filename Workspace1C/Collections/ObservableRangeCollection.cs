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
    /// Заменяет содержимое коллекции переданным набором элементов ПОШТУЧНО либо одним
    /// Reset (issue #21, Этап A + эксперимент A 1.0.0.34): вместо одного Reset эмитятся
    /// только реально изменившиеся позиции (Add/Remove), а элементы, оставшиеся
    /// в коллекции, не трогаются — их контейнеры в TreeView/ItemsControl не пересоздаются
    /// (в Avalonia без виртуализации это устраняет пересоздание всех 500+ карточек строк
    /// на каждое переключение вкладки). Если состав и порядок совпадают по
    /// <see cref="object.ReferenceEquals"/> — событий не порождается вовсе (повторный
    /// клик на вкладку, переход с неизменным составом корней).
    /// </summary>
    /// <remarks>
    /// Политика выбора режима (эксперимент A, issue #21): если сохраняется менее
    /// <paramref name="resetIfKeptTimesLessThanTotal"/>-й доли состава
    /// (<c>kept * resetIfKeptTimesLessThanTotal < total</c>; в том числе совсем ничего —
    /// <c>kept == 0</c>) — выполняется один Reset (<see cref="ReplaceAll"/>): WPF
    /// обрабатывает его одним проходом пересоздания контейнеров, что дешевле «шквала»
    /// из сотен поштучных Remove/Insert при переходе «Все»↔«Недавние». При большей доле
    /// сохраняемых элементов — поштучные события, переиспользующие контейнеры оставшихся
    /// строк (виртуализация Recycling). Итоговый порог фиксируется по парным замерам
    /// `CM_TREESWITCH` на 1.0.0.33 vs 1.0.0.34.
    /// </remarks>
    /// <param name="next">Новый состав коллекции.</param>
    /// <param name="resetIfKeptTimesLessThanTotal">
    /// Порог перехода в режим Reset: если число сохраняемых элементов, умноженное на это
    /// значение, меньше общего размера — выполняется Reset вместо поштучных событий.
    /// По умолчанию 4 (сохраняется менее четверти состава). Значение должно быть больше 0.
    /// </param>
    public void ReplaceItemWise(IReadOnlyList<T> next, int resetIfKeptTimesLessThanTotal = 4)
    {
        if (next is null)
            throw new ArgumentNullException(nameof(next));
        if (resetIfKeptTimesLessThanTotal <= 0)
            throw new ArgumentOutOfRangeException(nameof(resetIfKeptTimesLessThanTotal));

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

        // Reset (ReplaceAll) при МАЛОМ пересечении составов (эксперимент A, 1.0.0.34):
        // сохраняется менее resetIfKeptTimesLessThanTotal-й доли состава (в том числе
        // ничего — kept == 0) → один Reset вместо «шквала» сотен поштучных Remove/Insert
        // (переход «Все»(600)↔«Недавние»(10) давал до ~590 событий за смену, каждое из
        // которых WPF обрабатывал через ItemContainerGenerator). WPF обрабатывает Reset
        // одним проходом пересоздания контейнеров. При большей доле сохраняемых
        // элементов поштучные события переиспользуют контейнеры оставшихся строк
        // (виртуализация Recycling), что сохраняет достигнутое в 1.0.0.26–1.0.0.29
        // поведение для пересекающихся составов. Окончательный порог фиксируется по
        // парным замерам CM_TREESWITCH на 1.0.0.33 vs 1.0.0.34.
        var total = Math.Max(Count, next.Count);
        if (total > 0 && kept * resetIfKeptTimesLessThanTotal < total)
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