using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Глифы и цвета кнопок-команд служб (issue #11, замечание 7OH 18.10): единый
/// источник для панели окна «Управление службами», контекстных меню, меню трея
/// и юнит-тестов. «Восстановить» отличается от «Запустить» вертикальной палочкой
/// справа от треугольника (требование 7OH: «к текущей иконке дорисовать справа»).
/// Чистый .NET — обе платформы (WPF и Avalonia).
/// </summary>
public static class ServiceCommandGlyphs
{
    /// <summary>«Запустить» — зелёный треугольник.</summary>
    public const string StartGlyph = "▶";

    /// <summary>«Остановить» — красный квадрат.</summary>
    public const string StopGlyph = "■";

    /// <summary>«Пауза» — оранжевые две палочки.</summary>
    public const string PauseGlyph = "❚❚";

    /// <summary>
    /// «Восстановить» — треугольник «Запустить» + вертикальная палочка справа
    /// (замечание 7OH 18.10: палочку дорисовать к текущей иконке, не брать новую).
    /// </summary>
    public const string ContinueGlyph = "▶|";

    /// <summary>«Перезапустить» — синяя круговая стрелка.</summary>
    public const string RestartGlyph = "⟲";

    /// <summary>Глиф операции (для «Все сервисы» — тот же, что для службы/группы).</summary>
    public static string Glyph(ServiceAction action) => action switch
    {
        ServiceAction.Start => StartGlyph,
        ServiceAction.Stop => StopGlyph,
        ServiceAction.Pause => PauseGlyph,
        ServiceAction.Continue => ContinueGlyph,
        ServiceAction.Restart => RestartGlyph,
        _ => StartGlyph
    };

    /// <summary>Цвет глифа #RRGGBB (тот же, что у кнопок панели окна «Управление службами»).</summary>
    public static string ColorHex(ServiceAction action) => action switch
    {
        ServiceAction.Start => "#16A34A",
        ServiceAction.Stop => "#DC2626",
        ServiceAction.Pause => "#D97706",
        ServiceAction.Continue => "#0EA5E9",
        ServiceAction.Restart => "#2563EB",
        _ => "#6B7280"
    };
}