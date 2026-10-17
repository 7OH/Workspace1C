using System;
using System.Collections.Generic;
using System.Linq;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Учётные данные администратора информационной базы кластера для команд rac
/// «infobase info/update» (issue #3, комментарий 7OH 17.10): берутся из списка баз
/// приложения, если там есть клиент-серверная база с тем же именем на том же сервере
/// и для неё сохранены логин и пароль. Иначе пользователь вводит их в диалоге.
/// </summary>
public static class ClusterInfobaseCredentials
{
    /// <summary>Найденные учётные данные и имя базы списка, откуда они взяты.</summary>
    public sealed record Found(string User, string Password, string SourceName);

    /// <summary>
    /// Ищет сохранённые учётные данные: база списка — клиент-серверная, Ref совпадает с
    /// именем базы в кластере (без учёта регистра), сервер — тот же хост (порт не
    /// учитывается: rac/RAS и кластер слушают разные порты). Если по хосту совпадений нет,
    /// но база с таким именем в списке ровно одна — берётся она. Используется логин
    /// «Конфигуратора» (административный), иначе логин базы; вход ОС и пустой логин — не в счёт.
    /// </summary>
    public static Found? Find(IEnumerable<Infobase>? bases, string? serverAddress, string? infobaseName)
    {
        if (bases is null || string.IsNullOrWhiteSpace(infobaseName))
            return null;

        var name = infobaseName.Trim();
        var candidates = bases
            .Where(b => b?.Connection is { Type: ConnectionType.ClientServer } c &&
                        string.Equals(c.DatabaseName?.Trim(), name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0)
            return null;

        var host = HostOf(serverAddress);
        var sameHost = candidates
            .Where(b => SplitHosts(b.Connection.Server).Any(h => SameHost(h, host)))
            .ToList();
        var pool = sameHost.Count > 0 ? sameHost : (candidates.Count == 1 ? candidates : new List<Infobase>());

        foreach (var infobase in pool)
        {
            InfobaseAuthResolver.Resolve(infobase, OneCLaunchMode.Configurator,
                out var mode, out var user, out var password);
            if (mode == AuthenticationMode.Windows || string.IsNullOrWhiteSpace(user))
                InfobaseAuthResolver.Resolve(infobase, OneCLaunchMode.Enterprise,
                    out mode, out user, out password);
            if (mode != AuthenticationMode.Windows && !string.IsNullOrWhiteSpace(user))
                return new Found(user.Trim(), password ?? string.Empty, infobase.Name ?? string.Empty);
        }
        return null;
    }

    /// <summary>Хост из адреса «host», «host:port» (порт отбрасывается).</summary>
    internal static string HostOf(string? address)
    {
        var a = (address ?? string.Empty).Trim();
        if (a.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
            a = a.Substring(6);
        var colon = a.LastIndexOf(':');
        if (colon > 0 && a.IndexOf(':') == colon)
            a = a.Substring(0, colon);
        return a.Trim();
    }

    /// <summary>Хосты из значения Srvr («srv1:1541,srv2» — кластер с резервированием).</summary>
    private static IEnumerable<string> SplitHosts(string? server) =>
        (server ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(HostOf)
            .Where(h => h.Length > 0);

    /// <summary>Хосты совпадают без учёта регистра; localhost, 127.0.0.1 и «.» — один хост.</summary>
    internal static bool SameHost(string a, string b)
    {
        static string Norm(string h)
        {
            var v = h.Trim().ToLowerInvariant();
            return v is "127.0.0.1" or "." or "::1" ? "localhost" : v;
        }
        return a.Length > 0 && b.Length > 0 && Norm(a) == Norm(b);
    }
}
