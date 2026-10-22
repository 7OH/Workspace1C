using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Workspace1C.Tests;

/// <summary>
/// Подменный HttpMessageHandler для Bot API Telegram: отвечает по методу запроса
/// (sendMessage/getUpdates/answerCallbackQuery/editMessageText), записывает обращения
/// (URI + form-body) и эмулирует long polling очередью ответов getUpdates.
/// Плейсхолдер [GUID] в ответах getUpdates заменяется на requestId из последней
/// отправки с inline-кнопками — тест не знает requestId заранее.
/// </summary>
public sealed class TelegramTestHttpHandler : HttpMessageHandler
{
    /// <summary>Записанное обращение к Bot API.</summary>
    public sealed class Call
    {
        /// <summary>Последний сегмент URL (например sendMessage).</summary>
        public string Method { get; internal set; } = "";

        /// <summary>Полный URI запроса.</summary>
        public string Uri { get; internal set; } = "";

        /// <summary>Тело form-urlencoded запроса.</summary>
        public string Body { get; internal set; } = "";
    }

    /// <summary>Очередь тел ответа getUpdates (массив result); при исчерпании — пустой список.</summary>
    public List<string> GetUpdatesQueue { get; } = new();

    /// <summary>Все обращения к API в порядке выполнения.</summary>
    public List<Call> Calls { get; } = new();

    /// <summary>Ответ sendMessage (по умолчанию сообщение #10 в чате -100123).</summary>
    public string ResponseForSendMessage { get; set; } =
        "{\"ok\":true,\"result\":{\"message_id\":10,\"chat\":{\"id\":-100123}}}";

    /// <summary>Число запросов getUpdates.</summary>
    public int GetUpdatesCount => Calls.Count(c => c.Method == "getUpdates");

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
    {
        var uri = request.RequestUri?.ToString() ?? string.Empty;
        var method = uri.Length == 0 ? string.Empty : uri[(uri.LastIndexOf('/') + 1)..];
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Add(new Call { Method = method, Uri = uri, Body = body });

        var responseBody = method switch
        {
            "sendMessage" => ResponseForSendMessage,
            "getUpdates" => BuildGetUpdatesResponse(),
            _ => "{\"ok\":true}"
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        };
    }

    private string BuildGetUpdatesResponse()
    {
        var result = GetUpdatesQueue.Count > 0 ? GetUpdatesQueue[0] : "[]";
        if (GetUpdatesQueue.Count > 0)
            GetUpdatesQueue.RemoveAt(0);

        var guid = ExtractRequestIdFromLastSend();
        if (guid is not null)
            result = result.Replace("[GUID]", guid.Value.ToString());

        return "{\"ok\":true,\"result\":" + result + "}";
    }

    private Guid? ExtractRequestIdFromLastSend()
    {
        for (var i = Calls.Count - 1; i >= 0; i--)
        {
            if (Calls[i].Method != "sendMessage")
                continue;
            // Тело — form-urlencoded: значения (в т.ч. reply_markup) скрыты percent-кодированием.
            var decoded = Uri.UnescapeDataString(Calls[i].Body.Replace('+', ' '));
            var match = Regex.Match(decoded, "wc1c:([0-9a-fA-F-]{36}):");
            if (match.Success && Guid.TryParse(match.Groups[1].Value, out var id))
                return id;
        }
        return null;
    }

    /// <summary>Значение поля form-urlencoded тела последнего обращения заданного метода.</summary>
    public string? ValueOf(string method, string key)
    {
        for (var i = Calls.Count - 1; i >= 0; i--)
        {
            if (Calls[i].Method != method)
                continue;
            foreach (var pair in Calls[i].Body.Split('&'))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0)
                    continue;
                var name = Uri.UnescapeDataString(pair[..eq].Replace('+', ' '));
                if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            }
            return null;
        }
        return null;
    }
}
