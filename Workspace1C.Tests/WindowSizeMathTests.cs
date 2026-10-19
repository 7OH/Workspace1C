using Workspace1C;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты чистых расчётов размеров/позиции окон (<see cref="WindowSizeMath"/>, issue #308/#305):
/// клампинг высоты окна по контенту, подъём окна, уходящего за нижний край рабочей области,
/// границы максимизации на рабочую область монитора (issue #368), горизонтальная позиция
/// FitLeft и ужатие размера ClampToWorkArea (issue #3: окно «Серверы 1С» не уходит за экран).
/// </summary>
public sealed class WindowSizeMathTests
{
    [Theory]
    [InlineData(500, 420, 800, 500)] // контент в пределах ограничений
    [InlineData(300, 420, 800, 420)] // контент меньше MinHeight
    [InlineData(950, 420, 800, 800)] // контент больше MaxHeight
    [InlineData(420, 420, 800, 420)] // ровно MinHeight
    [InlineData(800, 420, 800, 800)] // ровно MaxHeight
    public void ClampHeight_RespectsMinAndMax(double desired, double min, double max, double expected)
    {
        Assert.Equal(expected, WindowSizeMath.ClampHeight(desired, min, max));
    }

    [Fact]
    public void ClampHeight_MaxZero_MeansNoLimit()
    {
        // max = 0 — ограничение не задано: результат не режется сверху.
        Assert.Equal(1200, WindowSizeMath.ClampHeight(1200, 420, 0));
    }

    [Fact]
    public void ClampHeight_NegativeDesired_IsRaisedToMin()
    {
        Assert.Equal(420, WindowSizeMath.ClampHeight(-10, 420, 800));
    }

    [Fact]
    public void FitTop_InsideWorkArea_KeepsPosition()
    {
        // top=100, height=600, область 0..1080: окно помещается — позиция не меняется.
        Assert.Equal(100, WindowSizeMath.FitTop(100, 600, 0, 1080));
    }

    [Fact]
    public void FitTop_BelowWorkAreaBottom_RaisesWindow()
    {
        // top=700, height=600, область 0..1080: низ 1300 > 1080 — поднимаем до 480.
        Assert.Equal(480, WindowSizeMath.FitTop(700, 600, 0, 1080));
    }

    [Fact]
    public void FitTop_TallerThanWorkArea_SticksToTop()
    {
        // Окно выше рабочей области: прижимаем к верхнему краю (не даём уйти за него).
        Assert.Equal(0, WindowSizeMath.FitTop(200, 1400, 0, 1080));
    }

    [Fact]
    public void FitTop_InvalidWorkArea_KeepsPosition()
    {
        // Некорректная область (низ <= верх) — позицию не трогаем.
        Assert.Equal(50, WindowSizeMath.FitTop(50, 600, 100, 100));
    }

    // ---- FitLeft (issue #3: окно «Серверы 1С» не должно уезжать за экран) ----

    [Fact]
    public void FitLeft_InsideWorkArea_KeepsPosition()
    {
        // left=200, width=800, область 0..1400: окно помещается — позиция не меняется.
        Assert.Equal(200, WindowSizeMath.FitLeft(200, 800, 0, 1400));
    }

    [Fact]
    public void FitLeft_OutsideWorkAreaRight_MovesWindowLeft()
    {
        // left=1200, width=800, область 0..1400: правый край 2000 > 1400 — сдвигаем до 600.
        Assert.Equal(600, WindowSizeMath.FitLeft(1200, 800, 0, 1400));
    }

    [Fact]
    public void FitLeft_WiderThanWorkArea_SticksToWorkLeft()
    {
        // Окно шире рабочей области: прижимаем к левому краю (не даём уйти за него).
        Assert.Equal(0, WindowSizeMath.FitLeft(200, 1600, 0, 1400));
    }

    [Fact]
    public void FitLeft_ReturnedWindowStaysInsideWorkAreaBothSides()
    {
        // Область 60..1200, окно 900: исходная позиция у правого края — сдвигаем так,
        // чтобы окно легло внутрь области и слева (>=60), и справа (<=1200).
        // 1000 + 900 = 1900 > 1200 -> Math.Max(60, 1200 - 900 = 300) = 300.
        Assert.Equal(300, WindowSizeMath.FitLeft(1000, 900, 60, 1200));
    }

    [Fact]
    public void FitLeft_InvalidWorkArea_KeepsPosition()
    {
        // Некорректная область (правый край <= левого) — позицию не трогаем.
        Assert.Equal(50, WindowSizeMath.FitLeft(50, 600, 100, 100));
    }

    // ---- ClampToWorkArea (issue #3: ужатие размера до рабочей области) ----

    [Theory]
    [InlineData(1500, 1024, 1024)] // размер больше максимума — ограничиваем
    [InlineData(500, 1024, 500)]   // размер в пределах максимума — не трогаем
    [InlineData(1024, 1024, 1024)] // ровно максимум — без изменений
    public void ClampToWorkArea_LimitsSize(double value, double max, double expected)
    {
        Assert.Equal(expected, WindowSizeMath.ClampToWorkArea(value, max));
    }

    [Fact]
    public void ClampToWorkArea_MaxZero_MeansNoLimit()
    {
        // max = 0 — ограничение не задано: результат не режется сверху.
        Assert.Equal(1500, WindowSizeMath.ClampToWorkArea(1500, 0));
    }

    [Fact]
    public void ClampToWorkArea_NonPositiveValue_ReturnedAsIs()
    {
        // value <= 0 («размер не задан») — не трогаем даже при ограничении.
        Assert.Equal(0, WindowSizeMath.ClampToWorkArea(0, 1024));
        Assert.Equal(-10, WindowSizeMath.ClampToWorkArea(-10, 1024));
    }

    // ---- ComputeMaximizedBounds (issue #368: максимизация на рабочую область) ----

    private static (int X, int Y, int Width, int Height) MaxBounds(
        (int Left, int Top, int Right, int Bottom) monitor,
        (int Left, int Top, int Right, int Bottom) work)
    {
        WindowSizeMath.ComputeMaximizedBounds(monitor, work,
            out int x, out int y, out int w, out int h);
        return (x, y, w, h);
    }

    [Fact]
    public void MaximizeBounds_TaskbarAtBottom_WindowCoversWorkAreaOnly()
    {
        var monitor = (0, 0, 1920, 1080);
        var work = (0, 0, 1920, 1040); // панель задач 40px внизу

        Assert.Equal((0, 0, 1920, 1040), MaxBounds(monitor, work));
    }

    [Fact]
    public void MaximizeBounds_TaskbarAtTop_WindowDoesNotCoverIt()
    {
        var monitor = (0, 0, 1920, 1080);
        var work = (0, 40, 1920, 1080);

        Assert.Equal((0, 40, 1920, 1040), MaxBounds(monitor, work));
    }

    [Fact]
    public void MaximizeBounds_TaskbarAtLeft_WindowDoesNotCoverIt()
    {
        var monitor = (0, 0, 1920, 1080);
        var work = (60, 0, 1920, 1080);

        Assert.Equal((60, 0, 1860, 1080), MaxBounds(monitor, work));
    }

    [Fact]
    public void MaximizeBounds_TaskbarAtRight_WindowDoesNotCoverIt()
    {
        var monitor = (0, 0, 1920, 1080);
        var work = (0, 0, 1860, 1080);

        Assert.Equal((0, 0, 1860, 1080), MaxBounds(monitor, work));
    }

    [Fact]
    public void MaximizeBounds_WorkAreaEqualsMonitor_MaximizesToFullScreen()
    {
        var monitor = (0, 0, 2560, 1440);
        var work = (0, 0, 2560, 1440);

        Assert.Equal((0, 0, 2560, 1440), MaxBounds(monitor, work));
    }

    [Fact]
    public void MaximizeBounds_SecondaryMonitor_PositionIsRelativeToItsOrigin()
    {
        // Второй монитор справа от основного (1920x1080).
        var monitor = (1920, 0, 3840, 1080);
        var work = (1920, 0, 3840, 1040);

        // ptMaxPosition задаётся относительно монитора, а не экрана.
        Assert.Equal((0, 0, 1920, 1040), MaxBounds(monitor, work));
    }

    [Fact]
    public void MaximizeBounds_MonitorWithNegativeCoordinates_RelativePositionStaysNonNegative()
    {
        // Монитор слева от основного — отрицательные координаты в системе экранов.
        var monitor = (-1920, 0, 0, 1080);
        var work = (-1920, 0, 0, 1040);

        Assert.Equal((0, 0, 1920, 1040), MaxBounds(monitor, work));
    }

    [Fact]
    public void MaximizeBounds_TaskbarsOnTwoSides_CombinedOffsets()
    {
        // Панель задач 60px слева и 40px сверху (док + закреплённая панель).
        var monitor = (0, 0, 2560, 1440);
        var work = (60, 40, 2560, 1440);

        Assert.Equal((60, 40, 2500, 1400), MaxBounds(monitor, work));
    }

    [Fact]
    public void MaximizeBounds_WorkAreaOnElevatedSecondaryMonitor_RelativeOffsets()
    {
        // Второй монитор выше и праве первого, панель задач — внизу.
        var monitor = (1920, -400, 3840, 680);
        var work = (1920, -400, 3840, 640);

        Assert.Equal((0, 0, 1920, 1040), MaxBounds(monitor, work));
    }
}