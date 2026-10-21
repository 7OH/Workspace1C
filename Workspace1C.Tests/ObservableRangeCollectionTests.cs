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
        // Эксперимент A (1.0.0.34): при малом пересечении составов (kept*4 < total,
        // в том числе совсем ничего — kept == 0) выполняется ОДИН Reset — дешевле
        // сотен поштучных событий. Здесь {a,b,c} → {d,e}: пересечения нет → Reset.
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

    [Fact]
    public void ReplaceItemWise_SmallRecentFromLargeAll_FallsBackToSingleReset_ExperimentA()
    {
        // Эксперимент A (1.0.0.34): переход «Все»(600) → «Недавние»(10 из тех же
        // объектов). Сохраняется 10 из 600 (10*4 = 40 < 600) → выполняется ОДИН
        // Reset вместо ~590 поштучных Remove/Insert («шквала» событий, который WPF
        // обрабатывал через ItemContainerGenerator по одному). Гипотеза эксперимента:
        // Reset обрабатывается WPF одним проходом пересоздания контейнеров без
        // накопления; окончательный порог фиксируется по замерам 1.0.0.33 vs 1.0.0.34.
        var all = Enumerable.Range(0, 600).Select(i => new Item("b" + i)).ToList();
        var recent = all.Take(10).ToList();
        var collection = new ObservableRangeCollection<Item>();
        collection.ReplaceAll(all);

        var resets = 0;
        var adds = 0;
        var removes = 0;
        collection.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resets++;
            else if (e.Action == NotifyCollectionChangedAction.Add) adds++;
            else if (e.Action == NotifyCollectionChangedAction.Remove) removes++;
        };

        collection.ReplaceItemWise(recent);

        Assert.Equal(recent, collection);
        Assert.Equal(1, resets);
        Assert.Equal(0, adds);
        Assert.Equal(0, removes);
    }

    // ============ Эксперимент A (1.0.0.34): порог Reset при малом пересечении ============

    [Fact]
    public void ReplaceItemWise_KeptTimes4EqualsTotal_Itemwise_NoReset()
    {
        // Граница порога: kept*4 == total — Reset НЕ выполняется (строгое '<'),
        // коллекция обновляется поштучно (сохраняется 1 из 4: 1*4 == 4).
        var a = new Item("a");
        var b = new Item("b");
        var c = new Item("c");
        var d = new Item("d");
        var collection = new ObservableRangeCollection<Item> { a, b, c, d };
        var resets = 0;
        var adds = 0;
        var removes = 0;
        collection.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resets++;
            else if (e.Action == NotifyCollectionChangedAction.Add) adds++;
            else if (e.Action == NotifyCollectionChangedAction.Remove) removes++;
        };

        collection.ReplaceItemWise(new[] { d, new Item("e"), new Item("f"), new Item("g") });

        Assert.Equal(4, collection.Count);
        Assert.Equal(0, resets);
        Assert.Equal(3, adds);
        Assert.Equal(3, removes);
    }

    [Fact]
    public void ReplaceItemWise_KeptTimes4LessTotal_SingleReset()
    {
        // kept*4 < total — один Reset (эксперимент A): сохраняется 1 из 5 (1*4 = 4 < 5).
        var a = new Item("a");
        var b = new Item("b");
        var c = new Item("c");
        var d = new Item("d");
        var e = new Item("e");
        var collection = new ObservableRangeCollection<Item> { a, b, c, d, e };
        var resets = 0;
        var otherActions = 0;
        collection.CollectionChanged += (_, ev) =>
        {
            if (ev.Action == NotifyCollectionChangedAction.Reset) resets++;
            else otherActions++;
        };

        collection.ReplaceItemWise(new[] { e, new Item("f"), new Item("g"), new Item("h"), new Item("i") });

        Assert.Equal(5, collection.Count);
        Assert.Equal(1, resets);
        Assert.Equal(0, otherActions);
    }

    [Fact]
    public void ReplaceItemWise_CustomResetThreshold_RespectsParameter()
    {
        // Порог конфигурируемый (эксперимент A): с параметром 4 (по умолчанию)
        // сохраняется 1 из 6 → Reset; с параметром 10 (1*10 >= 6) → поштучно.
        var all = Enumerable.Range(0, 6).Select(i => new Item("b" + i)).ToList();

        var c1 = new ObservableRangeCollection<Item>();
        c1.ReplaceAll(all);
        var resets1 = 0;
        c1.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resets1++;
        };
        c1.ReplaceItemWise(new[] { all[0] });
        Assert.Equal(1, resets1);

        var c2 = new ObservableRangeCollection<Item>();
        c2.ReplaceAll(all);
        var resets2 = 0;
        var otherActions2 = 0;
        c2.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resets2++;
            else otherActions2++;
        };
        c2.ReplaceItemWise(new[] { all[0] }, resetIfKeptTimesLessThanTotal: 10);
        Assert.Equal(0, resets2);
        Assert.True(otherActions2 > 0);
    }

    [Fact]
    public void ReplaceItemWise_InvalidThreshold_Throws()
    {
        var collection = new ObservableRangeCollection<int> { 1 };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => collection.ReplaceItemWise(new[] { 2 }, resetIfKeptTimesLessThanTotal: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => collection.ReplaceItemWise(new[] { 2 }, resetIfKeptTimesLessThanTotal: -1));
    }
}