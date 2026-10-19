using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты чистой логики выбора механизма показа меню служб трея при Ctrl+ПКМ
/// (issue #11, «Управление службами»; 1.0.0.20). Покрывают решения о подмене
/// штатного ContextMenuStrip менем служб (нативный показ — меню закрывается
/// по клику вне меню и по ESC, как обычное) и о восстановлении состояния
/// (замечание 7OH от 19.10: ручной Show() из v1.0.0.18 не закрывался).
/// </summary>
public sealed class TrayServicesMenuDeciderTests
{
    // ===================== MouseDown: подмена меню служб =====================

    [Fact]
    public void CtrlRightClick_ServicesAvailable_NotPending_ShouldSwap()
    {
        Assert.True(TrayServicesMenuDecider.ShouldSwapOnMouseDown(
            ctrlDown: true,
            servicesAvailable: true,
            isPending: false));
    }

    [Fact]
    public void NoCtrl_ShouldNotSwap()
    {
        Assert.False(TrayServicesMenuDecider.ShouldSwapOnMouseDown(
            ctrlDown: false,
            servicesAvailable: true,
            isPending: false));
    }

    [Fact]
    public void CtrlButServicesUnavailable_ShouldNotSwap()
    {
        // Службы недоступны — меню служб не показываем, остаётся обычное меню.
        Assert.False(TrayServicesMenuDecider.ShouldSwapOnMouseDown(
            ctrlDown: true,
            servicesAvailable: false,
            isPending: false));
    }

    [Fact]
    public void RepeatedCtrlRightClick_WhilePending_ShouldNotSwap()
    {
        // Меню служб уже открыто — повторный Ctrl+ПКМ игнорируем (без двойного меню).
        Assert.False(TrayServicesMenuDecider.ShouldSwapOnMouseDown(
            ctrlDown: true,
            servicesAvailable: true,
            isPending: true));
    }

    // ===================== MouseUp: восстановление штатного меню =====================

    [Fact]
    public void CtrlReleasedBetweenDownAndUp_ShouldRestore()
    {
        // Ctrl был зажат на MouseDown (pending), но отпущен к MouseUp —
        // штатное меню подавлено зря, его нужно восстановить.
        Assert.True(TrayServicesMenuDecider.ShouldRestoreOnMouseUp(
            ctrlDownNow: false,
            isPending: true));
    }

    [Fact]
    public void CtrlStillHeld_ShouldNotRestore()
    {
        // Ctrl зажат — меню служб уже подменено, восстановление только в Closed.
        Assert.False(TrayServicesMenuDecider.ShouldRestoreOnMouseUp(
            ctrlDownNow: true,
            isPending: true));
    }

    [Fact]
    public void NoPendingAndNoCtrl_ShouldNotRestore()
    {
        // Обычный ПКМ без Ctrl: никакой подавленной подмены нет.
        Assert.False(TrayServicesMenuDecider.ShouldRestoreOnMouseUp(
            ctrlDownNow: false,
            isPending: false));
    }

    // ===================== Closed: сброс состояния =====================

    [Fact]
    public void Closed_AlwaysResets()
    {
        // Закрытие меню служб (клик вне меню, ESC, выбор пункта) всегда
        // сбрасывает флаг и возвращает штатное меню трея.
        Assert.True(TrayServicesMenuDecider.ShouldResetOnClosed());
    }
}