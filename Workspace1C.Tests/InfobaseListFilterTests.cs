using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты чистой логики фильтрации/сортировки/кэширования списков «Избранное»/«Недавние»
/// (<see cref="InfobaseListFilter"/>, issue #21): фильтры возвращают только нужные базы,
/// кэш не пересчитывается при повторном запросе без изменения данных и инвалидируется
/// при изменении <see cref="Infobase.IsFavorite"/>/<see cref="Infobase.LastLaunchDate"/>.
/// </summary>
public sealed class InfobaseListFilterTests
{
    private static Infobase CreateBase(string name, bool isFavorite = false, DateTime? lastLaunch = null) => new()
    {
        Name = name,
        IsFavorite = isFavorite,
        LastLaunchDate = lastLaunch,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bases\" + name }
    };

    [Fact]
    public void FilterFavorites_ReturnsOnlyIsFavorite()
    {
        var bases = new[]
        {
            CreateBase("A", isFavorite: true),
            CreateBase("B"),
            CreateBase("C", isFavorite: true)
        };

        var result = InfobaseListFilter.FilterFavorites(bases);

        Assert.Equal(2, result.Count);
        Assert.All(result, b => Assert.True(b.IsFavorite));
        Assert.Contains(result, b => b.Name == "A");
        Assert.Contains(result, b => b.Name == "C");
    }

    [Fact]
    public void FilterRecent_SortsByLastLaunchDateDescending()
    {
        var bases = new[]
        {
            CreateBase("Old", lastLaunch: new DateTime(2026, 1, 1)),
            CreateBase("New", lastLaunch: new DateTime(2026, 9, 1)),
            CreateBase("Never"), // без даты запуска — не попадает в «Недавние»
            CreateBase("Mid", lastLaunch: new DateTime(2026, 5, 1))
        };

        var result = InfobaseListFilter.FilterRecent(bases);

        Assert.Equal(3, result.Count);
        Assert.Equal("New", result[0].Name);
        Assert.Equal("Mid", result[1].Name);
        Assert.Equal("Old", result[2].Name);
    }

    [Fact]
    public void GetFavorites_DoesNotRecomputeWhenDataUnchanged()
    {
        var bases = new List<Infobase> { CreateBase("A", isFavorite: true) };
        var calls = 0;
        var filter = new InfobaseListFilter(
            favoritesProvider: () =>
            {
                calls++;
                return InfobaseListFilter.FilterFavorites(bases);
            },
            recentProvider: () => InfobaseListFilter.FilterRecent(bases));

        var first = filter.GetFavorites();
        var second = filter.GetFavorites();

        Assert.Same(first, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void GetRecent_DoesNotRecomputeWhenDataUnchanged()
    {
        var bases = new List<Infobase> { CreateBase("A", lastLaunch: new DateTime(2026, 1, 1)) };
        var calls = 0;
        var filter = new InfobaseListFilter(
            favoritesProvider: () => InfobaseListFilter.FilterFavorites(bases),
            recentProvider: () =>
            {
                calls++;
                return InfobaseListFilter.FilterRecent(bases);
            });

        filter.GetRecent();
        filter.GetRecent();

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Invalidate_RecomputesFavoritesAfterIsFavoriteChange()
    {
        var bases = new List<Infobase> { CreateBase("A", isFavorite: true) };
        var calls = 0;
        var filter = new InfobaseListFilter(
            favoritesProvider: () =>
            {
                calls++;
                return InfobaseListFilter.FilterFavorites(bases);
            },
            recentProvider: () => InfobaseListFilter.FilterRecent(bases));

        filter.GetFavorites();
        Assert.Equal(1, calls);

        // Изменение данных: база перестала быть избранной.
        bases[0].IsFavorite = false;
        filter.Invalidate();

        var result = filter.GetFavorites();

        Assert.Equal(2, calls);
        Assert.Empty(result);
    }

    [Fact]
    public void Invalidate_RecomputesRecentAfterLastLaunchChange()
    {
        var bases = new List<Infobase> { CreateBase("A", lastLaunch: new DateTime(2026, 1, 1)) };
        var calls = 0;
        var filter = new InfobaseListFilter(
            favoritesProvider: () => InfobaseListFilter.FilterFavorites(bases),
            recentProvider: () =>
            {
                calls++;
                return InfobaseListFilter.FilterRecent(bases);
            });

        filter.GetRecent();
        Assert.Equal(1, calls);

        // Изменение данных: дата запуска обновилась.
        bases[0].LastLaunchDate = new DateTime(2026, 9, 1);
        filter.Invalidate();

        var result = filter.GetRecent();

        Assert.Equal(2, calls);
        Assert.Single(result);
        Assert.Equal(new DateTime(2026, 9, 1), result[0].LastLaunchDate);
    }

    [Fact]
    public void Invalidate_ClearsBothCaches()
    {
        var bases = new List<Infobase>
        {
            CreateBase("A", isFavorite: true, lastLaunch: new DateTime(2026, 1, 1))
        };
        var favoritesCalls = 0;
        var recentCalls = 0;
        var filter = new InfobaseListFilter(
            favoritesProvider: () =>
            {
                favoritesCalls++;
                return InfobaseListFilter.FilterFavorites(bases);
            },
            recentProvider: () =>
            {
                recentCalls++;
                return InfobaseListFilter.FilterRecent(bases);
            });

        filter.GetFavorites();
        filter.GetRecent();
        Assert.Equal(1, favoritesCalls);
        Assert.Equal(1, recentCalls);

        filter.Invalidate();

        filter.GetFavorites();
        filter.GetRecent();
        Assert.Equal(2, favoritesCalls);
        Assert.Equal(2, recentCalls);
    }
}