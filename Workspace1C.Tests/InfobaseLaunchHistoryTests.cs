using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты очистки истории запусков информационной базы (issue #18): метод
/// <see cref="Infobase.ClearLaunchHistory"/> должен удалять все записи истории И сбрасывать
/// <see cref="Infobase.LastLaunchDate"/>, чтобы база покидала режим «Недавние» (раньше при
/// «Очистить историю» обнулялся только список записей, а дата последнего запуска оставалась).
/// </summary>
public sealed class InfobaseLaunchHistoryTests
{
    private static Infobase CreateFilledInfobase()
    {
        var ib = new Infobase { Name = "База" };
        ib.LaunchHistory = new List<LaunchHistoryEntry>
        {
            new() { Mode = "Enterprise" },
            new() { Mode = "Configurator" }
        };
        ib.LastLaunchDate = DateTime.Now;
        return ib;
    }

    [Fact]
    public void ClearLaunchHistory_RemovesEntriesAndResetsLastLaunchDate()
    {
        var ib = CreateFilledInfobase();
        Assert.NotEmpty(ib.LaunchHistory);
        Assert.NotNull(ib.LastLaunchDate);

        ib.ClearLaunchHistory();

        Assert.Empty(ib.LaunchHistory);
        Assert.Null(ib.LastLaunchDate);
    }

    [Fact]
    public void ClearLaunchHistory_RaisesPropertyChanged()
    {
        var ib = CreateFilledInfobase();
        var changed = new List<string?>();
        ib.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        ib.ClearLaunchHistory();

        Assert.Contains(nameof(Infobase.LaunchHistory), changed);
        Assert.Contains(nameof(Infobase.LaunchHistoryDisplay), changed);
        Assert.Contains(nameof(Infobase.LastLaunchDisplay), changed);
    }

    [Fact]
    public void ClearLaunchHistory_WhenAlreadyEmpty_IsIdempotent()
    {
        var ib = new Infobase { Name = "База" };

        ib.ClearLaunchHistory();
        ib.ClearLaunchHistory();

        Assert.Empty(ib.LaunchHistory);
        Assert.Null(ib.LastLaunchDate);
    }

    [Fact]
    public void AddLaunchHistory_AfterClear_Works()
    {
        var ib = CreateFilledInfobase();
        ib.ClearLaunchHistory();

        ib.AddLaunchHistory("Enterprise");

        var entry = Assert.Single(ib.LaunchHistory);
        Assert.Equal("Enterprise", entry.Mode);
        Assert.NotNull(ib.LastLaunchDate);
        Assert.True(ib.LastLaunchDate.HasValue);
    }
}