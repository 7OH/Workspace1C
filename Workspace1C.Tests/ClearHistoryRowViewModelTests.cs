using Workspace1C.Models;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты строки диалога «Очистка истории запусков» (issue #17): свойство
/// <see cref="ClearHistoryRowViewModel.IsChecked"/> поднимает
/// <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/>, что позволяет
/// окну обновлять доступность кнопки «Очистить историю» при клике по флажку отдельной базы
/// (а не только при «Выбрать все / Сбросить / Инвертировать»).
/// </summary>
public sealed class ClearHistoryRowViewModelTests
{
    private static ClearHistoryRowViewModel CreateRow(bool withHistory = false)
    {
        var infobase = new Infobase { Name = "База" };
        if (withHistory)
            infobase.LaunchHistory = new List<LaunchHistoryEntry> { new() };
        return new ClearHistoryRowViewModel(infobase);
    }

    [Fact]
    public void IsChecked_Default_IsFalse()
    {
        var row = CreateRow();

        Assert.False(row.IsChecked);
    }

    [Fact]
    public void IsChecked_SetTrue_RaisesPropertyChanged()
    {
        var row = CreateRow();
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.IsChecked = true;

        Assert.True(row.IsChecked);
        Assert.Contains(nameof(ClearHistoryRowViewModel.IsChecked), changed);
    }

    [Fact]
    public void IsChecked_SetSameValue_DoesNotRaisePropertyChanged()
    {
        var row = CreateRow();
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.IsChecked = false; // уже false — событие не должно подниматься

        Assert.Empty(changed);
    }

    [Fact]
    public void IsChecked_SetFalseAfterTrue_RaisesPropertyChanged()
    {
        var row = CreateRow();
        row.IsChecked = true;
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.IsChecked = false;

        Assert.False(row.IsChecked);
        Assert.Contains(nameof(ClearHistoryRowViewModel.IsChecked), changed);
    }

    [Fact]
    public void HistoryCount_ReflectsInfobaseLaunchHistory()
    {
        var row = CreateRow(withHistory: true);

        Assert.Equal(1, row.HistoryCount);
        Assert.True(row.HasHistory);
    }

    [Fact]
    public void SyncFromInfobase_RaisesPropertyChangedForCount()
    {
        var row = CreateRow();
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.SyncFromInfobase();

        Assert.Contains(nameof(ClearHistoryRowViewModel.HistoryCount), changed);
        Assert.Contains(nameof(ClearHistoryRowViewModel.HasHistory), changed);
    }
}
