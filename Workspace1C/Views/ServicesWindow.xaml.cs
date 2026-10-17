using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Конвертер строки #RRGGBB в <see cref="Brush"/> для кружка статуса строки службы.
    /// При сбое — серый (как в построителях иконок меню).
    /// </summary>
    public sealed class BrushFromHexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is string hex && !string.IsNullOrWhiteSpace(hex))
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            catch
            {
                // Некорректный цвет — серый.
            }
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Окно «Управление службами» (issue #11, Windows/WPF): список выбранных служб
    /// (services.json), команды Запустить/Остановить/Пауза/Восстановить/Перезапустить,
    /// подменю «Все сервисы», «Обновить», «Настроить» (чеклист всех служб) и
    /// «Системный диалог» (services.msc). Вся логика — в чистой ViewModel
    /// <see cref="ServicesWindowViewModel"/>.
    /// </summary>
    public partial class ServicesWindow : Window
    {
        private readonly ServicesWindowViewModel _vm;

        public ServicesWindow()
        {
            InitializeComponent();

            var services = AppServices.GetRequiredService<IServiceControllerService>();
            var store = AppServices.GetRequiredService<IServicesStore>();
            var dialogs = AppServices.GetRequiredService<IDialogService>();

            _vm = new ServicesWindowViewModel(
                services,
                store,
                dialogs,
                action => Application.Current?.Dispatcher.BeginInvoke(action));

            DataContext = _vm;
            Title = LocalizationManager.T("Services.Title");
            PreviewKeyDown += OnWindowPreviewKeyDown;
            Closed += (_, _) => { /* таймеров и подписок на коллекции нет — освобождать нечего */ };

            // «Настроить» — модальный чеклист служб системы; результат применяет VM.
            _vm.ServicesPickerProvider = ShowServicesPicker;

            Loaded += async (_, _) => await _vm.LoadAsync();
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                Close();
                e.Handled = true;
            }
        }

        /// <summary>«Настроить»: модальное окно-чеклист; null — отмена.</summary>
        private IReadOnlyList<string>? ShowServicesPicker()
        {
            var window = new ServicesPickerWindow { Owner = this };
            return window.ShowDialog() == true ? window.SelectedServices : null;
        }

        // ===================== Кнопки =====================

        private void OnConfigure_Click(object sender, RoutedEventArgs e) => _vm.ConfigureCommand.Execute(null);

        private void OnSystemDialog_Click(object sender, RoutedEventArgs e) => _vm.OpenSystemDialogCommand.Execute(null);

        private void OnRefresh_Click(object sender, RoutedEventArgs e) => _vm.RefreshCommand.Execute(null);

        private void OnStart_Click(object sender, RoutedEventArgs e) => _vm.StartCommand.Execute(null);

        private void OnStop_Click(object sender, RoutedEventArgs e) => _vm.StopCommand.Execute(null);

        private void OnPause_Click(object sender, RoutedEventArgs e) => _vm.PauseCommand.Execute(null);

        private void OnContinue_Click(object sender, RoutedEventArgs e) => _vm.ContinueCommand.Execute(null);

        private void OnRestart_Click(object sender, RoutedEventArgs e) => _vm.RestartCommand.Execute(null);

        /// <summary>Подменю «Все сервисы»: Запустить / Остановить / Перезапустить.</summary>
        private void OnAllServices_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var menu = new ContextMenu { PlacementTarget = button };
            menu.Items.Add(new MenuItem
            {
                Header = LocalizationManager.T("Services.StartAll"),
                Command = _vm.StartAllCommand
            });
            menu.Items.Add(new MenuItem
            {
                Header = LocalizationManager.T("Services.StopAll"),
                Command = _vm.StopAllCommand
            });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem
            {
                Header = LocalizationManager.T("Services.RestartAll"),
                Command = _vm.RestartAllCommand
            });
            menu.IsOpen = true;
        }

        // ===================== Контекстное меню строки =====================

        /// <summary>Контекстное меню строки: тот же набор команд, что и кнопки.</summary>
        private void OnServicesList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // Правый клик по строке должен её выбрать (команды действуют на SelectedRow).
            if (e.OriginalSource is DependencyObject source &&
                FindAncestor<ListBoxItem>(source) is { } item &&
                item.DataContext is ServiceInfoRow row)
            {
                _vm.SelectedRow = row;
                item.IsSelected = true;
            }

            if (ServicesList.ContextMenu is not { } existing || existing.Items.Count == 0)
                ServicesList.ContextMenu = BuildRowContextMenu();
        }

        private ContextMenu BuildRowContextMenu()
        {
            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem
            {
                Header = LocalizationManager.T("Services.Start"),
                Command = _vm.StartCommand
            });
            menu.Items.Add(new MenuItem
            {
                Header = LocalizationManager.T("Services.Stop"),
                Command = _vm.StopCommand
            });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem
            {
                Header = LocalizationManager.T("Services.Pause"),
                Command = _vm.PauseCommand
            });
            menu.Items.Add(new MenuItem
            {
                Header = LocalizationManager.T("Services.Continue"),
                Command = _vm.ContinueCommand
            });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem
            {
                Header = LocalizationManager.T("Services.Restart"),
                Command = _vm.RestartCommand
            });
            return menu;
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            for (var node = current; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is T match)
                    return match;
            }
            return null;
        }
    }
}