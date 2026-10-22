using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Workspace1C.Services;

/// <summary>
/// Тонкий HTTP-клиент Bot API Telegram для интерактивных подтверждений (выпуск 1.0.0.36+):
/// long polling <c>getUpdates</c>, <c>sendMessage</c> с inline-клавиатурой,
/// <c>answerCallbackQuery</c>, <c>editMessageText</c> (снятие кнопок и статус).
/// Использует те же настройки (токен/чат из <see cref="NotificationChannelSettings"/>)
/// и тот же базовый адрес, что и <see cref="TelegramNotificationChannel"/>.
/// Токен маскируется в журнале и текстах ошибок (<see cref="SensitiveDataMasker"/>).
/// Подменяемый <see cref="HttpMessageHandler"/> — для юнит-тестов без сети.
/// </summary>
public sealed class TelegramBotApiClient
{
    /// <summary>Максимальный таймаут long polling Bot API (секунд).</summary>
    public const int LongPollTimeoutSeconds = 50;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    /// <summary>JSON-список типов обновлений, которые нас интересуют в поллинге.</summary>
    private const string AllowedUpdates = "[\"callback_query\"]";

    private readonly IAppLogger? _logger;
    private readonly HttpMessageHandler? _handler;

    /// <param name="logger">Журнал (может быть null).</param>
    /// <param name="handler">Обработчик HTTP (подменяется в тестах; null — стандартный).</param>
    public TelegramBotApiClient(IAppLogger? logger = null, HttpMessageHandler? handler = null)
    {
        _logger = logger;
        _handler = handler;
    }

    // ------------------------------------------------------------------
    // sendMessage с inline-клавиатурой
    // ------------------------------------------------------------------

    /// <summary>
    /// Отправляет сообщение; при <paramref name="buttons"/> — с inline-кнопками.
    /// При успехе возвращает chat_id и message_id (нужны для <c>editMessageText</c>).
    /// </summary>
    public async Task<(bool Ok, string Error, TelegramSendResult? Result)> SendMessageAsync(
        string token,
        string chatId,
        string text,
        IReadOnlyList<TelegramInlineButton>? buttons = null,
        string? messageThreadId = null,
        CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("chat_id", (chatId ?? string.Empty).Trim()),
            new("text", text ?? string.Empty),
            new("disable_web_page_preview", "true"),
        };
        if (!string.IsNullOrWhiteSpace(messageThreadId))
            fields.Add(new("message_thread_id", messageThreadId.Trim()));
        if (buttons is { Count: > 0 })
            fields.Add(new("reply_markup", BuildReplyMarkupJson(buttons)));

        var (ok, error, body) = await SendCoreAsync(token, "sendMessage", fields, cancellationToken).ConfigureAwait(false);
        if (!ok)
            return (false, error, null);

        try
        {
            using var doc = JsonDocument.Parse(body!);
            var root = doc.RootElement;
            var result = root.GetProperty("result");
            var messageId = result.TryGetProperty("message_id", out var mid) ? mid.GetInt64() : 0;
            var resultChat = result.TryGetProperty("chat", out var chat)
                             && chat.TryGetProperty("id", out var cid)
                ? cid.GetInt64().ToString()
                : chatId;
            return (true, string.Empty, new TelegramSendResult(resultChat, messageId));
        }
        catch (Exception ex)
        {
            var masked = SensitiveDataMasker.MaskValue(ex.Message, token);
            _logger?.Warn("Telegram: не удалось разобрать ответ sendMessage: " + masked);
            return (false, masked, null);
        }
    }

    // ------------------------------------------------------------------
    // long polling getUpdates (только callback_query)
    // ------------------------------------------------------------------

    /// <summary>
    /// Получает необработанные обновления. <paramref name="offset"/> — первый обрабатываемый
    /// <c>update_id</c>; подтверждённые ранее (хранятся в <see cref="TelegramOffsetStore"/>) сюда
    /// не передаются. Отсутствие <paramref name="offset"/> (первый запуск) возвращает историю
    /// неподтверждённых обновлений — вызывающий код подтверждает их и пропускает.
    /// </summary>
    public async Task<(bool Ok, string Error, IReadOnlyList<TelegramUpdate>? Updates)> GetUpdatesAsync(
        string token,
        long? offset,
        CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("timeout", LongPollTimeoutSeconds.ToString()),
            new("allowed_updates", AllowedUpdates),
        };
        if (offset is { } from)
            fields.Add(new("offset", from.ToString()));

        var (ok, error, body) = await SendCoreAsync(token, "getUpdates", fields, cancellationToken).ConfigureAwait(false);
        if (!ok)
            return (false, error, null);

        try
        {
            using var doc = JsonDocument.Parse(body!);
            var result = doc.RootElement.GetProperty("result");
            var updates = new List<TelegramUpdate>();
            foreach (var item in result.EnumerateArray())
                updates.Add(ParseUpdate(item));
            return (true, string.Empty, updates);
        }
        catch (Exception ex)
        {
            var masked = SensitiveDataMasker.MaskValue(ex.Message, token);
            _logger?.Warn("Telegram: не удалось разобрать getUpdates: " + masked);
            return (false, masked, null);
        }
    }

    /// <summary>
    /// Разбор одного элемента <c>result</c> из getUpdates. Элементы без <c>callback_query</c>
    /// (в т.ч. нераспознанные) возвращаются с пустыми данными — <c>update_id</c> всё равно
    /// учитывается, чтобы вызывающий код подтвердил offset и не получил их повторно.
    /// </summary>
    private static TelegramUpdate ParseUpdate(JsonElement item)
    {
        var updateId = item.TryGetProperty("update_id", out var uid) ? uid.GetInt64() : 0;
        if (!item.TryGetProperty("callback_query", out var cq))
            return new TelegramUpdate(updateId, string.Empty, null, null, null);

        var callbackId = cq.TryGetProperty("id", out var cid) ? cid.GetString() : null;
        var data = cq.TryGetProperty("data", out var d) ? d.GetString() : null;
        string chatId = string.Empty;
        long? messageId = null;
        if (cq.TryGetProperty("message", out var message))
        {
            messageId = message.TryGetProperty("message_id", out var mid) ? mid.GetInt64() : null;
            chatId = message.TryGetProperty("chat", out var chat)
                     && chat.TryGetProperty("id", out var c) && c.ValueKind == JsonValueKind.Number
                ? c.GetInt64().ToString()
                : string.Empty;
        }

        return new TelegramUpdate(updateId, chatId, callbackId, data, messageId);
    }

    // ------------------------------------------------------------------
    // answerCallbackQuery / editMessageText
    // ------------------------------------------------------------------

    /// <summary>Подтверждает получение нажатия кнопки (индикатор «крутилка» на кнопке).</summary>
    public async Task<(bool Ok, string Error)> AnswerCallbackQueryAsync(
        string token,
        string callbackQueryId,
        string? text = null,
        CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("callback_query_id", (callbackQueryId ?? string.Empty).Trim()),
        };
        if (!string.IsNullOrWhiteSpace(text))
            fields.Add(new("text", text.Trim()));
        var (ok, error, _) = await SendCoreAsync(token, "answerCallbackQuery", fields, cancellationToken).ConfigureAwait(false);
        return (ok, error);
    }

    /// <summary>
    /// Заменяет текст сообщения и снимает inline-кнопки (пустая inline_keyboard).
    /// Используется для фиксации статуса подтверждения.
    /// </summary>
    public async Task<(bool Ok, string Error)> EditMessageTextAsync(
        string token,
        string chatId,
        long messageId,
        string text,
        CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("chat_id", (chatId ?? string.Empty).Trim()),
            new("message_id", messageId.ToString()),
            new("text", text ?? string.Empty),
            new("reply_markup", "{\"inline_keyboard\":[]}"),
        };
        var (ok, error, _) = await SendCoreAsync(token, "editMessageText", fields, cancellationToken).ConfigureAwait(false);
        return (ok, error);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Сериализация inline-клавиатуры: все кнопки в одну строку —
    /// <c>{"inline_keyboard":[[{"text":..,"callback_data":..},...]]}</c>.
    /// Кириллица пишется читаемой (UnsafeRelaxedJsonEscaping), а не \uXXXX-последовательностями.
    /// </summary>
    public static string BuildReplyMarkupJson(IReadOnlyList<TelegramInlineButton> buttons)
    {
        var content = buttons.Select(b => new { text = b.Text, callback_data = b.CallbackData }).ToArray();
        var payload = new { inline_keyboard = new[] { content } };
        return System.Text.Json.JsonSerializer.Serialize(payload, ReplyMarkupJsonOptions);
    }

    private static readonly System.Text.Json.JsonSerializerOptions ReplyMarkupJsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private async Task<(bool Ok, string Error, string? Body)> SendCoreAsync(
        string token,
        string method,
        IEnumerable<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken)
    {
        // Внешний handler (тесты) приоритетнее; иначе — прокси из переменных окружения
        // (W1C_TELEGRAM_PROXY/HTTPS_PROXY...) для сетей с блокировкой api.telegram.org.
        var proxyHandler = _handler ?? TelegramHttpProxy.CreateHandler();
        using var http = proxyHandler is null
            ? new HttpClient()
            : new HttpClient(proxyHandler, disposeHandler: _handler is null);
        http.Timeout = RequestTimeout;
        var url = $"{TelegramNotificationChannel.ApiBase}/bot{token.Trim()}/{method}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(fields),
            };
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var error = $"HTTP {(int)response.StatusCode}: {Truncate(body, 300)}";
                var masked = SensitiveDataMasker.MaskValue(error, token);
                _logger?.Warn("Telegram API: " + masked);
                return (false, masked, null);
            }

            var ok = IsOk(body);
            if (!ok)
            {
                var description = ErrorCodeAndDescription(body);
                var message = $"API error{description}";
                var maskedError = SensitiveDataMasker.MaskValue(message, token);
                _logger?.Warn("Telegram API: " + maskedError);
                return (false, maskedError, null);
            }

            return (true, string.Empty, body);
        }
        catch (Exception ex)
        {
            var masked = SensitiveDataMasker.MaskValue(ex.Message, token);
            _logger?.Warn("Telegram API: " + masked);
            return (false, masked, null);
        }
    }

    private static bool IsOk(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>« error_code=XXX: description» если описание есть в ответе Bot API.</summary>
    private static string ErrorCodeAndDescription(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var parts = new List<string>();
            if (root.TryGetProperty("error_code", out var code))
                parts.Add("code=" + code.GetInt32());
            if (root.TryGetProperty("description", out var desc))
                parts.Add(": " + desc.GetString());
            return parts.Count > 0 ? " (" + string.Join(" ", parts) + ")" : body;
        }
        catch
        {
            return body;
        }
    }

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
            return text ?? string.Empty;
        return text.Substring(0, max - 1) + "…";
    }
}
