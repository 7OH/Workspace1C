using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты унификации операций импорта/экспорта *.v8i между «Настройками» (вкладка
/// «Базы» → «Список баз») и меню «Утилиты» (issue #29): меню «Утилиты» содержит те же
/// пункты «Экспорт в файл *.v8i…» и «Импорт из файла *.v8i…», что и настройки, с теми же
/// названиями (одни ключи локализации) и теми же видами команд, которые резолвятся
/// в единые методы/команды MainViewModel.
/// </summary>
public sealed class UtilitiesMenuV8iUnificationTests
{
    /// <summary>Каталог меню «Утилиты» содержит пункты экспорта/импорта *.v8i (issue #29).</summary>
    [Fact]
    public void Catalog_ContainsV8iExportImportItems()
    {
        var export = UtilitiesMenuCatalog.Find("ExportV8iFile");
        var import = UtilitiesMenuCatalog.Find("ImportV8iFile");

        Assert.NotNull(export);
        Assert.NotNull(import);
        Assert.Equal(UtilitiesMenuCommandKind.ExportBasesV8iFile, export!.CommandKind);
        Assert.Equal(UtilitiesMenuCommandKind.ImportBasesV8iFile, import!.CommandKind);
    }

    /// <summary>Пункты *.v8i используют те же ключи локализации, что и кнопки настроек, —
    /// названия в обоих местах идентичны (issue #29).</summary>
    [Fact]
    public void V8iItems_UseSameLocalizationKeysAsSettings()
    {
        var export = UtilitiesMenuCatalog.Find("ExportV8iFile");
        var import = UtilitiesMenuCatalog.Find("ImportV8iFile");

        Assert.NotNull(export);
        Assert.NotNull(import);
        // Те же ключи, что у кнопок «Экспорт в файл *.v8i…» / «Импорт из файла *.v8i…»
        // в настройках (вкладка «Базы» → «Список баз»).
        Assert.Equal("Settings.Bases.ExportV8iFile", export!.TextKey);
        Assert.Equal("Settings.Bases.ImportV8iFile", import!.TextKey);
    }

    /// <summary>Стандартная компоновка меню «Утилиты» включает пункты *.v8i в группе «Операции».</summary>
    [Fact]
    public void DefaultLayout_IncludesV8iItemsInOperationsGroup()
    {
        var layout = UtilitiesMenuLayouts.Default();
        var ids = new System.Collections.Generic.HashSet<string>();
        UtilitiesMenuResolver.CollectItemIds(layout.Root, ids);

        Assert.Contains("ExportV8iFile", ids);
        Assert.Contains("ImportV8iFile", ids);

        // Пункты находятся в подменю «Операции» (как и остальные операции экспорта/импорта).
        var operations = layout.Root.FirstOrDefault(n =>
            string.Equals(n.Kind, "group", System.StringComparison.Ordinal) &&
            string.Equals(n.GroupId, UtilitiesMenuCatalog.GroupOperations, System.StringComparison.Ordinal));
        Assert.NotNull(operations);
        var opsIds = new System.Collections.Generic.HashSet<string>();
        UtilitiesMenuResolver.CollectItemIds(operations!.Children, opsIds);
        Assert.Contains("ExportV8iFile", opsIds);
        Assert.Contains("ImportV8iFile", opsIds);
    }

    /// <summary>Пункты *.v8i не помечены как «новинки» — они сразу в стандартной компоновке.</summary>
    [Fact]
    public void V8iItems_AreNotMarkedAsNew()
    {
        var export = UtilitiesMenuCatalog.Find("ExportV8iFile");
        var import = UtilitiesMenuCatalog.Find("ImportV8iFile");

        Assert.NotNull(export);
        Assert.NotNull(import);
        Assert.False(export!.IsNew);
        Assert.False(import!.IsNew);
    }
}
