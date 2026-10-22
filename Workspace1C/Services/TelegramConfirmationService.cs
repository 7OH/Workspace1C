using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Интерактивные подтверждения через Telegram (выпуск 1.0.0.36+, CLI-команда
/// <c>--telegram-confirm</c>): отправляет сообщение с inline-кнопками «Да»/«Нет» и ждёт
/// нажатие через длинный поллинг <c>getUpdates</c> в текущем процессе. Единственный
/// активный запрос гарантируется lock-файлом; offset сохраняется в <see cref="TelegramOffsetStore"/>,
/// чтобы история не перечитывалась между запусками. Ответы принимаются только из чата,
/// заданного в настройках канала (<see cref="TelegramNotificationChannel.SameChatId"/>).
/// </summary>
public sealed class TelegramConfirmationService
{
    /// <summary>Пауза после пустого ответа getUpdates (прод. поллинг вернётся быстрее).</summary>
    public const int EmptyPollDelayMs = 200;

    /// <summary>Пауза перед повтором при ошибке сети/API.</summary>
    public const int RetryDelayMs = 2000;

    private readonly INotificationSettingsStore _store;
    private readonly TelegramBotApiClient _api;
    private readonly TelegramOffsetStore _offsets;
    private readonly TelegramConfirmationLock _lock;
    private readonly IAppLogger? _logger;

    /// <param name="store">Настройки каналов уведомлений (токен, chat_id).</param>
    /// <param name="api">HTTP-клиент Bot API (подменяемый в тестах).</param>
    /// <param name="offsets">Хранилище последнего обработанного update_id.</param>
    /// <param name="lockDirectoryOverride">Явный каталог lock-файла (юнит-тесты); null — каталог offset.</param>
    /// <param name="logger">Журнал (может быть null).</param>
    public TelegramConfirmationService(
        INotificationSettingsStore store,
        TelegramBotApiClient api,
        TelegramOffsetStore offsets,
        string? lockDirectoryOverride = null,
        IAppLogger? logger = null)
    {
        _store = store;
        _api = api;
        _offsets = offsets;
        _logger = logger;
        _lock = new TelegramConfirmationLock(
            lockDirectoryOverride ?? Path.GetDirectoryName(offsets.FilePath) ?? string.Empty,
            logger);
    }

    /// <summary>
    /// Посылает запрос подтверждения и ожидает ответ. Коды результата — <see cref="TelegramConfirmationOutcome"/>:
    /// <see cref="TelegramConfirmationOutcome.Yes"/> / <see cref="TelegramConfirmationOutcome.No"/> при нажатии кнопки,
    /// <see cref="TelegramConfirmationOutcome.Timeout"/> по истечении <paramref name="timeout"/>.
    /// Ошибка <c>not_configured</c> — токен/чат не заполнены, <c>busy</c> — уже есть активный запрос.
    /// </summary>
    public async Task<(bool Ok, string Error, TelegramConfirmationResult? Result)> ConfirmAsync(
        string? prompt,
        IReadOnlyList<string>? buttonLabels,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        NotificationChannelSettings settings;
        try { settings = _store.Load(); }
        catch (Exception ex) { return (false, ex.Message, null); }
        if (!TelegramNotificationChannel.IsConfigured(settings))
            return (false, "not_configured", null);

        var token = settings.TelegramBotToken.Trim();
        var (chatId, threadId) = TelegramNotificationChannel.ParseChatTarget(settings.TelegramChatId);
        if (string.IsNullOrWhiteSpace(chatId))
            return (false, "not_configured", null);

        var requestId = Guid.NewGuid();
        if (!_lock.TryAcquire(requestId, timeout + TimeSpan.FromMinutes(1)))
            return (false, "busy", null);

        try
        {
            var (sendOk, sendError, messageId) = await SendPromptAsync(
                token, chatId, threadId, requestId, prompt, buttonLabels, cancellationToken).ConfigureAwait(false);
            if (!sendOk)
                return (false, sendError, null);
            return await WaitForAnswerAsync(
                token, chatId, requestId, messageId!.Value, prompt, timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ------------------------------------------------------------------ отправка запроса

    private async Task<(bool Ok, string Error, long? MessageId)> SendPromptAsync(
        string token,
        string chatId,
        string? threadId,
        Guid requestId,
        string? prompt,
        IReadOnlyList<string>? buttonLabels,
        CancellationToken cancellationToken)
    {
        var (labelFor, labelAgainst) = ResolveButtonLabels(buttonLabels);
        var buttons = new[]
        {
            new TelegramInlineButton(labelFor, TelegramNotificationChannel.BuildCallbackData(requestId, approve: true)),
            new TelegramInlineButton(labelAgainst, TelegramNotificationChannel.BuildCallbackData(requestId, approve: false)),
        };
        var text = "🙋 Подтверждение запроса\n\n" + (string.IsNullOrWhiteSpace(prompt) ? "Разрешить выполнение?" : prompt.Trim());
        var (ok, error, result) = await _api.SendMessageAsync(token, chatId, text, buttons, threadId, cancellationToken)
            .ConfigureAwait(false);
        if (!ok || result is null || result.MessageId <= 0)
            return (false, string.IsNullOrEmpty(error) ? "send_failed" : error, null);
        _logger?.Info($"Telegram: запрос подтверждения {requestId} отправлен");
        return (true, string.Empty, result.MessageId);
    }

    /// <summary>Тексты кнопок: первый — «Да», второй — «Нет»; недостающие — значения по умолчанию.</summary>
    private static (string Yes, string No) ResolveButtonLabels(IReadOnlyList<string>? buttonLabels)
    {
        var yes = buttonLabels is { Count: > 0 } ? buttonLabels[0]?.Trim() : null;
        var no = buttonLabels is { Count: > 1 } ? buttonLabels[1]?.Trim() : null;
        return (string.IsNullOrWhiteSpace(yes) ? "Да" : yes!, string.IsNullOrWhiteSpace(no) ? "Нет" : no!);
    }

    // ------------------------------------------------------------------ ожидание ответа

    private async Task<(bool Ok, string Error, TelegramConfirmationResult? Result)> WaitForAnswerAsync(
        string token,
        string chatId,
        Guid requestId,
        long messageId,
        string? prompt,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        var lastProcessed = _offsets.Load();
        // getUpdates принимает первый ОБРАБАТЫВАЕМЫЙ update_id — после подтверждённого (+1).
        long? offset = lastProcessed is { } lp ? lp + 1 : null;

        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            var (ok, error, updates) = await _api.GetUpdatesAsync(token, offset, cancellationToken).ConfigureAwait(false);
            if (!ok)
            {
                _logger?.Warn($"Telegram: ошибка поллинга (повтор через {RetryDelayMs} мс): {error}");
                await Task.Delay(RetryDelayMs, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (updates is { Count: > 0 })
            {
                foreach (var update in updates)
                {
                    if (update.UpdateId > 0)
                        offset = update.UpdateId + 1;
                    if (!update.IsCallback)
                        continue;
                    // Ответы из чужих чатов игнорируются (защита от подмены бот-токенов).
                    if (!TelegramNotificationChannel.SameChatId(chatId, update.ChatId))
                        continue;
                    if (!TelegramNotificationChannel.TryParseCallbackData(update.CallbackData, out var answerRequestId, out var approve)
                        || answerRequestId != requestId)
                        continue;

                    await FinalizeAnsweredAsync(token, chatId, messageId, update, approve, prompt, cancellationToken)
                        .ConfigureAwait(false);
                    _offsets.Save(offset ?? 0);
                    return (true, string.Empty, new TelegramConfirmationResult(
                        approve ? TelegramConfirmationOutcome.Yes : TelegramConfirmationOutcome.No,
                        approve ? "confirmed" : "rejected"));
                }
            }

            _offsets.Save(offset ?? 0);
            // Пустой ответ (долгий поллинг завершился без событий) — короткая пауза и повтор.
            await Task.Delay(EmptyPollDelayMs, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await _api.EditMessageTextAsync(
                    token, chatId, messageId,
                    "⏰ " + (string.IsNullOrWhiteSpace(prompt) ? "Время ожидания истекло" : "Время ожидания истекло:\n" + prompt.Trim()),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // Статус «истекло» — вспомогательный; при неудаче редактирования результат тот же.
        }
        return (true, string.Empty, new TelegramConfirmationResult(TelegramConfirmationOutcome.Timeout, "timeout"));
    }

    private async Task FinalizeAnsweredAsync(
        string token,
        string chatId,
        long messageId,
        TelegramUpdate update,
        bool approve,
        string? prompt,
        CancellationToken cancellationToken)
    {
        var status = approve ? "✅ Подтверждено" : "❌ Отклонено";
        if (!string.IsNullOrEmpty(update.CallbackQueryId))
            await _api.AnswerCallbackQueryAsync(token, update.CallbackQueryId, status, cancellationToken).ConfigureAwait(false);
        var promptText = string.IsNullOrWhiteSpace(prompt) ? string.Empty : "\n" + prompt.Trim();
        await _api.EditMessageTextAsync(token, chatId, messageId, status + promptText, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Файловая блокировка «один активный запрос подтверждения»: <c>telegram.confirm.lock</c>
/// в каталоге данных профиля. Владелец прерван жёстко (kill/Power) — блок остаётся,
/// поэтому устаревший блок (старше <paramref name="staleAfter"/> при захвате) перезаписывается.
/// </summary>
public sealed class TelegramConfirmationLock
{
    /// <summary>Имя файла блокировки активного запроса.</summary>
    public const string FileName = "telegram.confirm.lock";

    private readonly string _directory;
    private readonly IAppLogger? _logger;

    public TelegramConfirmationLock(string directory, IAppLogger? logger = null)
    {
        _directory = string.IsNullOrWhiteSpace(directory) ? Directory.GetCurrentDirectory() : directory;
        _logger = logger;
    }

    /// <summary>Полный путь файла блокировки.</summary>
    public string FilePath => Path.Combine(_directory, FileName);

    /// <summary>
    /// Пытается захватить блок. Возвращает false, если активный (не старше
    /// <paramref name="staleAfter"/>) блок уже существует — значит, запрос подтверждения
    /// выполняется другим процессом.
    /// </summary>
    public bool TryAcquire(Guid requestId, TimeSpan staleAfter)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            if (File.Exists(FilePath))
            {
                var created = ReadCreatedUtc();
                if (created is { } c && DateTime.UtcNow - c < staleAfter)
                    return false;
                // Устаревший блок — перезаписываем (владелец, вероятно, аварийно завершился).
            }

            var payload = JsonSerializer.Serialize(new
            {
                pid = Environment.ProcessId,
                requestId,
                createdUtc = DateTime.UtcNow,
            });
            var tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, payload);
            File.Move(tempPath, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Telegram: не удалось создать блок подтверждения: {ex.Message}");
            return false;
        }
    }

    /// <summary>Снимает блокировку (не ошибка, если файла уже нет).</summary>
    public void Release()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Telegram: не удалось снять блок подтверждения: {ex.Message}");
        }
    }

    private DateTime? ReadCreatedUtc()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;
            var json = File.ReadAllText(FilePath);
            // Подпись lock-файла собственная — читаем createdUtc напрямую.
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("createdUtc", out var el) && el.TryGetDateTime(out var dt)
                ? dt.ToUniversalTime()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
