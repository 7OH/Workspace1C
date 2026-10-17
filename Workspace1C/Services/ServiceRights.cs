using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Workspace1C.Services;

/// <summary>
/// Итог выдачи прав на управление службами (issue #11, 7OH 17.10).
/// </summary>
public sealed class ServiceRightsResult
{
    /// <summary>Права выданы этим запуском.</summary>
    public List<string> Granted { get; } = new();

    /// <summary>Права уже были (ничего не менялось).</summary>
    public List<string> AlreadyGranted { get; } = new();

    /// <summary>Не удалось: имя службы → причина.</summary>
    public Dictionary<string, string> Failed { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Пользователь отменил запрос повышения прав (UAC).</summary>
    public bool Canceled { get; set; }

    /// <summary>Выдача прав на этой платформе не поддерживается.</summary>
    public bool NotSupported { get; set; }

    /// <summary>Общая ошибка (не удалось запустить/дождаться выполнения).</summary>
    public string Error { get; set; } = string.Empty;

    public static ServiceRightsResult Unsupported() => new() { NotSupported = true };
}

/// <summary>
/// Права текущего пользователя на управление службами Windows без запуска программы от
/// администратора (issue #11, 7OH 17.10). Дескриптор безопасности службы (SDDL) читается
/// без повышения прав (<c>sc sdshow</c>); в DACL добавляется разрешающая запись для SID
/// пользователя; новые дескрипторы записываются ОДНИМ командным файлом
/// <c>sc sdset</c>, запущенным с повышением прав — один запрос UAC на все службы.
/// Здесь — чистые функции (SDDL, командный файл, разбор результата), покрытые тестами.
/// </summary>
public static class ServiceRights
{
    /// <summary>
    /// Права записи для пользователя: CC — чтение конфигурации, LC — опрос статуса,
    /// SW — перечисление зависимых, RP — запуск, WP — остановка, DT — пауза/продолжение,
    /// LO — опрос, CR — пользовательские команды, RC — чтение дескриптора.
    /// Изменение конфигурации (DC), удаление и смена прав не выдаются.
    /// </summary>
    public const string ControlRights = "CCLCSWRPWPDTLOCRRC";

    private static readonly Regex AceRegex = new(@"\(([^()]*)\)", RegexOptions.Compiled);

    /// <summary>Строка SDDL из вывода <c>sc sdshow</c> (первая непустая строка с «D:»/«O:»/«G:»).</summary>
    public static string? ParseSdShowOutput(string? output)
    {
        foreach (var raw in (output ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("D:", StringComparison.Ordinal) ||
                line.StartsWith("O:", StringComparison.Ordinal) ||
                line.StartsWith("G:", StringComparison.Ordinal))
                return line;
        }
        return null;
    }

    /// <summary>
    /// Есть ли в DACL разрешающая запись для <paramref name="sid"/> с правами запуска и
    /// остановки (RP и WP).
    /// </summary>
    public static bool HasControlAce(string sddl, string sid)
    {
        var dacl = DaclPart(sddl);
        foreach (Match m in AceRegex.Matches(dacl))
        {
            var parts = m.Groups[1].Value.Split(';');
            if (parts.Length < 6)
                continue;
            if (!string.Equals(parts[0], "A", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(parts[5].Trim(), sid, StringComparison.OrdinalIgnoreCase))
                continue;
            var rights = parts[2];
            if (ContainsRight(rights, "RP") && ContainsRight(rights, "WP"))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Добавляет в DACL разрешающую запись <c>(A;;CCLCSWRPWPDTLOCRRC;;;SID)</c> перед
    /// первой записью DACL (после флагов «D:P», «D:AI» и т.п.). Если такая запись уже есть —
    /// возвращает дескриптор без изменений. Дескриптор без DACL — null (не трогаем).
    /// </summary>
    public static string? AddControlAce(string sddl, string sid)
    {
        if (string.IsNullOrWhiteSpace(sddl) || string.IsNullOrWhiteSpace(sid))
            return null;
        if (HasControlAce(sddl, sid))
            return sddl;

        var d = sddl.IndexOf("D:", StringComparison.Ordinal);
        if (d < 0)
            return null;
        var insertAt = sddl.IndexOf('(', d);
        var sacl = sddl.IndexOf("S:", d, StringComparison.Ordinal);
        if (insertAt < 0 || (sacl >= 0 && insertAt > sacl))
            insertAt = sacl >= 0 ? sacl : sddl.Length; // пустой DACL — запись в конец его части
        var ace = $"(A;;{ControlRights};;;{sid.Trim()})";
        return sddl.Insert(insertAt, ace);
    }

    /// <summary>
    /// Имя службы безопасно для командного файла (без символов, которые cmd трактует
    /// особо: кавычек, %, &amp;, |, &lt;, &gt;, ^).
    /// </summary>
    public static bool IsSafeName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.IndexOfAny(new[] { '"', '%', '&', '|', '<', '>', '^', '\r', '\n' }) < 0;

    /// <summary>
    /// Командный файл записи дескрипторов: для каждой службы — <c>sc.exe sdset</c> и строка
    /// «имя=код» в файл результата (перенаправление в начале строки — иначе «0&gt;&gt;»
    /// cmd принял бы за номер потока).
    /// </summary>
    public static string BuildBatch(IEnumerable<(string Name, string Sddl)> items, string resultFile)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine($"type nul > \"{resultFile}\"");
        foreach (var (name, sddl) in items)
        {
            var safeSddl = sddl.Replace("%", "%%");
            sb.AppendLine($"sc.exe sdset \"{name}\" \"{safeSddl}\" >nul 2>&1");
            sb.AppendLine($">>\"{resultFile}\" echo {name}=%errorlevel%");
        }
        sb.AppendLine($">>\"{resultFile}\" echo __done__=0");
        return sb.ToString();
    }

    /// <summary>Разбор файла результата: имя службы → код возврата sc (0 — успех).</summary>
    public static Dictionary<string, int> ParseResultFile(string? text)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            var eq = line.LastIndexOf('=');
            if (eq <= 0)
                continue;
            var name = line.Substring(0, eq).Trim();
            if (int.TryParse(line.Substring(eq + 1).Trim(), out var code))
                result[name] = code;
        }
        return result;
    }

    /// <summary>Объединяет итог по службам из кодов sc и списка служб, которым права выдавались.</summary>
    public static void ApplyResults(ServiceRightsResult result, IEnumerable<string> attempted, IReadOnlyDictionary<string, int> codes)
    {
        foreach (var name in attempted)
        {
            if (codes.TryGetValue(name, out var code) && code == 0)
                result.Granted.Add(name);
            else
                result.Failed[name] = codes.TryGetValue(name, out var c) ? $"sc sdset: {c}" : "нет результата";
        }
    }

    private static string DaclPart(string sddl)
    {
        var d = sddl.IndexOf("D:", StringComparison.Ordinal);
        if (d < 0)
            return string.Empty;
        var s = sddl.IndexOf("S:", d, StringComparison.Ordinal);
        return s < 0 ? sddl.Substring(d) : sddl.Substring(d, s - d);
    }

    private static bool ContainsRight(string rights, string right)
    {
        for (var i = 0; i + 1 < rights.Length; i += 2)
        {
            if (string.Equals(rights.Substring(i, 2), right, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
