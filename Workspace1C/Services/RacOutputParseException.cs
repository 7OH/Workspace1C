using System;

namespace Workspace1C.Services;

/// <summary>
/// Вывод утилиты rac не удалось распознать: rac завершился успешно (exit=0), вывод НЕ пуст,
/// но ни табличный разбор, ни формат блоков «ключ : значение» не дали ни одной строки данных
/// (issue #324). Обычно означает новую версию формата вывода rac — пользователю нужно понятное
/// сообщение и остановка бесконечного автообновления вместо тихих повторов каждые 5 с.
/// </summary>
public sealed class RacOutputParseException : Exception
{
    public RacOutputParseException(string message)
        : base(message)
    {
    }

    public RacOutputParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Создаёт исключение с префиксом вывода rac (первые ~200 символов, пароль
    /// маскируется <see cref="SensitiveDataMasker.MaskRacPassword"/>) — при новом
    /// формате причина видна сразу в тексте ошибки окна/issue, без чтения журнала
    /// (issue #25). Префикс строится <see cref="RacClient.Preview"/> (однострочный,
    /// до 200 символов).
    /// </summary>
    public static RacOutputParseException WithOutputPrefix(string commandName, string output)
    {
        var preview = RacClient.Preview(output);
        var masked = SensitiveDataMasker.MaskRacPassword(preview);
        return new RacOutputParseException(
            $"Вывод rac «{commandName}» не распознан (возможно, новая версия формата): " +
            $"код 0, но 0 строк данных при непустом выводе. Начало вывода: «{masked}»");
    }
}