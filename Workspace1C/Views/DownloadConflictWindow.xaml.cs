#if WINDOWS
using System.Windows;
using Workspace1C.Localization;
using Workspace1C.Services;

namespace Workspace1C;

/// <summary>
/// Диалог «Файл уже скачивается» (issue #22, Этап C): при ручном запуске загрузки файла,
/// который уже качается в фоне, предлагает «Отменить фоновую загрузку и начать заново» /
/// «Подождать» / «Отмена». Тексты кнопок — локализуемые ключи
/// (<see cref="DownloadConflictGuard.CancelAndRestartKey"/>, <see cref="DownloadConflictGuard.WaitKey"/>,
/// <see cref="DownloadConflictGuard.CancelKey"/>).
/// </summary>
public partial class DownloadConflictWindow : Window
{
    /// <summary>Итог диалога (по умолчанию — «Отмена»).</summary>
    public DownloadConflictDecision Decision { get; private set; } = DownloadConflictDecision.Cancel;

    public DownloadConflictWindow(string message)
    {
        InitializeComponent();
        Title = LocalizationManager.T(DownloadConflictGuard.FileAlreadyDownloadingTitleKey);
        MessageText.Text = message;

        var owner = Application.Current?.MainWindow;
        if (owner is not null && !ReferenceEquals(owner, this))
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        Loaded += (_, _) => CancelAndRestartButton.Focus();
    }

    /// <summary>Показывает диалог модально и возвращает выбранное действие.</summary>
    public static DownloadConflictDecision Ask(Window? owner, string message)
    {
        var window = new DownloadConflictWindow(message);
        if (owner is not null && !ReferenceEquals(owner, window))
            window.Owner = owner;
        window.ShowDialog();
        return window.Decision;
    }

    private void OnCancelAndRestartClick(object sender, RoutedEventArgs e)
    {
        Decision = DownloadConflictDecision.CancelBackgroundAndRestart;
        Close();
    }

    private void OnWaitClick(object sender, RoutedEventArgs e)
    {
        Decision = DownloadConflictDecision.Wait;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Decision = DownloadConflictDecision.Cancel;
        Close();
    }
}
#endif
