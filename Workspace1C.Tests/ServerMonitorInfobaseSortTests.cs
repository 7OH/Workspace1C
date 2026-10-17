using System.Linq;
using Workspace1C.Models;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #3 (7OH 17.10): список баз монитора «Серверы 1С» всегда отсортирован по имени —
/// по алфавиту или в обратном порядке (кнопки над списком), без учёта регистра.
/// </summary>
public sealed class ServerMonitorInfobaseSortTests
{
    private static RacInfobaseRow Row(string name) => new(new RacInfobaseSummary { Name = name });

    [Fact]
    public void SortAscending_Alphabetical_IgnoreCase()
    {
        var sorted = ServerMonitorViewModel.SortInfobaseRows(
            new[] { Row("ut_11"), Row("Accounting"), Row("erp"), Row("Buh") }, descending: false);

        Assert.Equal(new[] { "Accounting", "Buh", "erp", "ut_11" }, sorted.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void SortDescending_Reverse()
    {
        var sorted = ServerMonitorViewModel.SortInfobaseRows(
            new[] { Row("B"), Row("c"), Row("a") }, descending: true);

        Assert.Equal(new[] { "c", "B", "a" }, sorted.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void Sort_Empty_ReturnsEmpty()
    {
        Assert.Empty(ServerMonitorViewModel.SortInfobaseRows(System.Array.Empty<RacInfobaseRow>(), false));
    }
}
