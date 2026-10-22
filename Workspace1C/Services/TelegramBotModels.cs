using System;

namespace Workspace1C.Services;

/// <summary>
/// Модели Bot API Telegram для интерактивного режима подтверждений (CLI-команды
/// <c>--telegram-send</c> / <c>--telegram-confirm</c>, выпуск 1.0.0.36+): inline-кнопка,
/// результат отправки и «сырое» обновление <c>getUpdates</c>, сведённое к нужным полям
/// <c>callback_query</c>. Чистые данные без HTTP и UI — пригодны для юнит-тестов.
/// </summary>
public sealed record TelegramInlineButton(string Text, string CallbackData);

/// <summary>Результат успешной отправки сообщения: chat_id и message_id (нужен для editMessage*).</summary>
public sealed record TelegramSendResult(string ChatId, long MessageId);

/// <summary>
/// Обновление из <c>getUpdates</c>, приведённое к полям, которые понимает
/// <see cref="TelegramConfirmationService"/>: идентификатор обновления (подтверждение offset),
/// чат отправителя и данные callback_query (если нажата кнопка). Уже обработанный
/// <c>update_id</c> не должен попадать сюда повторно — offset хранится в <see cref="TelegramOffsetStore"/>.
/// </summary>
public sealed record TelegramUpdate(
    long UpdateId,
    string ChatId,
    string? CallbackQueryId,
    string? CallbackData,
    long? MessageId)
{
    /// <summary>Признак «пользователь нажал кнопку» (есть данные callback_query).</summary>
    public bool IsCallback => !string.IsNullOrEmpty(CallbackQueryId);
}

/// <summary>Итог запроса подтверждения: нажата кнопка «Да», «Нет» либо истекло время ожидания.</summary>
public enum TelegramConfirmationOutcome
{
    /// <summary>Подтверждено (кнопка «Да»). Рабочий процесс может продолжать.</summary>
    Yes,

    /// <summary>Отклонено (кнопка «Нет»). Рабочий процесс должен остановиться.</summary>
    No,

    /// <summary>Время ожидания истекло — ответа не было.</summary>
    Timeout
}

/// <summary>Результат запроса подтверждения.</summary>
public sealed record TelegramConfirmationResult(TelegramConfirmationOutcome Outcome, string Text)
{
    /// <summary>true — пользователь нажал «Да».</summary>
    public bool Approved => Outcome == TelegramConfirmationOutcome.Yes;

    /// <summary>Машинное имя исхода для контракта CLI (<c>yes</c>/<c>no</c>/<c>timeout</c>).</summary>
    public string MachineName => Outcome switch
    {
        TelegramConfirmationOutcome.Yes => "yes",
        TelegramConfirmationOutcome.No => "no",
        _ => "timeout"
    };
}
