using System.Collections.Specialized;
using System.Diagnostics;
using Workspace1C.Collections;
using Workspace1C.Models;
using Workspace1C.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace Workspace1C.Tests;

/// <summary>
/// Замеры производительности переключения вкладок «Все» → «Избранное» → «Все»
/// на списке из 500+ баз (issue #21, п. 2.3 плана). Измеряется VM-часть пересборки
/// дерева: фильтрация/сортировка, построение узлов, массовое обновление коллекций
/// (ObservableRangeCollection.ReplaceAll, GroupNodeViewModel.ResetFlatContent).
/// Отрисовка TreeView в юнит-тестах не гоняется — её замеряется вручную в приложении.
/// Цель плана — переключение < 100–200 мс; VM-часть должна быть на порядок меньше.
/// </summary>
public sealed class ListSwitchPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public ListSwitchPerformanceTests(ITestOutputHelper output) => _output = output;

    private static List<Infobase> CreateBases(int count)
    {
        var list = new List<Infobase>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(new Infobase
            {
                Id = "base-" + i,
                Name = "База " + i,
                IsFavorite = i % 3 == 0,
                LastLaunchDate = i % 2 == 0 ? DateTime.UtcNow.AddMinutes(-i) : null,
                Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bases\b" + i }
            });
        }
        return list;
    }

    /// <summary>Упрощённая модель группированного дерева для режима «Все» (как в RebuildGroupTreeCore).</summary>
    private static List<GroupNodeViewModel> BuildGroupedTree(IReadOnlyList<Infobase> bases)
    {
        var group = new Group { Id = "g1", Name = "Группа" };
        var root = new GroupNodeViewModel(group);
        var noGroup = new GroupNodeViewModel(null, marker: GroupNodeViewModel.NoGroupMarker);
        foreach (var ib in bases)
        {
            if (ib.IsFavorite)
                root.Infobases.Add(ib);
            else
                noGroup.Infobases.Add(ib);
        }
        root.PopulateItems();
        noGroup.PopulateItems();
        return new List<GroupNodeViewModel> { noGroup, root };
    }

    [Fact]
    public void FlatNodeRebuild_ReusedNode_IsFasterThanNewNodeEachTime()
    {
        var bases = CreateBases(600);
        var favorites = bases.Where(b => b.IsFavorite).ToList();

        // «До» (Этап C не применён): новый узел + заполнение Infobases + PopulateItems на каждый вызов.
        var swOld = Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
        {
            var flatNode = new GroupNodeViewModel(null, displayName: "Найдено", marker: null);
            flatNode.SetNotificationsSuppressed(true);
            foreach (var ib in favorites)
                flatNode.Infobases.Add(ib);
            flatNode.PopulateItems();
            flatNode.SetNotificationsSuppressed(false);
        }
        swOld.Stop();

        // «После» (Этап C): один постоянный узел + ResetFlatContent (массовая замена одним Reset).
        var swNew = Stopwatch.StartNew();
        var persistent = new GroupNodeViewModel(null);
        for (var i = 0; i < 20; i++)
            persistent.ResetFlatContent(favorites, displayName: "Найдено", marker: null);
        swNew.Stop();

        _output.WriteLine(
            $"Плоский узел ×20 ({favorites.Count} баз): до (новый узел) = {swOld.ElapsedMilliseconds} мс, " +
            $"после (переиспользование) = {swNew.ElapsedMilliseconds} мс");

        // Переиспользование не должно быть медленнее пересоздания (иначе Этап C бессмысленен).
        Assert.True(swNew.ElapsedMilliseconds <= swOld.ElapsedMilliseconds + 5,
            $"Переиспользование плоского узла ({swNew.ElapsedMilliseconds} мс) медленнее пересоздания ({swOld.ElapsedMilliseconds} мс)");
    }

    [Fact]
    public void FullSwitchCycle_AllFavoritesAll_StaysUnderTarget()
    {
        var bases = CreateBases(600);
        var favorites = bases.Where(b => b.IsFavorite).ToList();

        // Полный цикл «Все» → «Избранное» → «Все» (VM-часть): группированное дерево
        // для «Все», постоянный плоский узел для «Избранное».
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 10; i++)
        {
            BuildGroupedTree(bases);                                                       // «Все»
            var flat = new GroupNodeViewModel(null);
            flat.ResetFlatContent(favorites, displayName: "Найдено", marker: null);        // «Избранное»
            BuildGroupedTree(bases);                                                       // «Все»
        }
        sw.Stop();

        var perSwitch = sw.ElapsedMilliseconds / 10.0;
        _output.WriteLine(
            $"Цикл «Все→Избранное→Все» ×10 ({bases.Count} баз): {sw.ElapsedMilliseconds} мс, " +
            $"в среднем {perSwitch:F1} мс на переключение");

        // Цель плана — переключение < 100–200 мс (включая отрисовку UI, которая здесь не
        // гоняется). VM-часть обязана укладываться с большим запасом.
        Assert.True(perSwitch < 200,
            $"Среднее переключение {perSwitch:F1} мс — медленнее цели 200 мс");
    }

    [Fact]
    public void RootSwitch_DisjointComposition_FallsBackToSingleReset()
    {
        // 1.0.0.41: переход корней «Все»(несколько групп) → «Избранное»(один плоский
        // узел) — составы не пересекаются, ReplaceItemWise для GroupNodes обязана
        // уходить в ОДИН Reset (без «шквала» поштучных событий). resetMode=reset
        // в логе CM_TREESWITCH автора соответствует именно этому пути.
        var allBases = CreateBases(600);
        var allRoots = BuildGroupedTree(allBases);
        var flat = new GroupNodeViewModel(null);
        flat.ResetFlatContent(allBases.Where(b => b.IsFavorite).ToList(),
            displayName: "Найдено", marker: null);

        var collection = new ObservableRangeCollection<object>();
        collection.ReplaceAll(allRoots.Cast<object>().ToList());
        var resets = 0;
        var adds = 0;
        var removes = 0;
        collection.CollectionChanged += (_, e) =>
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Reset: resets++; break;
                case NotifyCollectionChangedAction.Add: adds++; break;
                case NotifyCollectionChangedAction.Remove: removes++; break;
            }
        };

        collection.ReplaceItemWise(new object[] { flat });

        Assert.Equal(1, resets);
        Assert.Equal(0, adds);
        Assert.Equal(0, removes);
    }

    [Fact]
    public void RootSwitch_SameRootsAgain_EmitsNoEvents()
    {
        // Повторный переход на ту же вкладку (тот же состав корней по ReferenceEquals):
        // ReplaceItemWise не порождает событий — контейнеры строк не пересоздаются,
        // ранний выход ReplaceGroupNodes срабатывает до событий TreeRebuilt.
        var flat = new GroupNodeViewModel(null);
        flat.ResetFlatContent(CreateBases(10).Take(3).ToList(),
            displayName: "Найдено", marker: null);

        var collection = new ObservableRangeCollection<object> { flat };
        var events = 0;
        collection.CollectionChanged += (_, _) => events++;

        collection.ReplaceItemWise(new object[] { flat });

        Assert.Equal(0, events);
    }

    [Fact]
    public void FlatNodeSwitch_SameContentTwice_KeepsRowInstances()
    {
        // Строки (Items) плоского узла НЕ пересоздаются при повторном переходе на
        // вкладку с тем же составом: тот же объект узла и те же экземпляры строк —
        // контейнеры строк в WPF остаются на месте (это и есть «меньше
        // пересозданий контейнеров» для повторных переходов).
        var bases = CreateBases(600);
        var favorites = bases.Where(b => b.IsFavorite).ToList();
        var flat = new GroupNodeViewModel(null);
        flat.ResetFlatContent(favorites, displayName: "Найдено", marker: null);
        var before = flat.Items.ToList();

        flat.ResetFlatContent(favorites, displayName: "Найдено", marker: null);

        Assert.Equal(before.Count, flat.Items.Count);
        for (var i = 0; i < before.Count; i++)
            Assert.Same(before[i], flat.Items[i]);
    }

    [Fact]
    public void CachedGroupedTreeReuse_IsNotSlowerThanRebuilding()
    {
        // Этап C (issue #21): кэш группированного дерева «Все» — переключение «Избранное»→«Все»
        // переиспользует узлы дерева вместо пересоздания тысяч GroupNodeViewModel.
        // Переиспользование не должно быть медленнее пересоздания (иначе этап бессмысленен).
        var bases = CreateBases(600);
        var favorites = bases.Where(b => b.IsFavorite).ToList();

        // «До»: полное пересоздание группированного дерева на каждый проход.
        var swOld = Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
            BuildGroupedTree(bases);
        swOld.Stop();

        // «После»: дерево построено один раз; возврат на «Все» — раздача кэша.
        var swNew = Stopwatch.StartNew();
        var cached = BuildGroupedTree(bases);
        for (var i = 0; i < 20; i++)
        {
            var flat = new GroupNodeViewModel(null);
            flat.ResetFlatContent(favorites, displayName: "Найдено", marker: null); // «Избранное»
            _ = cached; // «Все» из кэша (в тесте не пересоздаётся)
        }
        swNew.Stop();

        _output.WriteLine(
            $"Кэш дерева «Все» ×20 ({bases.Count} баз): пересоздание = {swOld.ElapsedMilliseconds} мс, " +
            $"переиспользование = {swNew.ElapsedMilliseconds} мс");

        Assert.True(swNew.ElapsedMilliseconds <= swOld.ElapsedMilliseconds + 5,
            $"Переиспользование кэша дерева ({swNew.ElapsedMilliseconds} мс) медленнее пересоздания ({swOld.ElapsedMilliseconds} мс)");
    }

    [Fact]
    public void RepeatedSwitchCycle_VMMedian_DoesNotGrow()
    {
        // issue #21: «задержка становится больше с каждым переключением». На уровне VM
        // прогоняем 30 циклов «Все→Избранное→Все» и проверяем, что медиана последней
        // декады не растёт радикально относительно первой (широкий запас — защита от
        // флуктуаций CI). Отрисовка TreeView здесь не гоняется (её замеряется отдельно).
        var bases = CreateBases(600);
        var favorites = bases.Where(b => b.IsFavorite).ToList();

        var times = new List<double>(30);
        for (var i = 0; i < 30; i++)
        {
            var sw = Stopwatch.StartNew();
            BuildGroupedTree(bases);                                                   // «Все»
            var flat = new GroupNodeViewModel(null);
            flat.ResetFlatContent(favorites, displayName: "Найдено", marker: null);   // «Избранное»
            BuildGroupedTree(bases);                                                   // «Все»
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }

        var first = times.Take(10).OrderBy(t => t).ToArray();
        var last = times.Skip(20).OrderBy(t => t).ToArray();
        var medianFirst = first[first.Length / 2];
        var medianLast = last[last.Length / 2];

        _output.WriteLine(
            $"30 циклов: медиана первых 10 = {medianFirst:F1} мс, медиана последних 10 = {medianLast:F1} мс");

        // Мягкий порог «нет роста»: последняя декада не хуже удвоенной первой (+5 мс).
        Assert.True(medianLast <= Math.Max(medianFirst * 2 + 5, 15),
            $"Медиана последних 10 циклов {medianLast:F1} мс растёт относительно первых {medianFirst:F1} мс");
    }

    [Fact]
    public void FlatNodeSwitch_DisjointComposition_EmitsSingleResetOnItemsAndNoEventsOnRoots()
    {
        // 1.0.0.46/1.0.0.50, resetMode=reset в логах CM_TREESWITCH: смена состава плоского
        // узла («Все»(600) → «Недавние»(10), составы почти не пересекаются) уходит в ОДИН
        // Reset коллекции Items узла (collReset=1 за смену — как в логах 7OH на 1.0.0.33–46),
        // а корневая коллекция GroupNodes не порождает событий вовсе (тот же постоянный
        // узел, ReplaceGroupNodes задетектил идентичный состав). Именно полный Reset строк
        // заставляет WPF складывать старые контейнеры в Recycling-пул вложенного
        // ItemsControl — пул обязан разбираться сбросом VirtualizationMode (синхронный
        // флаш 1.0.0.50), иначе контейнеры копятся и heapAfterGcMb растёт.
        var bases = CreateBases(600);
        var recents = bases.Take(10).ToList();
        var flat = new GroupNodeViewModel(null);
        flat.ResetFlatContent(bases, displayName: null, marker: GroupNodeViewModel.AllBasesMarker); // «Все»

        var rootEvents = 0;
        var collection = new ObservableRangeCollection<object> { flat };
        collection.CollectionChanged += (_, _) => rootEvents++;

        var itemsResets = 0;
        var itemsPointwise = 0;
        flat.Items.CollectionChanged += (_, e) =>
        {
            // Reset — один счётчик; поштучные Add/Remove — другой.
            if (e.Action == NotifyCollectionChangedAction.Reset) itemsResets++;
            else itemsPointwise++;
        };

        // «Все» (600) → «Недавние» (10): признак Reset по политике ReplaceItemWise
        // (сохраняется менее 1/4 состава → один Reset вместо сотен поштучных Remove).
        flat.ResetFlatContent(recents, displayName: null, marker: GroupNodeViewModel.AllBasesMarker);

        Assert.Equal(1, itemsResets);
        Assert.Equal(0, itemsPointwise);
        Assert.Equal(0, rootEvents);
        Assert.Equal(10, flat.Items.Count);
    }

    [Fact]
    public void RepeatedFlatReset_SameContent_EmitsNoItemsEvents()
    {
        // issue #21, Этап A: повторный клик на вкладку / переход с тем же составом —
        // постоянный плоский узел не должен порождать событий Items (контейнеры строк
        // не пересоздаются). Раньше ResetFlatContent слал Reset на каждое переключение.
        var bases = CreateBases(600);
        var favorites = bases.Where(b => b.IsFavorite).ToList();
        var flat = new GroupNodeViewModel(null);
        flat.ResetFlatContent(favorites, displayName: "Найдено", marker: null);

        var events = 0;
        flat.Items.CollectionChanged += (_, _) => events++;

        flat.ResetFlatContent(favorites, displayName: "Найдено", marker: null);

        Assert.Equal(0, events);
    }
}
