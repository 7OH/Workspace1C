using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты подстановки переменных в дополнение заголовка окна (issue #28).
/// </summary>
public sealed class TitleSuffixResolverTests
{
    [Fact]
    public void Resolve_SubstitutesAllKnownTokens()
    {
        var resolved = TitleSuffixResolver.Resolve(
            "%computer_name%|%user_name%|%user_domain%|%computer_ip%|%display_server%");

        // Все пять токенов заменены: в результате не осталось ни одного известного токена.
        foreach (var token in TitleSuffixResolver.Tokens)
            Assert.DoesNotContain(token, resolved);

        // Имя компьютера и пользователя подставлены из окружения.
        Assert.Contains(Environment.MachineName, resolved);
        Assert.Contains(Environment.UserName, resolved);
    }

    [Fact]
    public void Resolve_UnknownTokenStaysAsIs()
    {
        var resolved = TitleSuffixResolver.Resolve("prefix %unknown_token% suffix");
        Assert.Equal("prefix %unknown_token% suffix", resolved);
    }

    [Fact]
    public void Resolve_EmptyTemplate_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TitleSuffixResolver.Resolve(string.Empty));
        Assert.Equal(string.Empty, TitleSuffixResolver.Resolve("   "));
    }

    [Fact]
    public void Compose_Disabled_ReturnsBaseWithVersion()
    {
        var title = TitleSuffixResolver.Compose("Workspace 1C", "1.0.0.31", enabled: false, "%computer_name%");
        Assert.Equal("Workspace 1C v1.0.0.31", title);
    }

    [Fact]
    public void Compose_EnabledWithTemplate_AppendsExtra()
    {
        var title = TitleSuffixResolver.Compose("Workspace 1C", "1.0.0.31", enabled: true, "%user_name%");
        Assert.Equal($"Workspace 1C v1.0.0.31 — {Environment.UserName}", title);
    }

    [Fact]
    public void Compose_EnabledWithEmptyResult_DoesNotAppendSeparator()
    {
        var title = TitleSuffixResolver.Compose("Workspace 1C", "1.0.0.31", enabled: true, "   ");
        Assert.Equal("Workspace 1C v1.0.0.31", title);
    }

    [Fact]
    public void Compose_DoesNotDuplicateVersionSuffix()
    {
        // Локализованное имя может уже содержать суффикс версии — не задваиваем.
        var title = TitleSuffixResolver.Compose("Workspace 1C v1.0.0.31", "1.0.0.31", enabled: false, string.Empty);
        Assert.Equal("Workspace 1C v1.0.0.31", title);
    }

    [Fact]
    public void Compose_EmptyVersion_OmitsSuffix()
    {
        var title = TitleSuffixResolver.Compose("Workspace 1C", string.Empty, enabled: false, string.Empty);
        Assert.Equal("Workspace 1C", title);
    }
}