using System.Diagnostics;
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
}
