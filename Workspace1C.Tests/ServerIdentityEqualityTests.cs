using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты чистого правила подсветки подключённого сервера (issue #3, комментарий 7OH
/// 19.10): строка списка «Сервера управления» выделяется жирным, когда Id записи
/// совпадает с Id подключённого сервера (ConnectedServer). Правило
/// <see cref="ServerIdentityEquality.EqualsById"/> общее для WPF-конвертера
/// (Converters.ServerIdentityEqualityConverter) и Avalonia-версии
/// (Converters/Avalonia.ServerConnectedFontWeightConverter) — проверяется напрямую,
/// без платформенных зависимостей.
/// </summary>
public sealed class ServerIdentityEqualityTests
{
    [Fact]
    public void EqualsById_SameId_ReturnsTrue()
    {
        var connected = new ManagementServer { Id = "srv-1" };
        var candidate = new ManagementServer { Id = "srv-1", Description = "Другое описание" };

        Assert.True(ServerIdentityEquality.EqualsById(connected, candidate));
    }

    [Fact]
    public void EqualsById_DifferentIds_ReturnsFalse()
    {
        var connected = new ManagementServer { Id = "srv-1" };
        var candidate = new ManagementServer { Id = "srv-2" };

        Assert.False(ServerIdentityEquality.EqualsById(connected, candidate));
    }

    [Fact]
    public void EqualsById_NullOperand_ReturnsFalse()
    {
        // Без активного подключения (ConnectedServer == null) жирным не выделяется
        // ни одна строка — «не совпадает» для любого кандидата.
        var server = new ManagementServer { Id = "srv-1" };

        Assert.False(ServerIdentityEquality.EqualsById(null, server));
        Assert.False(ServerIdentityEquality.EqualsById(server, null));
        Assert.False(ServerIdentityEquality.EqualsById(null, null));
    }

    [Fact]
    public void EqualsById_EmptyConnectedId_ReturnsFalse()
    {
        // Пустой идентификатор записи-кандидата не может «подсветить» строку.
        var connected = new ManagementServer { Id = string.Empty };
        var candidate = new ManagementServer { Id = string.Empty };

        Assert.False(ServerIdentityEquality.EqualsById(connected, candidate));
    }
}