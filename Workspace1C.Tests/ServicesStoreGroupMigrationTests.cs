using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты миграции хранилища служб на объектный формат (issue #11, итерация 2 —
/// группы служб, релиз 1.0.0.12): чтение старого формата (массив строк) даёт
/// службы без групп; сохранение пишет объектный формат и читается обратно;
/// нормализация (дубликаты/пустые/порядок); битый/пустой файл → пустой объект
/// без исключений.
/// </summary>
public sealed class ServicesStoreGroupMigrationTests
{
    [Fact]
    public void Load_LegacyArray_MigratesToServicesWithoutGroups()
    {
        using var tmp = TempDir();
        Directory.CreateDirectory(tmp.Path);
        var store = new ServicesStore(directoryOverride: tmp.Path);
        File.WriteAllText(store.FilePath, "[\"srv-a\", \"srv-b\"]");

        var data = store.Load();

        Assert.Equal(new[] { "srv-a", "srv-b" }, data.Services);
        Assert.Empty(data.Groups);
    }

    [Fact]
    public void Save_WritesObjectFormat_AndReadsBack()
    {
        using var tmp = TempDir();
        var store = new ServicesStore(directoryOverride: tmp.Path);
        var data = new ServiceStoreData
        {
            Services = new[] { "srv-a" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Группа 1", Services = new[] { "srv-b", "srv-c" }.ToList() }
            }.ToList()
        };

        store.Save(data);

        // Новый формат — объект со службами и группами (не массив строк).
        var text = File.ReadAllText(store.FilePath);
        Assert.Contains("\"Services\"", text);
        Assert.Contains("\"Groups\"", text);
        Assert.Contains("\"Name\"", text);

        var loaded = store.Load();
        Assert.Equal(new[] { "srv-a" }, loaded.Services);
        Assert.Single(loaded.Groups);
        Assert.Equal("Группа 1", loaded.Groups[0].Name);
        Assert.Equal(new[] { "srv-b", "srv-c" }, loaded.Groups[0].Services);
    }

    [Fact]
    public void Normalize_RemovesDuplicates_SkipsEmpty_KeepsOrder()
    {
        var data = new ServiceStoreData
        {
            Services = new[] { "  srv-a  ", "", "  ", "srv-a", "srv-b", null! }.ToList()
        };

        var normalized = ServiceStoreData.Normalize(data);

        Assert.Equal(new[] { "srv-a", "srv-b" }, normalized.Services);
        Assert.Empty(normalized.Groups);
    }

    [Fact]
    public void Normalize_RemovesDuplicateGroupNames()
    {
        var data = new ServiceStoreData
        {
            Groups = new[]
            {
                new ServiceGroup { Name = "Группа", Services = new[] { "srv-a" }.ToList() },
                new ServiceGroup { Name = " группа ", Services = new[] { "srv-b" }.ToList() },
                new ServiceGroup { Name = "Другая", Services = new[] { "srv-c" }.ToList() }
            }.ToList()
        };

        var normalized = ServiceStoreData.Normalize(data);

        Assert.Equal(2, normalized.Groups.Count);
        Assert.Equal("Группа", normalized.Groups[0].Name);
        Assert.Equal(new[] { "srv-a" }, normalized.Groups[0].Services);
        Assert.Equal("Другая", normalized.Groups[1].Name);
        Assert.Equal(new[] { "srv-c" }, normalized.Groups[1].Services);
    }

    [Fact]
    public void Normalize_TrimsGroupServiceNames_AndSkipsEmpty()
    {
        var data = new ServiceStoreData
        {
            Groups = new[]
            {
                new ServiceGroup { Name = "Группа", Services = new[] { "  srv-a  ", "", "srv-a", "srv-b" }.ToList() }
            }.ToList()
        };

        var normalized = ServiceStoreData.Normalize(data);

        Assert.Single(normalized.Groups);
        Assert.Equal(new[] { "srv-a", "srv-b" }, normalized.Groups[0].Services);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmptyObjectWithoutThrowing()
    {
        using var tmp = TempDir();
        Directory.CreateDirectory(tmp.Path);
        var store = new ServicesStore(directoryOverride: tmp.Path);
        File.WriteAllText(store.FilePath, "{ not valid json ");

        var data = store.Load();

        Assert.Empty(data.Services);
        Assert.Empty(data.Groups);
    }

    [Fact]
    public void Load_EmptyFile_ReturnsEmptyObject()
    {
        using var tmp = TempDir();
        Directory.CreateDirectory(tmp.Path);
        var store = new ServicesStore(directoryOverride: tmp.Path);
        File.WriteAllText(store.FilePath, "");

        var data = store.Load();

        Assert.Empty(data.Services);
        Assert.Empty(data.Groups);
    }

    // ===================== Порядок строк (Order, issue #11, итерация 2) =====================

    [Fact]
    public void Load_ObjectWithoutOrder_BuildsDefaultOrder_ServicesThenGroups()
    {
        using var tmp = TempDir();
        Directory.CreateDirectory(tmp.Path);
        var store = new ServicesStore(directoryOverride: tmp.Path);
        // Объект итерации 2 без поля Order (старый формат этой итерации).
        File.WriteAllText(store.FilePath,
            "{\"Services\":[\"srv-a\",\"srv-b\"],\"Groups\":[{\"Name\":\"Группа\",\"Services\":[\"srv-c\"]}]}");

        var data = store.Load();

        Assert.Equal(new[] { "srv-a", "srv-b", "group:Группа" }, data.Order);
    }

    [Fact]
    public void Save_WithOrder_RoundTripsMixedSequence()
    {
        using var tmp = TempDir();
        var store = new ServicesStore(directoryOverride: tmp.Path);
        var data = new ServiceStoreData
        {
            Services = new[] { "srv-a", "srv-b" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Группа 1", Services = new[] { "srv-c" }.ToList() },
                new ServiceGroup { Name = "Группа 2", Services = new[] { "srv-d" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Группа 1", "srv-a", "group:Группа 2", "srv-b" }.ToList()
        };

        store.Save(data);

        var loaded = store.Load();
        Assert.Equal(new[] { "group:Группа 1", "srv-a", "group:Группа 2", "srv-b" }, loaded.Order);
        Assert.Equal(new[] { "srv-a", "srv-b" }, loaded.Services);
        Assert.Equal(2, loaded.Groups.Count);
    }

    [Fact]
    public void NormalizeOrder_RemovesDuplicatesAndMissing_AppendsMissing()
    {
        var data = new ServiceStoreData
        {
            Services = new[] { "srv-a", "srv-b" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Группа", Services = new[] { "srv-c" }.ToList() }
            }.ToList(),
            Order = new[] { "group:Группа", "srv-a", "group:Группа", "srv-missing", "  srv-a  " }.ToList()
        };

        var normalized = ServiceStoreData.Normalize(data);

        // Дубли и пропущенные ключи удалены; недостающая служба srv-b дописана в конец.
        Assert.Equal(new[] { "group:Группа", "srv-a", "srv-b" }, normalized.Order);
    }

    [Fact]
    public void NormalizeOrder_GroupKeyPrefix_ResolvesGroupBeforeService()
    {
        var data = new ServiceStoreData
        {
            Services = new[] { "group:x" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "x", Services = new[] { "srv-a" }.ToList() }
            }.ToList(),
            Order = new[] { "group:x" }.ToList()
        };

        var normalized = ServiceStoreData.Normalize(data);

        // Ключ «group:x» трактуется как группа (имя «x» существует). Служба с именем
        // «group:x» не может быть представлена в Order — коллизия ключа с префиксом
        // группы (документированное ограничение; имена служб с «group:» не встречаются).
        Assert.Equal(new[] { "group:x" }, normalized.Order);
    }

    [Fact]
    public void Load_NewEmptyObjectFormat_ReturnsEmptyDataWithoutThrowing()
    {
        // Пустой объект нового формата (итерация 2): services и groups пусты —
        // загрузка не должна бросать исключений и возвращает пустой объект
        // (Задача 5, issue #11).
        using var tmp = TempDir();
        Directory.CreateDirectory(tmp.Path);
        var store = new ServicesStore(directoryOverride: tmp.Path);
        File.WriteAllText(store.FilePath, "{\"Services\":[],\"Groups\":[],\"Order\":[]}");

        var data = store.Load();

        Assert.Empty(data.Services);
        Assert.Empty(data.Groups);
        Assert.Empty(data.Order);
    }

    [Fact]
    public void Load_DuplicateGroupNamesInFile_CaseInsensitive_KeepsFirst()
    {
        // Файл с группами, имена которых дублируются без учёта регистра (плюс пробелы
        // вокруг имени): нормализация при загрузке убирает дубль, оставляя первую группу
        // (Задача 5, issue #11).
        using var tmp = TempDir();
        Directory.CreateDirectory(tmp.Path);
        var store = new ServicesStore(directoryOverride: tmp.Path);
        File.WriteAllText(store.FilePath,
            "{\"Services\":[\"srv-a\"],\"Groups\":[{\"Name\":\"Группа\",\"Services\":[\"srv-a\"]}," +
            "{\"Name\":\" группа \",\"Services\":[\"srv-b\"]}]}");

        var data = store.Load();

        var group = Assert.Single(data.Groups);
        Assert.Equal("Группа", group.Name);
        Assert.Equal(new[] { "srv-a" }, group.Services);
    }

    [Fact]
    public void NormalizeOrder_DropsUnknownKeys_TrimsGroupKeys_AppendsMissing()
    {
        // Order с «мусорными» ключами (нет ни в services, ни в groups) и пробелами вокруг
        // ключа группы: мусор отбрасывается, пробелы обрезаются, недостающая служба b
        // дописывается в конец (Задача 5, issue #11).
        var data = new ServiceStoreData
        {
            Services = new[] { "a", "b" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Г1", Services = new[] { "c" }.ToList() }
            }.ToList(),
            Order = new[] { "srv-junk", "group:JunkGroup", "  group:Г1  ", "a" }.ToList()
        };

        var normalized = ServiceStoreData.Normalize(data);

        Assert.Equal(new[] { "group:Г1", "a", "b" }, normalized.Order);
    }

    [Fact]
    public void SaveWithGroups_RoundTrip_KeepsMembersInGroupsAndDropsTheirRootKeys()
    {
        // Полный round-trip данных с группами: Save → Load — группы и их члены сохраняются;
        // члены групп НЕ остаются в корневом списке и в верхнем Order (инвариант «служба
        // ровно в одном месте» в иерархическом списке, issue #11, 1.0.0.14).
        using var tmp = TempDir();
        var store = new ServicesStore(directoryOverride: tmp.Path);
        var data = new ServiceStoreData
        {
            Services = new[] { "x" }.ToList(),
            Groups = new[]
            {
                new ServiceGroup { Name = "Группа 1", Services = new[] { "a", "c" }.ToList() },
                new ServiceGroup { Name = "Группа 2", Services = new[] { "b", "d" }.ToList() }
            }.ToList(),
            // «c», «a», «d», «b» — ключи членов в верхнем Order (устаревший вид данных):
            // при нормализации они отбрасываются, остаются группы и корневая «x».
            Order = new[] { "group:Группа 1", "c", "a", "group:Группа 2", "d", "b", "x" }.ToList()
        };

        store.Save(data);

        var loaded = store.Load();
        Assert.Equal(new[] { "x" }, loaded.Services);
        Assert.Equal(data.Groups.Count, loaded.Groups.Count);
        for (var i = 0; i < data.Groups.Count; i++)
        {
            Assert.Equal(data.Groups[i].Name, loaded.Groups[i].Name);
            Assert.Equal(data.Groups[i].Services, loaded.Groups[i].Services);
        }
        Assert.Equal(new[] { "group:Группа 1", "group:Группа 2", "x" }, loaded.Order);
    }

    [Fact]
    public void Load_LegacyArray_BuildsAutoOrderFromServices()
    {
        // Старый формат (массив строк, итерация 1): файл без Order — порядок выводится
        // автоматически, все службы; групп нет (Задача 5, issue #11).
        using var tmp = TempDir();
        Directory.CreateDirectory(tmp.Path);
        var store = new ServicesStore(directoryOverride: tmp.Path);
        File.WriteAllText(store.FilePath, "[\"srv-a\", \"srv-b\"]");

        var data = store.Load();

        Assert.Equal(new[] { "srv-a", "srv-b" }, data.Services);
        Assert.Equal(new[] { "srv-a", "srv-b" }, data.Order);
        Assert.Empty(data.Groups);
    }

    private static TempDirectory TempDir() => new();

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cm_svcs_grp_{Guid.NewGuid():N}");

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* не критично */ }
        }
    }
}