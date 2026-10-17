using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #14: основная редакция типовой конфигурации. Вычисляемые свойства
/// (<c>DefaultEdition</c>, <c>EffectiveUrlCode</c>, <c>HasUrlOverride</c>) не пишутся в
/// <c>custom_config_types.json</c>; основная редакция задаётся флагом <c>IsDefault</c> в строке
/// редакции, без флага основной считается первая (обратная совместимость).
/// </summary>
public sealed class DefaultEditionTests : IDisposable
{
    private readonly string _tempDir;

    public DefaultEditionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_default_edition_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Игнорируем: каталог мог быть занят или уже удалён.
        }
    }

    private static OneCConfigType Zup(params OneCConfigEdition[] editions) => new()
    {
        Code = "ZUP_TEST",
        Name = "Зарплата и управление персоналом",
        Nick = "HRM",
        Editions = editions.ToList(),
    };

    [Fact]
    public void DefaultEdition_WithoutFlag_IsFirst()
    {
        var config = Zup(new OneCConfigEdition { Name = "3.0", Red = "3.0" },
            new OneCConfigEdition { Name = "3.1", Red = "3.1" });

        Assert.Equal("3.0", config.DefaultEdition?.Red);
    }

    [Fact]
    public void DefaultEdition_WithFlag_IsFlagged()
    {
        var config = Zup(new OneCConfigEdition { Name = "3.0", Red = "3.0" },
            new OneCConfigEdition { Name = "3.1", Red = "3.1", IsDefault = true });

        Assert.Equal("3.1", config.DefaultEdition?.Red);
    }

    [Fact]
    public void DefaultEdition_NoEditions_IsNull()
    {
        Assert.Null(Zup().DefaultEdition);
    }

    [Fact]
    public void SetDefaultEdition_LeavesSingleFlag()
    {
        var a = new OneCConfigEdition { Red = "3.0", IsDefault = true };
        var b = new OneCConfigEdition { Red = "3.1" };
        var list = new List<OneCConfigEdition> { a, b };

        OneCConfigType.SetDefaultEdition(list, b);

        Assert.False(a.IsDefault);
        Assert.True(b.IsDefault);
    }

    [Fact]
    public void SetDefaultEdition_ForeignEdition_DoesNothing()
    {
        var a = new OneCConfigEdition { Red = "3.0", IsDefault = true };
        var list = new List<OneCConfigEdition> { a };

        OneCConfigType.SetDefaultEdition(list, new OneCConfigEdition { Red = "9.9" });

        Assert.True(a.IsDefault);
    }

    [Fact]
    public void NormalizeDefaultEdition_KeepsFirstFlagOnly()
    {
        var list = new List<OneCConfigEdition>
        {
            new() { Red = "1" },
            new() { Red = "2", IsDefault = true },
            new() { Red = "3", IsDefault = true },
        };

        OneCConfigType.NormalizeDefaultEdition(list);

        Assert.Equal(new[] { false, true, false }, list.Select(e => e.IsDefault).ToArray());
    }

    [Fact]
    public void Save_DoesNotWriteComputedProperties()
    {
        var store = new CustomConfigTypesStore(directoryOverride: _tempDir);
        store.Save(new[]
        {
            Zup(new OneCConfigEdition { Name = "3.0", Red = "3.0", UrlOverride = "https://example/3.0" },
                new OneCConfigEdition { Name = "3.1", Red = "3.1" }),
        });

        var json = File.ReadAllText(store.FilePath);
        Assert.DoesNotContain("DefaultEdition", json);
        Assert.DoesNotContain("EffectiveUrlCode", json);
        Assert.DoesNotContain("HasUrlOverride", json);
        Assert.DoesNotContain("DefaultMark", json);
        // Флаг пишется только когда он установлен.
        Assert.DoesNotContain("IsDefault", json);
    }

    [Fact]
    public void SaveLoad_PreservesDefaultFlag()
    {
        var store = new CustomConfigTypesStore(directoryOverride: _tempDir);
        store.Save(new[]
        {
            Zup(new OneCConfigEdition { Name = "3.0", Red = "3.0" },
                new OneCConfigEdition { Name = "3.1", Red = "3.1", IsDefault = true }),
        });

        var json = File.ReadAllText(store.FilePath);
        Assert.Contains("\"IsDefault\": true", json);

        var loaded = store.Load().Single();
        Assert.Equal("3.1", loaded.DefaultEdition?.Red);
    }

    [Fact]
    public void Load_OldFileWithDefaultEditionBlock_IsReadAndBlockDropped()
    {
        // Файл в формате до issue #14: блок DefaultEdition дублирует первую редакцию.
        var store = new CustomConfigTypesStore(directoryOverride: _tempDir);
        File.WriteAllText(store.FilePath, """
        [
          {
            "Code": "ZUP_TEST",
            "Name": "ЗУП",
            "Nick": "HRM",
            "Editions": [
              { "Name": "3.0", "Red": "3.0", "SubRed": "", "UrlOverride": "", "HasUrlOverride": false },
              { "Name": "3.1", "Red": "3.1", "SubRed": "", "UrlOverride": "", "HasUrlOverride": false }
            ],
            "EffectiveUrlCode": "ЗУП",
            "DefaultEdition": { "Name": "3.0", "Red": "3.0", "SubRed": "", "UrlOverride": "", "HasUrlOverride": false }
          }
        ]
        """);

        var loaded = store.Load().Single();
        Assert.Equal(2, loaded.Editions.Count);
        Assert.Equal("3.0", loaded.DefaultEdition?.Red);

        store.Save(new[] { loaded });
        Assert.DoesNotContain("DefaultEdition", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void Load_SeveralFlags_NormalizedToOne()
    {
        var store = new CustomConfigTypesStore(directoryOverride: _tempDir);
        File.WriteAllText(store.FilePath, """
        [
          {
            "Code": "ZUP_TEST",
            "Name": "ЗУП",
            "Editions": [
              { "Name": "3.0", "Red": "3.0", "IsDefault": true },
              { "Name": "3.1", "Red": "3.1", "IsDefault": true }
            ]
          }
        ]
        """);

        var loaded = store.Load().Single();
        Assert.Single(loaded.Editions, e => e.IsDefault);
        Assert.Equal("3.0", loaded.DefaultEdition?.Red);
    }
}
