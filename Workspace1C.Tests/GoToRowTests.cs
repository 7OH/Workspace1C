using System;
using System.Collections.Generic;
using Workspace1C.Models;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты сценария «переход к строке» (issue #31): команды «Найти в списке» (Ctrl+T)
/// и переход к закладке (Ctrl+цифра) должны выделять ЦЕЛЕВУЮ строку базы (в её группе
/// во «Все базы») и прокручивать список к ней, а НЕ первую попавшуюся копию
/// в «Закреплённых» (она идёт первой в дереве) — именно об этом сообщал пользователь:
/// «находит строку в Закреплённые и на ней и остаётся».
///
/// Чистые хелперы выбраны осознанно: полный <see cref="MainViewModel"/> для прямого
/// теста слишком нагружен (конструктор, диалоги, диспетчер) — конвенция проекта
/// тестировать селекторы и оркестрацию через чистые статические помощники
/// (см. комментарий в CustomActionExecutionPlanTests).
/// </summary>
public sealed class GoToRowTests
{
    private static Infobase CreateBase(string name = "База") => new()
    {
        Id = "id-" + name,
        Name = name,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bases\demo" }
    };

    /// <summary>
    /// Закреплённая база присутствует в дереве дважды: копия в узле «Закреплённые»
    /// (в начале дерева) и строка в своей группе. Reveal перехода к строке (issue #31)
    /// должен вести на ДОМАШНЮЮ строку в группе — «переход туда, где папку раскрыли»,
    /// а не на копию в «Закреплённых».
    /// </summary>
    [Fact]
    public void FindInfobaseHomeNode_PinnedBaseInGroup_ReturnsHomeGroupNotPinned()
    {
        var target = CreateBase("База 1");
        var pinnedNode = new GroupNodeViewModel(null, marker: GroupNodeViewModel.PinnedMarker);
        pinnedNode.Infobases.Add(target);
        var groupNode = new GroupNodeViewModel(new Group { Id = "g1", Name = "Группа" });
        groupNode.Infobases.Add(target);

        var roots = new List<GroupNodeViewModel> { pinnedNode, groupNode };

        var home = GroupNodeViewModel.FindInfobaseHomeNode(roots, target);

        Assert.NotNull(home);
        Assert.Same(groupNode, home);
    }

    /// <summary>
    /// База без группы показана в служебном узле «Без группы» — переход к строке ведёт
    /// на её строку именно там.
    /// </summary>
    [Fact]
    public void FindInfobaseHomeNode_BaseWithoutGroup_ReturnsNoGroupNode()
    {
        var target = CreateBase("База без группы");
        var noGroupNode = new GroupNodeViewModel(null, marker: GroupNodeViewModel.NoGroupMarker);
        noGroupNode.Infobases.Add(target);

        var roots = new List<GroupNodeViewModel> { noGroupNode };

        var home = GroupNodeViewModel.FindInfobaseHomeNode(roots, target);

        Assert.Same(noGroupNode, home);
    }

    /// <summary>
    /// Цель отсутствует в текущем дереве (например, база не входит в активный режим
    /// вкладки) — домашнего узла нет: reveal не сможет привести к строке и корректно
    /// выходит (проверка отсутствия ложного «перехода в закрепления»).
    /// </summary>
    [Fact]
    public void FindInfobaseHomeNode_TargetMissingFromTree_ReturnsNull()
    {
        var other = CreateBase("Другая база");
        var target = CreateBase("Невидимая база");
        var groupNode = new GroupNodeViewModel(new Group { Id = "g1", Name = "Группа" });
        groupNode.Infobases.Add(other);

        var roots = new List<GroupNodeViewModel> { groupNode };

        var home = GroupNodeViewModel.FindInfobaseHomeNode(roots, target);

        Assert.Null(home);
    }

    /// <summary>
    /// Плоский режим (вкладка «Избранное»/«Недавние», поиск, теги) показывает базы одним
    /// узлом без дублей — домашним узлом базы-в-группе является плоский узел, и переход
    /// к строке не уходит в «Закреплённые» (дубликата в плоском списке просто нет).
    /// </summary>
    [Fact]
    public void FindInfobaseHomeNode_FlatMode_ReturnsFlatNode()
    {
        var target = CreateBase("База 1");
        var flatNode = new GroupNodeViewModel(null, marker: GroupNodeViewModel.AllBasesMarker);
        flatNode.Infobases.Add(target);

        var roots = new List<GroupNodeViewModel> { flatNode };

        var home = GroupNodeViewModel.FindInfobaseHomeNode(roots, target);

        Assert.Same(flatNode, home);
    }

#if !LINUX
    /// <summary>
    /// Второй проход «перехода к строке» (issue #31): NavigateToBookmark раскрывает цепочку
    /// групп-предков и снимает их ключи из набора свёрнутых (ExpandChainToRoot) ДО пересборки,
    /// а после пересборки повторяет проход по новым узлам. Если ключ остался бы в наборе
    /// свёрнутых, ApplyExpandedState внутри RebuildGroupTree свернул бы группу обратно,
    /// контейнер целевой базы не создался бы и выделение осталось бы на «Закреплённых».
    /// </summary>
    [Fact]
    public void NavigateToBookmark_CollapsedAncestorsStayExpandedAfterRebuild()
    {
        // Пользователь свернул цепочку групп (ключи в наборе свёрнутых).
        var top = new GroupNodeViewModel(null, marker: "Top");
        var mid = new GroupNodeViewModel(null, parent: top, marker: "Mid");
        var deep = new GroupNodeViewModel(null, parent: mid, marker: "Deep");
        top.SetExpandedSilent(false);
        mid.SetExpandedSilent(false);
        deep.SetExpandedSilent(false);
        var collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            top.NodeKey,
            mid.NodeKey,
            deep.NodeKey
        };

        // 1-й проход NavigateToBookmark: ExpandChainToRoot снимает ключи свёрнутых предков.
        MainViewModel.ExpandChainToRoot(new[] { top, mid, deep }, collapsed);
        Assert.DoesNotContain(top.NodeKey, collapsed);
        Assert.DoesNotContain(mid.NodeKey, collapsed);
        Assert.DoesNotContain(deep.NodeKey, collapsed);

        // «Пересборка» (RebuildGroupTree): узлы создаются заново; развёрнутость новых узлов
        // определяется набором свёрнутых (!IsGroupCollapsed(key) → развёрнут). Ключи цепочки
        // уже выведены первым проходом — новые узлы развёрнуты, контейнер строки создастся.
        var rebuiltTop = new GroupNodeViewModel(null, marker: "Top");
        var rebuiltMid = new GroupNodeViewModel(null, parent: rebuiltTop, marker: "Mid");
        var rebuiltDeep = new GroupNodeViewModel(null, parent: rebuiltMid, marker: "Deep");
        Assert.True(rebuiltTop.IsExpanded);
        Assert.True(rebuiltMid.IsExpanded);
        Assert.True(rebuiltDeep.IsExpanded);

        // 2-й проход NavigateToBookmark (повторный ExpandPathTo после пересборки) —
        // идемпотентен: ключи уже сняты, узлы остаются развёрнутыми.
        MainViewModel.ExpandChainToRoot(new[] { rebuiltTop, rebuiltMid, rebuiltDeep }, collapsed);
        Assert.True(rebuiltTop.IsExpanded);
        Assert.True(rebuiltMid.IsExpanded);
        Assert.True(rebuiltDeep.IsExpanded);
        Assert.DoesNotContain(rebuiltTop.NodeKey, collapsed);
    }
#endif
}
