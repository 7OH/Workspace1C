using System.Net;
using System.Net.Http;

namespace Workspace1C.Services;

/// <summary>
/// Резолвер HTTP-прокси для Bot API Telegram (выпуск 1.0.0.36+). В сетях, где
/// провайдер фильтрует <c>api.telegram.org:443</c> (распространённая блокировка по SNI;
/// ICMP при этом проходит), запросы к Bot API «зависают» до таймаута. Чтобы мост
/// работал, укажите адрес прокси/VPN-порта переменной окружения (порядок приоритета):
/// <para>
/// <c>W1C_TELEGRAM_PROXY</c> → <c>HTTPS_PROXY</c> → <c>https_proxy</c> → <c>HTTP_PROXY</c> → <c>http_proxy</c>.
/// </para>
/// Пустое/невалидное значение — прямое соединение (исходное поведение). Клиенты
/// (<see cref="TelegramNotificationChannel"/>, <see cref="TelegramBotApiClient"/>) создают
/// handler с прокси в момент запроса, поэтому переменные окружения можно менять между запусками.
/// </summary>
public static class TelegramHttpProxy
{
    /// <summary>Создаёт handler с прокси из переменных окружения; null — прямое соединение.</summary>
    public static HttpMessageHandler? CreateHandler()
    {
        var url = ResolveUrl();
        return CreateHandlerFor(url);
    }

    /// <summary>Создаёт handler по явному адресу прокси (Internal — для юнит-тестов).</summary>
    internal static HttpMessageHandler? CreateHandlerFor(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return null;
        return new HttpClientHandler { UseProxy = true, Proxy = new WebProxy(uri) };
    }

    /// <summary>Адрес прокси из переменных окружения (null — не задан).</summary>
    public static string? ResolveUrl() =>
        FirstNonEmpty(
            Environment.GetEnvironmentVariable("W1C_TELEGRAM_PROXY"),
            Environment.GetEnvironmentVariable("HTTPS_PROXY"),
            Environment.GetEnvironmentVariable("https_proxy"),
            Environment.GetEnvironmentVariable("HTTP_PROXY"),
            Environment.GetEnvironmentVariable("http_proxy"));

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }
}