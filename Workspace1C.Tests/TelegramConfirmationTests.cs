using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты <see cref="TelegramConfirmationService"/>: отправка запроса с кнопками и обработка
/// callback_query через длинный поллинг (подменный <see cref="TelegramTestHttpHandler"/>),
/// таймаут, чужой чат, блокировка и не настроенный канал.
/// </summary>
public sealed class TelegramConfirmationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "w1c_tg_confirm_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* каталог может не существовать */ }
    }

    private (TelegramOffsetStore Offsets, TelegramTestHttpHandler Handler, TelegramConfirmationService Service) Build(
        bool saved = true, string chatId = "-100123")
    {
        var handler = new TelegramTestHttpHandler();
        var store = new NotificationSettingsStore(directoryOverride: _dir);
        if (saved)
        {
            store.Save(new NotificationChannelSettings
            {
                TelegramEnabled = true,
                TelegramBotToken = "123:ABC",
                TelegramChatId = chatId,
            });
        }

        var offsets = new TelegramOffsetStore(directoryOverride: _dir);
        var service = new TelegramConfirmationService(store, new TelegramBotApiClient(null, handler), offsets, _dir);
        return (offsets, handler, service);
    }

    [Fact]
    public async Task Confirm_YesButton_ReturnsYesAndClosesButtons()
    {
        var (offsets, handler, service) = Build();
        handler.GetUpdatesQueue.Add("[]");
        handler.GetUpdatesQueue.Add(
            "[{\"update_id\":105,\"callback_query\":{\"id\":\"cq-1\",\"data\":\"wc1c:[GUID]:yes\"," +
            "\"message\":{\"message_id\":10,\"chat\":{\"id\":-100123}}}}]");

        var (ok, error, result) = await service.ConfirmAsync("Опубликовать релиз?", null, TimeSpan.FromSeconds(30));

        Assert.True(ok, error);
        Assert.Equal(TelegramConfirmationOutcome.Yes, result!.Outcome);
        Assert.True(result.Approved);
        // offset сохранён (обработан update_id 105 → следующий запрос с 106).
        Assert.Equal(106, offsets.Load());
        // Нажатие подтверждено и текст сообщения заменён, кнопки сняты.
        Assert.Equal("cq-1", handler.ValueOf("answerCallbackQuery", "callback_query_id"));
        var edited = handler.ValueOf("editMessageText", "text");
        Assert.NotNull(edited);
        Assert.StartsWith("✅ Подтверждено", edited);
        Assert.Equal("{\"inline_keyboard\":[]}", handler.ValueOf("editMessageText", "reply_markup"));
    }

    [Fact]
    public async Task Confirm_NoButton_ReturnsNo()
    {
        var (_, handler, service) = Build();
        handler.GetUpdatesQueue.Add(
            "[{\"update_id\":105,\"callback_query\":{\"id\":\"cq-2\",\"data\":\"wc1c:[GUID]:no\"," +
            "\"message\":{\"message_id\":10,\"chat\":{\"id\":-100123}}}}]");

        var (ok, error, result) = await service.ConfirmAsync("Продолжать?", new[] { "Да", "Нет" }, TimeSpan.FromSeconds(30));

        Assert.True(ok, error);
        Assert.Equal(TelegramConfirmationOutcome.No, result!.Outcome);
        Assert.False(result.Approved);
        Assert.StartsWith("❌ Отклонено", handler.ValueOf("editMessageText", "text"));
    }

    [Fact]
    public async Task Confirm_Timeout_ReturnsTimeoutAndMarkStatus()
    {
        var (_, handler, service) = Build();

        var (ok, error, result) = await service.ConfirmAsync("Вопрос на 200 мс", null, TimeSpan.FromMilliseconds(150));

        Assert.True(ok, error);
        Assert.Equal(TelegramConfirmationOutcome.Timeout, result!.Outcome);
        // Сообщение помечено как истёкшее.
        var edited = handler.ValueOf("editMessageText", "text");
        Assert.NotNull(edited);
        Assert.Contains("истекло", edited);
    }

    [Fact]
    public async Task Confirm_ForeignChat_IgnoredUntilTrustedAnswer()
    {
        var (offsets, handler, service) = Build();
        // Первый callback — из чужого чата (-999), второй — правильный (-100123).
        handler.GetUpdatesQueue.Add(
            "[{\"update_id\":104,\"callback_query\":{\"id\":\"foreign\",\"data\":\"wc1c:[GUID]:yes\"," +
            "\"message\":{\"message_id\":10,\"chat\":{\"id\":-999}}}}," +
            "{\"update_id\":105,\"callback_query\":{\"id\":\"cq-3\",\"data\":\"wc1c:[GUID]:yes\"," +
            "\"message\":{\"message_id\":10,\"chat\":{\"id\":-100123}}}}]");

        var (ok, error, result) = await service.ConfirmAsync("Вопрос", null, TimeSpan.FromSeconds(30));

        Assert.True(ok, error);
        Assert.Equal(TelegramConfirmationOutcome.Yes, result!.Outcome);
        Assert.Equal(106, offsets.Load());
        // Чужой callback не подтверждался: только один answerCallbackQuery.
        Assert.Equal(1, handler.Calls.Count(c => c.Method == "answerCallbackQuery"));
        Assert.Equal("cq-3", handler.ValueOf("answerCallbackQuery", "callback_query_id"));
    }

    [Fact]
    public async Task Confirm_CustomButtonLabels_UseProvidedText()
    {
        var (_, handler, service) = Build();
        handler.GetUpdatesQueue.Add(
            "[{\"update_id\":105,\"callback_query\":{\"id\":\"cq-4\",\"data\":\"wc1c:[GUID]:yes\"," +
            "\"message\":{\"message_id\":10,\"chat\":{\"id\":-100123}}}}]");

        await service.ConfirmAsync("Публиковать?", new[] { "Разрешить", "Отказать" }, TimeSpan.FromSeconds(30));

        var markup = handler.ValueOf("sendMessage", "reply_markup");
        Assert.NotNull(markup);
        Assert.Contains("Разрешить", markup);
        Assert.Contains("Отказать", markup);
    }

    [Fact]
    public async Task Confirm_Busy_ReturnsErrorWithoutSending()
    {
        var (_, handler, service) = Build();
        var lockFile = new TelegramConfirmationLock(_dir);
        Assert.True(lockFile.TryAcquire(Guid.NewGuid(), TimeSpan.FromMinutes(10)));
        try
        {
            var (ok, error, result) = await service.ConfirmAsync("Вопрос", null, TimeSpan.FromSeconds(30));

            Assert.False(ok);
            Assert.Equal("busy", error);
            Assert.Null(result);
            Assert.DoesNotContain(handler.Calls, c => c.Method == "sendMessage");
        }
        finally
        {
            lockFile.Release();
        }
    }

    [Fact]
    public async Task Confirm_NotConfigured_ReturnsError()
    {
        var (_, handler, service) = Build(saved: false);

        var (ok, error, result) = await service.ConfirmAsync("Вопрос", null, TimeSpan.FromSeconds(30));

        Assert.False(ok);
        Assert.Equal("not_configured", error);
        Assert.Null(result);
        Assert.Empty(handler.Calls);
    }
}
