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