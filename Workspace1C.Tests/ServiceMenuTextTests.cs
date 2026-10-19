using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты экранирования '_' в WPF-заголовках меню служб (issue #11, часть 2,
/// релиз 1.0.0.18; замечание 7OH от 19.10): WPF трактует '_' как мнемонику
/// AccessKey — сам символ пропадает, следующая буква подчёркивается («S_DB» → «SDB»).
/// Двойное подчёркивание '__' WPF отображает как один '_' и мнемонику не создаёт,
/// поэтому <see cref="ServiceTrayMenuBuilder.EscapeMenuText"/> удваивает каждый '_'.
/// В WinForms-трее мнемоника — '&', в Avalonia '_' мнемоникой не считается, поэтому
/// экранирование применяется только в WPF-адаптерах (модель не меняется).
/// </summary>
public sealed class ServiceMenuTextTests
{
    [Fact]
    public void EscapeMenuText_ServiceNameWithUnderscore_Doubles()
    {
        Assert.Equal("S__DB", ServiceTrayMenuBuilder.EscapeMenuText("S_DB"));
    }

    [Fact]
    public void EscapeMenuText_TextWithoutUnderscore_Unchanged()
    {
        Assert.Equal("web.service", ServiceTrayMenuBuilder.EscapeMenuText("web.service"));
    }

    [Fact]
    public void EscapeMenuText_MultipleUnderscores_AllDoubled()
    {
        Assert.Equal("A__B__C", ServiceTrayMenuBuilder.EscapeMenuText("A_B_C"));
    }

    [Fact]
    public void EscapeMenuText_EmptyString_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ServiceTrayMenuBuilder.EscapeMenuText(string.Empty));
    }

    [Fact]
    public void EscapeMenuText_AllUnderscores_Doubled()
    {
        Assert.Equal("____", ServiceTrayMenuBuilder.EscapeMenuText("__"));
    }

    [Fact]
    public void EscapeMenuText_Null_ReturnsEmpty()
    {
        // null → пустая строка (конвенция модели: ServiceMenuItem.Text = string.Empty).
        Assert.Equal(string.Empty, ServiceTrayMenuBuilder.EscapeMenuText(null!));
    }
}