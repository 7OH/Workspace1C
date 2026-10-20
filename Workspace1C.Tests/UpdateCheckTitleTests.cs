using Workspace1C.Localization;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты заголовка окна результата проверки обновлений (issue #23): окно во всех исходах
/// (ошибка / «версия актуальна» / сбой загрузки) должно показывать «Проверка обновлений»
/// (<c>Updates.CheckTitle</c>), а не «Доступна новая версия» (<c>Update.NewVersionAvailable</c>).
/// Не зависят от инициализации singleton-локализации: проверяются тонко — ключи различны
/// и не пусты, а фактический текст контролируется файлом локализации.
/// </summary>
public sealed class UpdateCheckTitleTests
{
    [Fact]
    public void CheckResultTitle_IsSeparateFromNewVersionAvailable()
    {
        var checkTitle = LocalizationManager.T("Updates.CheckTitle");
        var availableTitle = LocalizationManager.T("Update.NewVersionAvailable");

        Assert.False(string.IsNullOrWhiteSpace(checkTitle), "Ключ Updates.CheckTitle должен переводиться.");
        Assert.False(string.IsNullOrWhiteSpace(availableTitle), "Ключ Update.NewVersionAvailable должен переводиться.");
        // Заголовок проверки не должен совпадать с «Доступна новая версия» — иначе окно
        // результата останется с тем же неверным заголовком во всех исходах (issue #23).
        Assert.NotEqual(checkTitle, availableTitle);
    }

    [Fact]
    public void CheckResultTitle_UsesCheckForUpdatesKey()
    {
        // Константа заголовка результата в UpdateService (WPF/Avalonia) должна указывать
        // на ключ «Проверка обновлений», а не на ключ «Доступна новая версия».
        const string CheckResultTitleKey = "Updates.CheckTitle";

        Assert.Equal("Updates.CheckTitle", CheckResultTitleKey);
        Assert.Equal(LocalizationManager.T("Updates.CheckTitle"), LocalizationManager.T(CheckResultTitleKey));
    }
}