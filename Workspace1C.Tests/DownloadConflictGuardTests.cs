using System;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты единой точки проверки пересечения загрузок по целевому файлу (issue #22, Этап C):
/// «файл свободен», «файл уже качается» (с заголовком активной загрузки), варианты диалога
/// «отменить фоновую и начать заново» / «подождать» / «отмена».
/// </summary>
public sealed class DownloadConflictGuardTests
{
    [Fact]
    public void Resolve_FileFree_ReturnsProceed_WithoutDialog()
    {
        var manager = new BackgroundDownloadManager();
        var guard = new DownloadConflictGuard(manager);
        var asked = false;

        var decision = guard.Resolve(@"C:\tmp\file.zip", (title, message) =>
        {
            asked = true;
            return DownloadConflictDecision.Cancel;
        });

        Assert.Equal(DownloadConflictDecision.Proceed, decision);
        Assert.False(asked); // диалог не показывается, когда файл свободен
    }

    [Fact]
    public void Resolve_FileDownloading_AsksWithActiveTitle()
    {
        var manager = new BackgroundDownloadManager();
        manager.Start("bg", "Фоновая загрузка", @"C:\tmp\file.zip");
        var guard = new DownloadConflictGuard(manager);
        string? askedTitle = null;
        string? askedMessage = null;

        var decision = guard.Resolve(@"c:\TMP\file.zip", (title, message) =>
        {
            askedTitle = title;
            askedMessage = message;
            return DownloadConflictDecision.Wait;
        });

        Assert.Equal(DownloadConflictDecision.Wait, decision);
        Assert.False(string.IsNullOrWhiteSpace(askedTitle));
        Assert.False(string.IsNullOrWhiteSpace(askedMessage));
    }

    [Fact]
    public void Resolve_CancelBackgroundAndRestart_Returned()
    {
        var manager = new BackgroundDownloadManager();
        manager.Start("bg", "Фоновая загрузка", @"C:\tmp\file.zip");
        var guard = new DownloadConflictGuard(manager);

        var decision = guard.Resolve(@"C:\tmp\file.zip",
            (_, _) => DownloadConflictDecision.CancelBackgroundAndRestart);

        Assert.Equal(DownloadConflictDecision.CancelBackgroundAndRestart, decision);
    }

    [Fact]
    public void Resolve_Wait_Returned()
    {
        var manager = new BackgroundDownloadManager();
        manager.Start("bg", "Фоновая загрузка", @"C:\tmp\file.zip");
        var guard = new DownloadConflictGuard(manager);

        var decision = guard.Resolve(@"C:\tmp\file.zip", (_, _) => DownloadConflictDecision.Wait);

        Assert.Equal(DownloadConflictDecision.Wait, decision);
    }

    [Fact]
    public void Resolve_Cancel_Returned()
    {
        var manager = new BackgroundDownloadManager();
        manager.Start("bg", "Фоновая загрузка", @"C:\tmp\file.zip");
        var guard = new DownloadConflictGuard(manager);

        var decision = guard.Resolve(@"C:\tmp\file.zip", (_, _) => DownloadConflictDecision.Cancel);

        Assert.Equal(DownloadConflictDecision.Cancel, decision);
    }

    [Fact]
    public void FindActiveDownload_ReturnsActiveEntry_IgnoresCaseAndNormalizesPath()
    {
        var manager = new BackgroundDownloadManager();
        var entry = manager.Start("bg", "Фоновая загрузка", @"C:\tmp\file.zip");
        var guard = new DownloadConflictGuard(manager);

        var found = guard.FindActiveDownload(@"c:\TMP\FILE.ZIP");

        Assert.Same(entry, found);
    }

    [Fact]
    public void FindActiveDownload_AfterComplete_ReturnsNull()
    {
        var manager = new BackgroundDownloadManager();
        manager.Start("bg", "Фоновая загрузка", @"C:\tmp\file.zip");
        manager.Complete("bg");
        var guard = new DownloadConflictGuard(manager);

        Assert.Null(guard.FindActiveDownload(@"C:\tmp\file.zip"));
    }

    [Fact]
    public void Resolve_AfterComplete_ReturnsProceed()
    {
        var manager = new BackgroundDownloadManager();
        manager.Start("bg", "Фоновая загрузка", @"C:\tmp\file.zip");
        manager.Complete("bg");
        var guard = new DownloadConflictGuard(manager);

        var decision = guard.Resolve(@"C:\tmp\file.zip", (_, _) => DownloadConflictDecision.Cancel);

        Assert.Equal(DownloadConflictDecision.Proceed, decision);
    }
}
