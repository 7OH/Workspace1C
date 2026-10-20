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
}