using System.Collections.Generic;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Резолвер компоновки меню «Утилиты» (issue #374): выбирает эффективную компоновку
/// по настройкам (правка включена/выключена) и собирает «новинки программы» — пункты
/// каталога, добавленные в новых версиях и отсутствующие в сохранённой компоновке.
/// Платформенно-нейтральная логика, общая для построителей WPF и Avalonia.
/// </summary>
public static class UtilitiesMenuResolver
{
    /// <summary>
    /// Возвращает компоновку для построения меню:
    /// <list type="bullet">
    /// <item>правка выключена (по умолчанию) — стандартная компоновка (обратная совместимость);</item>
    /// <item>правка включена и файл компоновки есть — пользовательская компоновка;</item>
    /// <item>правка включена, файла нет — стандартная компоновка (файл создаст редактор).</item>
    /// </list>
    /// </summary>
    public static UtilitiesMenuLayout Resolve(AppSettings? settings, IUtilitiesMenuLayoutStore? store)
    {
        if (settings is null || !settings.UtilitiesMenuEditingEnabled)
            return UtilitiesMenuLayouts.Default();
        var layout = store?.Load();
        return layout ?? UtilitiesMenuLayouts.Default();
    }

    /// <summary>
    /// Собирает «новинки программы»: пункты каталога с <see cref="UtilitiesMenuItem.IsNew"/>,
    /// отсутствующие в компоновке (по <c>ItemId</c>). Такие пункты автоматически попадают
    /// в подменю «Новинки программы» в конце меню, пока пользователь не разместит их
    /// в редакторе структуры.
    /// </summary>
    public static IReadOnlyList<UtilitiesMenuItem> CollectNewItems(UtilitiesMenuLayout layout)
    {
        var present = new HashSet<string>();
        CollectItemIds(layout?.Root, present);
        var result = new List<UtilitiesMenuItem>();
        foreach (var item in UtilitiesMenuCatalog.All)
        {
            if (item.IsNew && !present.Contains(item.Id))
                result.Add(item);
        }
        return result;
    }

    /// <summary>
    /// Гарантирует наличие группы «Новинки программы» в компоновке (issue #4): если
    /// в каталоге есть пункты с признаком «новинка», отсутствующие в компоновке, они
    /// добавляются в группу <see cref="UtilitiesMenuCatalog.GroupNewItems"/>. Группа
    /// создаётся в конце корня, если её нет в компоновке; если она уже есть — используется
    /// её текущее местоположение (пользователь может менять только его). Видимость группы
    /// всегда принудительно включается: скрытие в редакторе не влияет на отображение
    /// при работе.
    /// </summary>
    public static UtilitiesMenuLayout? EnsureNewItemsGroup(UtilitiesMenuLayout? layout)
    {
        if (layout is null)
            return null;
        var newItems = CollectNewItems(layout);
        if (newItems.Count == 0)
            return layout;

        var existing = FindGroup(layout.Root, UtilitiesMenuCatalog.GroupNewItems);
        if (existing is not null)
        {
            // Группа уже есть (пользователь мог её переместить) — добавляем в неё
            // отсутствующие пункты и принудительно включаем видимость.
            existing.Visible = true;
            var present = new HashSet<string>();
            CollectItemIds(existing.Children, present);
            foreach (var item in newItems)
            {
                if (!present.Contains(item.Id))
                    existing.Children.Add(new UtilitiesMenuNode { Kind = "item", ItemId = item.Id });
            }
            return layout;
        }

        // Группы нет — создаём в конце корня с разделителем перед ней (как в меню).
        var group = new UtilitiesMenuNode
        {
            Kind = "group",
            GroupId = UtilitiesMenuCatalog.GroupNewItems,
            Visible = true,
            SeparatorBefore = true,
        };
        foreach (var item in newItems)
            group.Children.Add(new UtilitiesMenuNode { Kind = "item", ItemId = item.Id });
        layout.Root.Add(group);
        return layout;
    }

    /// <summary>Ищет группу с указанным <c>GroupId</c> в дереве (рекурсивно, включая вложенные).</summary>
    private static UtilitiesMenuNode? FindGroup(IEnumerable<UtilitiesMenuNode>? nodes, string groupId)
    {
        if (nodes is null)
            return null;
        foreach (var node in nodes)
        {
            if (node is null)
                continue;
            if (string.Equals(node.Kind, "group", StringComparison.Ordinal) &&
                string.Equals(node.GroupId, groupId, StringComparison.Ordinal))
            {
                return node;
            }
            var found = FindGroup(node.Children, groupId);
            if (found is not null)
                return found;
        }
        return null;
    }

    /// <summary>Собирает все ItemId узлов дерева (включая вложенные группы).</summary>
    public static void CollectItemIds(IEnumerable<UtilitiesMenuNode>? nodes, HashSet<string> target)
    {
        if (nodes is null || target is null)
            return;
        foreach (var node in nodes)
        {
            if (node is null)
                continue;
            if (string.Equals(node.Kind, "item", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(node.ItemId))
            {
                target.Add(node.ItemId!);
            }
            CollectItemIds(node.Children, target);
        }
    }
}