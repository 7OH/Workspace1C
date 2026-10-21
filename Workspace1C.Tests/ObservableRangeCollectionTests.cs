using System.Collections.Specialized;
using Workspace1C.Collections;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты <see cref="ObservableRangeCollection{T}"/> (issue #21): массовые операции
/// порождают ОДНО событие <see cref="NotifyCollectionChangedAction.Reset"/>, а не
/// тысячи событий на каждый элемент — это ключ к ускорению пересборки дерева.
/// </summary>
public sealed class ObservableRangeCollectionTests
{
    [Fact]
    public void ReplaceAll_ReplacesContent()
    {
        var collection = new ObservableRangeCollection<int> { 1, 2, 3 };

        collection.ReplaceAll(new[] { 4, 5 });

        Assert.Equal(new[] { 4, 5 }, collection);
    }

    [Fact]
    public void ReplaceAll_RaisesSingleReset()
    {
        var collection = new ObservableRangeCollection<int> { 1, 2, 3 };
        var resets = 0;
        var otherActions = 0;
        collection.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
                resets++;
            else
                otherActions++;
        };

        collection.ReplaceAll(Enumerable.Range(0, 1000));

        Assert.Equal(1, resets);
        Assert.Equal(0, otherActions);
    }

    [Fact]
    public void ReplaceAll_EmptySource_RaisesSingleReset()
    {
        var collection = new ObservableRangeCollection<int> { 1, 2, 3 };
        var resets = 0;
        collection.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
                resets++;
        };

        collection.ReplaceAll(Array.Empty<int>());

        Assert.Empty(collection);
        Assert.Equal(1, resets);
    }

    [Fact]
    public void AddRange_AddsAllItemsWithSingleAddEvent()
    {
        var collection = new ObservableRangeCollection<int>();
        var addEvents = 0;
        var resets = 0;
        collection.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
                addEvents++;
            else if (e.Action == NotifyCollectionChangedAction.Reset)
                resets++;
        };

        collection.AddRange(new[] { 1, 2, 3 });

        Assert.Equal(new[] { 1, 2, 3 }, collection);
        Assert.Equal(1, addEvents);
        Assert.Equal(0, resets);
    }

    [Fact]
    public void AddRange_EmptySource_NoEvents()
    {
        var collection = new ObservableRangeCollection<int>();
        var events = 0;
        collection.CollectionChanged += (_, _) => events++;

        collection.AddRange(Array.Empty<int>());

        Assert.Empty(collection);
        Assert.Equal(0, events);
    }

    [Fact]
    public void InheritsObservableCollection_StandardOperationsStillWork()
    {
        var collection = new ObservableRangeCollection<string> { "a" };

        collection.Add("b");
        collection.Remove("a");
        collection.Insert(0, "c");

        Assert.Equal(new[] { "c", "b" }, collection);
        Assert.Equal(2, collection.Count);
    }

    [Fact]
    public void ReplaceAll_ManyItems_EmitsSingleReset_NotPerItem()
    {
        // issue #21: массовая замена содержимого (используется PopulateItems/FlatItems/
        // GroupNodes при переключении вкладок) должна порождать ОДНО событие Reset,
        // а не Add/Remove на каждый элемент — иначе TreeView получает тысячи событий.
        var collection = new ObservableRangeCollection<int> { 1, 2, 3 };
        var resets = 0;
        var adds = 0;
        var removes = 0;
        collection.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resets++;
            else if (e.Action == NotifyCollectionChangedAction.Add) adds++;
            else if (e.Action == NotifyCollectionChangedAction.Remove) removes++;
        };

        var items = Enumerable.Range(0, 600).ToList();
        collection.ReplaceAll(items);

        Assert.Equal(600, collection.Count);
        Assert.Equal(1, resets);
        Assert.Equal(0, adds);
        Assert.Equal(0, removes);
    }

    // ======================= ReplaceItemWise (issue #21, Этап A) =======================

    private sealed class Item
    {
        public Item(string name) => Name = name;
        public string Name { get; }
    }

    [Fact]
    public void ReplaceItemWise_SameContentAndOrder_NoEvents()
    {
        // Повторный клик на вкладку / переход с неизменным составом корней: коллекция
        // не должна порождать НИ ОДНОГО события — контейнеры строк не пересоздаются.
        var a = new Item("a");
        var b = new Item("b");
        var c = new Item("c");
        var collection = new ObservableRangeCollection<Item> { a, b, c };
        var events = 0;
        collection.CollectionChanged += (_, _) => events++;

        collection.ReplaceItemWise(new[] { a, b, c });

        Assert.Equal(0, events);
        Assert.Equal(new[] { a, b, c }, collection);
    }

    [Fact]
    public void ReplaceItemWise_PartialOverlap_EmitsOnlyChanges_NoReset()
    {
        // Переход с частичным пересечением: события только по реально изменившимся
        // позициям (Add/Remove), без Reset — контейнеры оставшихся строк сохраняются.
        var a = new Item("a");
        var b = new Item("b");
        var c = new Item("c");
        var d = new Item("d");
        var collection = new ObservableRangeCollection<Item> { a, b, c };
        var resets = 0;
        var adds = 0;
        var removes = 0;
        collection.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resets++;
            else if (e.Action == NotifyCollectionChangedAction.Add) adds++;
            else if (e.Action == NotifyCollectionChangedAction.Remove) removes++;
        };

        // «Все» (a,b,c) → «Недавние» (a,b,d): c удаляется, d добавляется.
        collection.ReplaceItemWise(new[] { a, b, d });

        Assert.Equal(new[] { a, b, d }, collection);
        Assert.Equal(0, resets);
        Assert.Equal(1, adds);
        Assert.Equal(1, removes);
    }

    [Fact]
    public void ReplaceItemWise_Reorders_EmitsRemoveAndAdd_NoReset()
    {
        // Перестановка порядка: элементы переносятся (Remove + Add), Reset не шлётся.
        var a = new Item("a");
        var b = new Item("b");
        var c = new Item("c");
        var collection = new ObservableRangeCollection<Item> { a, b, c };
        var resets = 0;
        var adds = 0;
        var removes = 0;
        collection.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resets++;
            else if (e.Action == NotifyCollectionChangedAction.Add) adds++;
            else if (e.Action == NotifyCollectionChangedAction.Remove) removes++;
        };

        collection.ReplaceItemWise(new[] { c, a, b });

        Assert.Equal(new[] { c, a, b }, collection);
        Assert.Equal(0, resets);
        Assert.True(adds > 0);
        Assert.True(removes > 0);
    }

    [Fact]
    public void ReplaceItemWise_MajorityChanged_FallsBackToSingleReset()
    {
        // Если меняется большинство элементов — один Reset дешевле сотен поштучных
        // событий (fallback на ReplaceAll), поведение прежнее.
        var a = new Item("a");
        var b = new Item("b");
        var c = new Item("c");
        var d = new Item("d");
        var e = new Item("e");
        var collection = new ObservableRangeCollection<Item> { a, b, c };
        var resets = 0;
        var adds = 0;
        collection.CollectionChanged += (_, ev) =>
        {
            if (ev.Action == NotifyCollectionChangedAction.Reset) resets++;
            else if (ev.Action == NotifyCollectionChangedAction.Add) adds++;
        };

        collection.ReplaceItemWise(new[] { d, e });

        Assert.Equal(new[] { d, e }, collection);
        Assert.Equal(1, resets);
        Assert.Equal(0, adds);
    }

    [Fact]
    public void ReplaceItemWise_On600Bases_EmitsFewerEventsThanListSize()
    {
        // Переход «Все»(600) → «Недавние»(500 из тех же объектов): сохраняется 500 строк,
        // пересоздаются только 100 удалённых + 100 добавленных — событий ~200, а не 600.
        // Это и есть устранение «пересоздания всех контейнеров на каждое переключение».
        var all = Enumerable.Range(0, 600).Select(i => new Item("b" + i)).ToList();
        var recent = all.Take(500).ToList();
        var collection = new ObservableRangeCollection<Item>();
        collection.ReplaceAll(all);

        var events = 0;
        var resets = 0;
        collection.CollectionChanged += (_, e) =>
        {
            events++;
            if (e.Action == NotifyCollectionChangedAction.Reset)
                resets++;
        };

        collection.ReplaceItemWise(recent);

        Assert.Equal(recent, collection);
        Assert.Equal(0, resets);
        Assert.True(events < 600,
            $"Ожидалось меньше 600 событий при частичном пересечении, получено {events}");
    }
}