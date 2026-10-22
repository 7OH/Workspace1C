using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Единое форматирование имени версии платформы во всех списках (issue #33):
/// «8.3.27.2214 (x64)» для обычной платформы и «8.3.27.2214 (x64) (тонкий клиент)»
/// для тонкого клиента (в каталоге версии есть 1cv8c, но нет полного 1cv8).
/// Используется и в диалоге выбора версии, и в настройках, на обеих платформах
/// (WPF и Avalonia), чтобы суффикс/подсветка гарантированно появлялись везде.
/// </summary>
public static class PlatformDisplayFormatter
{
    /// <summary>Ключ локализации суффикса тонкого клиента (ru: «(тонкий клиент)», en: «(thin client)»).</summary>
    public const string ThinClientSuffixKey = "PlatformVersionPicker.ThinClientSuffix";

    /// <summary>Локализованный суффикс тонкого клиента, например «(тонкий клиент)».</summary>
    public static string ThinClientSuffix => LocalizationManager.T(ThinClientSuffixKey);

    /// <summary>
    /// Чистое ядро форматирования (для тестов: суффикс передаётся явно, без
    /// зависимости от текущего языка): добавляет суффикс с пробелом к имени,
    /// если <paramref name="isThinClient"/> == true.
    /// </summary>
    public static string AppendThinClientSuffix(string baseName, bool isThinClient, string? suffix = null)
        => isThinClient
            ? baseName + " " + (suffix ?? ThinClientSuffix)
            : baseName;

    /// <summary>Отображаемое имя узла дерева платформ с учётом тонкого клиента.</summary>
    public static string Format(PlatformVersionGroup node)
        => AppendThinClientSuffix(node.Name, node.IsThinClient);

    /// <summary>Отображаемое имя версии платформы с учётом тонкого клиента.</summary>
    public static string Format(PlatformVersionInfo info)
        => AppendThinClientSuffix(info.Display, info.IsThinClient);
}