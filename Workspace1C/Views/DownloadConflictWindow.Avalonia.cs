#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C;

/// <summary>
/// Диалог «Файл уже скачивается» (issue #22, Этап C): при ручном запуске загрузки файла,
/// который уже качается в фоне, предлагает «Отменить фоновую загрузку и начать заново» /
/// «Подождать» / «Отмена». Avalonia/Linux-версия окна <see cref="DownloadConflictWindow"/>
/// (строится кодом).
/// </summary>
public sealed class DownloadConflictWindow : ModalWindowBase
{
    private DownloadConflictDecision _decision = DownloadConflictDecision.Cancel;

    public DownloadConflictWindow(string message)
    {
        Title = LocalizationManager.T(DownloadConflictGuard.FileAlreadyDownloadingTitleKey);
        Width = 520;
        FontSize = 13;
        CanResize = false;

        var root = new StackPanel { Margin = new Thickness(16) };
        var messageText = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        ThemeBrushes.Bind(messageText, TextBlock.ForegroundProperty, "TextPrimaryBrush");
        root.Children.Add(messageText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 18, 0, 0),
        };

        var cancelAndRestartButton = new Button
        {
            MinWidth = 150,
            Height = 34,
            Content = LocalizationManager.T(DownloadConflictGuard.CancelAndRestartKey),
        };
        cancelAndRestartButton.Styled(ControlThemes.DialogConfirmButton);
        cancelAndRestartButton.Click += (_, _) =>
        {
            _decision = DownloadConflictDecision.CancelBackgroundAndRestart;
            Close();
        };

        var waitButton = BuildCancelActionButton(110, 34);
        waitButton.Content = LocalizationManager.T(DownloadConflictGuard.WaitKey);
        waitButton.Click += (_, _) =>
        {
            _decision = DownloadConflictDecision.Wait;
            Close();
        };

        var cancelButton = BuildCancelActionButton(110, 34);
        cancelButton.Content = LocalizationManager.T(DownloadConflictGuard.CancelKey);
        cancelButton.Click += (_, _) =>
        {
            _decision = DownloadConflictDecision.Cancel;
            Close();
        };

        buttons.Children.Add(cancelAndRestartButton);
        buttons.Children.Add(waitButton);
        buttons.Children.Add(cancelButton);
        root.Children.Add(buttons);

        Content = root;
    }

    /// <summary>Показывает диалог модально и возвращает выбранное действие.</summary>
    public static DownloadConflictDecision Ask(Window? owner, string message)
    {
        var window = new DownloadConflictWindow(message);
        window.ShowDialogSync(owner);
        return window._decision;
    }
}
#endif
