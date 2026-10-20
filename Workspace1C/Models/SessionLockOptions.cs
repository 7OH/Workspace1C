using System;

namespace Workspace1C.Models;

/// <summary>
/// Параметры установки блокировки сеансов информационной базы (функция №20 StartManager):
/// время начала, длительность и текст сообщения пользователям.
/// Чистая .NET-модель без UI-зависимостей — используется и Windows/WPF, и Linux/Avalonia.
/// </summary>
public class SessionLockOptions
{
    /// <summary>
    /// Время начала блокировки. По умолчанию устанавливается на «+5 минут»
    /// от текущего момента (как в StartManager 1.4).
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Длительность блокировки. По умолчанию 30 минут (как в StartManager 1.4).
    /// </summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Текст сообщения пользователям. Может содержать параметры-плейсхолдеры
    /// <c>{ДатаНач}</c> / <c>{ДатаКон}</c>, которые подставляются фактическими
    /// значениями дат начала и окончания блокировки перед запуском 1С.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Секретный код разрешения (issue #20): пятый параметр файла блокировки
    /// <c>1cv8.cdn</c> файловой ИБ. Позволяет пользователю снять блокировку
    /// вручную, указав код в диалоге 1С. Необязательный — пустая строка
    /// означает «код не задан».
    /// </summary>
    public string PermissionCode { get; set; } = string.Empty;

    /// <summary>Момент окончания блокировки = время начала + длительность.</summary>
    public DateTime EndTime => StartTime + Duration;

    /// <summary>
    /// Длительность в минутах (для передачи в командную строку 1С /LockIB).
    /// </summary>
    public int DurationMinutes => Math.Max(0, (int)Math.Round(Duration.TotalMinutes));

    /// <summary>
    /// Подставляет плейсхолдеры <c>{ДатаНач}</c> / <c>{ДатаКон}</c> в текст
    /// сообщения фактическими значениями дат. Если плейсхолдеры отсутствуют —
    /// возвращает текст без изменений.
    /// </summary>
    public string BuildFinalMessage()
    {
        var message = Message ?? string.Empty;
        message = message.Replace("{ДатаНач}", StartTime.ToString("dd.MM.yyyy HH:mm"), StringComparison.Ordinal);
        message = message.Replace("{ДатаКон}", EndTime.ToString("dd.MM.yyyy HH:mm"), StringComparison.Ordinal);
        return message;
    }

    /// <summary>
    /// Строка сеансов для командной строки 1С в формате /LockIB:
    /// «время начала;длительность в минутах;текст сообщения».
    /// Время — в формате ДД.ММ.ГГГГ ЧЧ:ММ:СС, принятом платформой 1С.
    /// </summary>
    public string BuildSessionLockString()
    {
        var start = StartTime.ToString("dd.MM.yyyy HH:mm:ss");
        return $"{start};{DurationMinutes};{BuildFinalMessage()}";
    }

    /// <summary>
    /// Строка файла блокировки <c>1cv8.cdn</c> (issue #20) для файловой ИБ:
    /// <c>{1,ГГГГММДДЧЧММСС,ГГГГММДДЧЧММСС,"причина","код разрешения"}</c>.
    /// Параметры: 1 — флаг активности; 2–3 — дата начала/окончания в формате
    /// ГГГГММДДЧЧММСС; 4 — текст причины (с подставленными {ДатаНач}/{ДатаКон});
    /// 5 — секретный код разрешения. Кавычки внутри значений удваиваются
    /// (правило строки подключения 1С, как в <see cref="ConnectionSettings"/>).
    /// </summary>
    public string BuildCdnString()
    {
        var start = StartTime.ToString("yyyyMMddHHmmss");
        var end = EndTime.ToString("yyyyMMddHHmmss");
        var message = EscapeCdnValue(BuildFinalMessage());
        var code = EscapeCdnValue(PermissionCode ?? string.Empty);
        return $"{{1,{start},{end},\"{message}\",\"{code}\"}}";
    }

    /// <summary>Экранирует значение для 1cv8.cdn: кавычка внутри значения удваивается.</summary>
    private static string EscapeCdnValue(string value) => value.Replace("\"", "\"\"");
}