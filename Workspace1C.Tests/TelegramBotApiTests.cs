using System.Net;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты HTTP-клиента Bot API Telegram (<see cref="TelegramBotApiClient"/>) и чистых хелперов
/// callback-data в <see cref="TelegramNotificationChannel"/>. Все запросы идут через
/// <see cref="TelegramTestHttpHandler"/> — сети нет.
/// </summary>
public sealed class TelegramBotApiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "w1c_tg_api_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* каталог может не существовать */ }
    }

    private static TelegramBotApiClient CreateClient(TelegramTestHttpHandler handler) => new(null, handler);

    [Fact]
    public async Task SendMessage_ReturnsMessageIdAndChat()
    {
        var handler = new TelegramTestHttpHandler();
        var api = CreateClient(handler);

        var (ok, error, result) = await api.SendMessageAsync("123:ABC", "-100123", "Привет");

        Assert.True(ok, error);
        Assert.NotNull(result);
        Assert.Equal(10, result!.MessageId);
        Assert.Equal("-100123", result.ChatId);
        Assert.Equal("https://api.telegram.org/bot123:ABC/sendMessage", handler.Calls.Single().Uri);
    }

    [Fact]
    public async Task SendMessage_WithButtons_IncludesReplyMarkup()
    {
        var handler = new TelegramTestHttpHandler();
        var api = CreateClient(handler);
        var buttons = new[]
        {
            new TelegramInlineButton("Да", "wc1c:00000000-0000-0000-0000-000000000001:yes"),
            new TelegramInlineButton("Нет", "wc1c:00000000-0000-0000-0000-000000000001:no"),
        };

        await api.SendMessageAsync("123:ABC", "42", "Вопрос?", buttons);

        var markup = handler.ValueOf("sendMessage", "reply_markup");
        Assert.NotNull(markup);
        Assert.Contains("inline_keyboard", markup);
        Assert.Contains("wc1c:00000000-0000-0000-0000-000000000001:yes", markup);
        Assert.Contains("\"Да\"", markup);
    }

    [Fact]
    public async Task GetUpdates_CallbackQuery_Parsed()
    {
        var handler = new TelegramTestHttpHandler();
        var guid = Guid.NewGuid();
        handler.GetUpdatesQueue.Add(
            "[{\"update_id\":105,\"callback_query\":{\"id\":\"cq-1\",\"data\":\"wc1c:" + guid + ":yes\"," +
            "\"message\":{\"message_id\":10,\"chat\":{\"id\":-100123}}}}]");
        var api = CreateClient(handler);

        var (ok, error, updates) = await api.GetUpdatesAsync("123:ABC", 104);

        Assert.True(ok, error);
        var update = Assert.Single(updates!);
        Assert.Equal(105, update.UpdateId);
        Assert.True(update.IsCallback);
        Assert.Equal("-100123", update.ChatId);
        Assert.Equal("cq-1", update.CallbackQueryId);
        Assert.Equal("wc1c:" + guid + ":yes", update.CallbackData);
        Assert.Equal(10, update.MessageId);
    }

    [Fact]
    public async Task GetUpdates_EmptyResult_ReturnsEmptyList()
    {
        var handler = new TelegramTestHttpHandler();
        var api = CreateClient(handler);

        var (ok, error, updates) = await api.GetUpdatesAsync("123:ABC", null);

        Assert.True(ok, error);
        Assert.NotNull(updates);
        Assert.Empty(updates);
    }

    [Fact]
    public async Task AnswerCallbackQuery_SendsCallbackId()
    {
        var handler = new TelegramTestHttpHandler();
        var api = CreateClient(handler);

        var (ok, error) = await api.AnswerCallbackQueryAsync("123:ABC", "cq-1", "✅ Подтверждено");

        Assert.True(ok, error);
        Assert.Equal("cq-1", handler.ValueOf("answerCallbackQuery", "callback_query_id"));
    }

    [Fact]
    public async Task EditMessageText_ClearsKeyboard()
    {
        var handler = new TelegramTestHttpHandler();
        var api = CreateClient(handler);

        var (ok, error) = await api.EditMessageTextAsync("123:ABC", "-100123", 10, "✅ Готово");

        Assert.True(ok, error);
        Assert.Equal("-100123", handler.ValueOf("editMessageText", "chat_id"));
        Assert.Equal("10", handler.ValueOf("editMessageText", "message_id"));
        Assert.Equal("{\"inline_keyboard\":[]}", handler.ValueOf("editMessageText", "reply_markup"));
    }

    [Fact]
    public async Task ApiError_NotOk_MasksTokenAndReturnsError()
    {
        var handler = new TelegramTestHttpHandler { ResponseForSendMessage = "{\"ok\":false,\"error_code\":401,\"description\":\"Unauthorized\"}" };
        var api = CreateClient(handler);

        var (ok, error, _) = await api.SendMessageAsync("SECRET", "42", "x");
        Assert.False(ok);
        Assert.DoesNotContain("SECRET", error);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task NetworkError_ReturnsErrorAndMasksToken()
    {
        var api = new TelegramBotApiClient(null, new FailingHandler());
        var (ok, error, _) = await api.SendMessageAsync("SECRET", "42", "x");
        Assert.False(ok);
        Assert.DoesNotContain("SECRET", error);
        Assert.NotEmpty(error);
    }

    // ---------- callback-data и сравнение чатов (TelegramNotificationChannel) ----------

    [Fact]
    public void CallbackData_RoundTrips()
    {
        var guid = Guid.NewGuid();
        var yes = TelegramNotificationChannel.BuildCallbackData(guid, approve: true);
        var no = TelegramNotificationChannel.BuildCallbackData(guid, approve: false);

        Assert.True(TelegramNotificationChannel.TryParseCallbackData(yes, out var yesId, out var yesApprove));
        Assert.Equal(guid, yesId);
        Assert.True(yesApprove);

        Assert.True(TelegramNotificationChannel.TryParseCallbackData(no, out _, out var noApprove));
        Assert.False(noApprove);
    }

    [Fact]
    public void CallbackData_Invalid_ReturnsFalse()
    {
        Assert.False(TelegramNotificationChannel.TryParseCallbackData(null, out _, out _));
        Assert.False(TelegramNotificationChannel.TryParseCallbackData("wc1c:garbage:yes", out _, out _));
        Assert.False(TelegramNotificationChannel.TryParseCallbackData("other:00000000-0000-0000-0000-000000000001:yes", out _, out _));
    }

    [Fact]
    public void SameChatId_ComparesNumericAndNames()
    {
        Assert.True(TelegramNotificationChannel.SameChatId("-1001234567890", "-1001234567890"));
        Assert.True(TelegramNotificationChannel.SameChatId("123456", "123456"));
        Assert.True(TelegramNotificationChannel.SameChatId("@my_channel", "@my_channel"));
        Assert.True(TelegramNotificationChannel.SameChatId("@my_channel", "@MY_CHANNEL"));
        Assert.False(TelegramNotificationChannel.SameChatId("123", "124"));
        Assert.False(TelegramNotificationChannel.SameChatId("123", "@my_channel"));
        Assert.False(TelegramNotificationChannel.SameChatId(null, "123"));
    }

    // ---------- dev-отправка в обход фильтра событий ----------

    [Fact]
    public async Task SendDevAsync_GeneralEvent_BypassesEventFilter()
    {
        var store = new NotificationSettingsStore(directoryOverride: _dir);
        store.Save(new NotificationChannelSettings
        {
            TelegramEnabled = true,
            TelegramBotToken = "123:ABC",
            TelegramChatId = "-100123",
        });
        var handler = new TelegramTestHttpHandler();
        var channel = new TelegramNotificationChannel(store, null, handler);

        // Event.General внешние каналы не получают по фильтру — dev-отправка должна пройти.
        var (ok, error) = await channel.SendDevAsync(new NotificationMessage
        {
            Title = "Сборка",
            Message = "OK",
            Kind = NotificationKind.Success,
            Event = NotificationEvent.General,
        });

        Assert.True(ok, error);
        Assert.Contains(handler.Calls, c => c.Method == "sendMessage");
        Assert.Contains("Сборка", handler.ValueOf("sendMessage", "text"));
    }

    [Fact]
    public async Task SendDevAsync_NotConfigured_ReturnsError()
    {
        var store = new NotificationSettingsStore(directoryOverride: _dir);
        var channel = new TelegramNotificationChannel(store, null, new TelegramTestHttpHandler());

        var (ok, error) = await channel.SendDevAsync(new NotificationMessage { Message = "x" });

        Assert.False(ok);
        Assert.Equal("not_configured", error);
    }

    // ---------- прокси (сети с блокировкой api.telegram.org) ----------

    [Fact]
    public void HttpProxy_ValidUrl_CreatesHandler()
    {
        // HTTP-прокси (типичный локальный порт VPN-клиента) — handler с прокси.
        var handler = TelegramHttpProxy.CreateHandlerFor("http://127.0.0.1:8080");
        Assert.NotNull(handler);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-uri")]
    public void HttpProxy_InvalidOrEmpty_ReturnsNull(string? url)
    {
        Assert.Null(TelegramHttpProxy.CreateHandlerFor(url));
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Network is down");
    }
}
