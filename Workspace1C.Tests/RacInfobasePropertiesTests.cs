using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #3 (7OH 17.10): полные свойства базы кластера — разбор rac «infobase info»,
/// диалог свойств (загрузка, «Обновить», сборка запроса update) и учётные данные
/// администратора ИБ из списка баз приложения.
/// </summary>
public sealed class RacInfobasePropertiesTests
{
    private const string InfoOutput = """
        infobase                                   : 0f6d2b55-5f7d-4d9f-9a0e-1b8a0b1c2d3e
        name                                       : erp
        dbms                                       : MSSQLServer
        db-server                                  : sql01
        db-name                                    : erp_db
        db-user                                    : sa
        security-level                             : 1
        license-distribution                       : allow
        scheduled-jobs-deny                        : on
        sessions-deny                              : on
        denied-from                                : 2026-10-17T22:00:00
        denied-message                             : "Идёт обновление"
        denied-parameter                           :
        denied-to                                  : 2026-10-18T06:00:00
        permission-code                            : 777
        external-session-manager-connection-string :
        external-session-manager-required          : no
        reserve-working-processes                  : no
        security-profile-name                      :
        safe-mode-security-profile-name            : safe
        descr                                      : "Производственная база"
        """;

    [Fact]
    public void Parser_ReadsAllProperties()
    {
        var info = RacOutputParser.ToInfobaseInfo(InfoOutput);

        Assert.NotNull(info);
        Assert.Equal(Guid.Parse("0f6d2b55-5f7d-4d9f-9a0e-1b8a0b1c2d3e"), info!.InfobaseId);
        Assert.Equal("erp", info.Name);
        Assert.Equal("Производственная база", info.Descr);
        Assert.Equal("sql01", info.DbServer);
        Assert.Equal("erp_db", info.DbName);
        Assert.Equal("sa", info.DbUser);
        Assert.Equal(1, info.SecurityLevel);
        Assert.True(info.LicenseDistributionAllowed);
        Assert.True(info.ScheduledJobsDeny);
        Assert.True(info.SessionsDeny);
        Assert.Equal(new DateTime(2026, 10, 17, 22, 0, 0), info.DeniedFrom);
        Assert.Equal(new DateTime(2026, 10, 18, 6, 0, 0), info.DeniedTo);
        Assert.Equal("Идёт обновление", info.DeniedMessage);
        Assert.Equal("777", info.PermissionCode);
        Assert.False(info.ExternalSessionManagerRequired);
        Assert.Equal("safe", info.SafeModeSecurityProfileName);
    }

    [Fact]
    public void Parser_EmptyOrGarbage_ReturnsNull()
    {
        Assert.Null(RacOutputParser.ToInfobaseInfo(string.Empty));
        Assert.Null(RacOutputParser.ToInfobaseInfo("some text without colon"));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("0001-01-01T00:00:00", null)]
    [InlineData("2026-10-17T22:00:00", "2026-10-17T22:00:00")]
    public void ParseRacDate(string text, string? expected)
    {
        var value = RacInfobaseInfo.ParseRacDate(text);
        Assert.Equal(expected, value is null ? null : RacInfobaseInfo.FormatRacDate(value.Value));
    }

    private static RacInfobaseSummary Summary() => new()
    {
        InfobaseId = Guid.NewGuid(),
        Name = "erp",
        Descr = "старое",
        Dbms = "mssqlserver",
        DbServer = "sql01",
        DbName = "erp_db",
        DbUser = "sa",
    };

    [Fact]
    public async Task Refresh_Success_AppliesInfoAndEnablesFullUpdate()
    {
        string? usedUser = null;
        var vm = new RacInfobasePropertiesViewModel(Summary(),
            (user, _) =>
            {
                usedUser = user;
                return Task.FromResult<(RacInfobaseInfo?, string)>((RacOutputParser.ToInfobaseInfo(InfoOutput), string.Empty));
            },
            new ClusterInfobaseCredentials.Found("Админ", "pwd", "ERP (прод)"));

        Assert.Equal("Админ", vm.InfobaseUser);
        Assert.False(vm.IsFullyLoaded);

        await vm.RefreshAsync();

        Assert.Equal("Админ", usedUser);
        Assert.True(vm.IsFullyLoaded);
        Assert.False(vm.StatusIsError);
        Assert.Equal("Производственная база", vm.Description);
        Assert.True(vm.SessionsDeny);
        Assert.Equal("17.10.2026 22:00:00", vm.DeniedFromText);

        var request = vm.BuildRequest(out var error);
        Assert.Null(error);
        Assert.NotNull(request);
        Assert.Equal("Админ", request!.InfobaseUser);
        Assert.Equal("pwd", request.InfobasePassword);
        Assert.True(request.SessionsDeny);
        Assert.Equal(new DateTime(2026, 10, 17, 22, 0, 0), request.DeniedFrom);
        Assert.Equal("Идёт обновление", request.DeniedMessage);
        Assert.True(request.LicenseDistributionAllowed);
        Assert.Equal("safe", request.SafeModeSecurityProfileName);
    }

    [Fact]
    public async Task Refresh_Failure_KeepsSummaryAndDoesNotSendExtendedFields()
    {
        var vm = new RacInfobasePropertiesViewModel(Summary(),
            (_, _) => Task.FromResult<(RacInfobaseInfo?, string)>((null, "Недостаточно прав пользователя на информационную базу")));

        await vm.RefreshAsync();

        Assert.False(vm.IsFullyLoaded);
        Assert.True(vm.StatusIsError);
        Assert.Contains("Недостаточно прав", vm.StatusText);
        Assert.Equal("старое", vm.Description);
        Assert.Equal("MSSQLServer", vm.Dbms);

        var request = vm.BuildRequest(out _);
        Assert.NotNull(request);
        Assert.Null(request!.SessionsDeny);
        Assert.Null(request.DeniedMessage);
        Assert.Null(request.LicenseDistributionAllowed);
        Assert.Null(request.SecurityProfileName);
    }

    [Fact]
    public void BuildRequest_BadDate_ReturnsError()
    {
        var vm = new RacInfobasePropertiesViewModel(Summary(), null) { DeniedFromText = "завтра" };

        Assert.Null(vm.BuildRequest(out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void BuildRequest_EmptyDbServer_ReturnsError()
    {
        var vm = new RacInfobasePropertiesViewModel(Summary(), null) { DbServer = " " };

        Assert.Null(vm.BuildRequest(out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData("17.10.2026 22:00", 2026, 10, 17, 22, 0)]
    [InlineData("17.10.2026", 2026, 10, 17, 0, 0)]
    [InlineData("2026-10-17T22:00:00", 2026, 10, 17, 22, 0)]
    public void TryParseDateText_Formats(string text, int y, int mo, int d, int h, int mi)
    {
        Assert.True(RacInfobasePropertiesViewModel.TryParseDateText(text, out var value));
        Assert.Equal(new DateTime(y, mo, d, h, mi, 0), value);
    }

    [Fact]
    public void TryParseDateText_Empty_IsNotSet()
    {
        Assert.True(RacInfobasePropertiesViewModel.TryParseDateText("  ", out var value));
        Assert.Null(value);
    }

    // ---- Учётные данные администратора ИБ из списка баз ----

    private static Infobase ServerBase(string name, string server, string db, string user, string password,
        AuthenticationMode mode = AuthenticationMode.Credentials)
    {
        var ib = new Infobase { Name = name };
        ib.Connection.Type = ConnectionType.ClientServer;
        ib.Connection.Server = server;
        ib.Connection.DatabaseName = db;
        ib.Connection.User = user;
        ib.Connection.Password = password;
        ib.Connection.AuthenticationMode = mode;
        return ib;
    }

    [Fact]
    public void Credentials_SameHostAndRef_Found()
    {
        var bases = new List<Infobase>
        {
            ServerBase("Чужая", "other", "erp", "x", "y"),
            ServerBase("ERP прод", "SQL01:1541", "ERP", "Админ", "pwd"),
        };

        var found = ClusterInfobaseCredentials.Find(bases, "sql01:1545", "erp");

        Assert.NotNull(found);
        Assert.Equal("Админ", found!.User);
        Assert.Equal("pwd", found.Password);
        Assert.Equal("ERP прод", found.SourceName);
    }

    [Fact]
    public void Credentials_ConfiguratorAuthPreferred()
    {
        var ib = ServerBase("ERP", "srv", "erp", "user", "u");
        ib.ConfiguratorAuth = new InfobaseAuthSettings
        {
            AuthenticationMode = AuthenticationMode.Credentials,
            User = "admin",
            Password = "a"
        };

        var found = ClusterInfobaseCredentials.Find(new[] { ib }, "srv", "erp");

        Assert.Equal("admin", found?.User);
    }

    [Fact]
    public void Credentials_OsAuthOrEmptyUser_NotFound()
    {
        var bases = new[]
        {
            ServerBase("A", "srv", "erp", "", ""),
            ServerBase("B", "srv", "erp", "dom\\user", "", AuthenticationMode.Windows),
        };

        Assert.Null(ClusterInfobaseCredentials.Find(bases, "srv", "erp"));
    }

    [Fact]
    public void Credentials_OtherHost_SingleCandidate_Used_ManyCandidates_NotUsed()
    {
        var single = new[] { ServerBase("A", "alias-host", "erp", "adm", "1") };
        Assert.Equal("adm", ClusterInfobaseCredentials.Find(single, "srv", "erp")?.User);

        var many = new[]
        {
            ServerBase("A", "host1", "erp", "adm1", "1"),
            ServerBase("B", "host2", "erp", "adm2", "2"),
        };
        Assert.Null(ClusterInfobaseCredentials.Find(many, "srv", "erp"));
    }

    [Theory]
    [InlineData("localhost", "127.0.0.1", true)]
    [InlineData("SRV", "srv", true)]
    [InlineData("srv1", "srv2", false)]
    public void SameHost(string a, string b, bool expected)
    {
        Assert.Equal(expected, ClusterInfobaseCredentials.SameHost(a, b));
    }
}
