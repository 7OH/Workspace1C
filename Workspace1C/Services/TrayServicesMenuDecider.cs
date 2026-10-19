namespace Workspace1C.Services;

/// <summary>
/// Чистая логика состояния меню служб системного трея при Ctrl+ПКМ (issue #11,
/// «Управление службами»; 1.0.0.20). Определяет, когда меню служб подменяет штатный
/// ContextMenuStrip трея (нативный показ — закрытие по клику вне меню и ESC работает,
/// как у обычного меню), а когда состояние нужно сбросить/восстановить.
/// Класс не зависит от WinForms/WPF — покрывается юнит-тестами.
/// </summary>
public static class TrayServicesMenuDecider
{
    /// <summary>Решение на MouseDown: подменять ли штатное меню трея меню служб.</summary>
    /// <param name="ctrlDown">Зажат ли Ctrl (нативная проверка GetKeyState).</param>
    /// <param name="servicesAvailable">Доступен ли контроллер служб (иначе — обычное меню).</param>
    /// <param name="isPending">Уже идёт подавление штатного меню (повторный Ctrl+ПКМ).</param>
    public static bool ShouldSwapOnMouseDown(bool ctrlDown, bool servicesAvailable, bool isPending)
        => ctrlDown && servicesAvailable && !isPending;

    /// <summary>Решение на MouseUp: восстанавливать ли штатное меню (Ctrl отпущен между
    /// MouseDown и MouseUp — подмену нужно откатить до системного показа).</summary>
    public static bool ShouldRestoreOnMouseUp(bool ctrlDownNow, bool isPending)
        => !ctrlDownNow && isPending;

    /// <summary>Решение при закрытии меню служб (клик вне меню, ESC, выбор пункта):
    /// состояние всегда сбрасывается, штатное меню возвращается.</summary>
    public static bool ShouldResetOnClosed()
        => true;
}