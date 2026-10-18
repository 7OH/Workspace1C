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

            // Редактирование групп (issue #11, итерация 2; окно — Задача 3): провайдер
            // открывает модальное окно группы и возвращает готовую группу или null.
            _vm.GroupEditorProvider = ShowGroupEditor;

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

        /// <summary>
        /// «Добавить/Изменить группу» (issue #11, итерация 2; окно — Задача 3): модальное
        /// окно редактирования группы с уже отфильтрованным списком доступных служб;
        /// null — отмена пользователем. Входной список строится VM
        /// (<see cref="ServicesWindowViewModel.AvailableServicesForGroup"/>), окно лишь
        /// показывает его в «Все службы».
        /// </summary>
        private ServiceGroup? ShowGroupEditor(ServiceGroup? editingGroup, IReadOnlyList<string> availableServices)
        {
            var window = new ServiceGroupEditWindow(editingGroup, availableServices) { Owner = this };
            return window.ShowDialog() == true ? window.Result : null;
        }

        // ===================== Кнопки =====================

        private void OnConfigure_Click(object sender, RoutedEventArgs e) => _vm.ConfigureCommand.Execute(null);

        private void OnSystemDialog_Click(object sender, RoutedEventArgs e) => _vm.OpenSystemDialogCommand.Execute(null);

        private void OnRefresh_Click(object sender, RoutedEventArgs e) => _vm.RefreshCommand.Execute(null);

        private void OnMoveUp_Click(object sender, RoutedEventArgs e) => _vm.MoveUpCommand.Execute(null);

        private void OnMoveDown_Click(object sender, RoutedEventArgs e) => _vm.MoveDownCommand.Execute(null);

        private void OnGrantRights_Click(object sender, RoutedEventArgs e) => _vm.GrantRightsCommand.Execute(null);

        // Группы служб (issue #11, итерация 2): кнопки «Добавить/Изменить/Удалить группу».
        private void OnAddGroup_Click(object sender, RoutedEventArgs e) => _vm.AddGroupCommand.Execute(null);

        private void OnEditGroup_Click(object sender, RoutedEventArgs e) => _vm.EditGroupCommand.Execute(null);

        private void OnDeleteGroup_Click(object sender, RoutedEventArgs e) => _vm.DeleteGroupCommand.Execute(null);

        /// <summary>Пункт меню с цветной иконкой (issue #11, 7OH 17.10).</summary>
        private static MenuItem CommandItem(string textKey, string icon, string colorHex, ICommand command) => new()
        {
            Header = LocalizationManager.T(textKey),
            Command = command,
            Icon = new TextBlock
            {
                Text = icon,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

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
            menu.Items.Add(CommandItem("Services.StartAll", "▶", "#16A34A", _vm.StartAllCommand));
            menu.Items.Add(CommandItem("Services.StopAll", "■", "#DC2626", _vm.StopAllCommand));
            menu.Items.Add(new Separator());
            menu.Items.Add(CommandItem("Services.RestartAll", "⟲", "#2563EB", _vm.RestartAllCommand));
            menu.IsOpen = true;
        }

        // ===================== Контекстное меню строки =====================

        /// <summary>
        /// Контекстное меню строки: для службы — команды службы; для группы — команды
        /// группы + «Изменить/Удалить группу» + перемещение (issue #11, итерация 2).
        /// Меню перестраивается при каждом открытии — состав зависит от типа строки.
        /// </summary>
        private void OnServicesList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // Правый клик по строке должен её выбрать (команды действуют на SelectedRow).
            if (e.OriginalSource is DependencyObject source &&
                FindAncestor<ListBoxItem>(source) is { } item &&
                item.DataContext is ServiceListRow row)
            {
                _vm.SelectedRow = row;
                item.IsSelected = true;
            }

            ServicesList.ContextMenu = BuildRowContextMenu();
        }

        private ContextMenu BuildRowContextMenu()
        {
            var menu = new ContextMenu();
            if (_vm.SelectedRow is ServiceGroupRow)
            {
                // Меню группы: команды группы + разделитель + изменить/удалить + перемещение.
                menu.Items.Add(CommandItem("Services.Start", "▶", "#16A34A", _vm.StartGroupCommand));
                menu.Items.Add(CommandItem("Services.Stop", "■", "#DC2626", _vm.StopGroupCommand));
                menu.Items.Add(new Separator());
                menu.Items.Add(CommandItem("Services.Pause", "❚❚", "#D97706", _vm.PauseGroupCommand));
                menu.Items.Add(CommandItem("Services.Continue", ServiceCommandGlyphs.ContinueGlyph, "#0EA5E9", _vm.ContinueGroupCommand));
                menu.Items.Add(new Separator());
                menu.Items.Add(CommandItem("Services.Restart", "⟲", "#2563EB", _vm.RestartGroupCommand));
                menu.Items.Add(new Separator());
                menu.Items.Add(CommandItem("Services.EditGroup", "✎", "#7C3AED", _vm.EditGroupCommand));
                menu.Items.Add(CommandItem("Services.DeleteGroup", "🗑", "#DC2626", _vm.DeleteGroupCommand));
                menu.Items.Add(new Separator());
                menu.Items.Add(CommandItem("Services.MoveUp", "▲", "#2563EB", _vm.MoveUpCommand));
                menu.Items.Add(CommandItem("Services.MoveDown", "▼", "#2563EB", _vm.MoveDownCommand));
            }
            else
            {
                menu.Items.Add(CommandItem("Services.Start", "▶", "#16A34A", _vm.StartCommand));
                menu.Items.Add(CommandItem("Services.Stop", "■", "#DC2626", _vm.StopCommand));
                menu.Items.Add(new Separator());
                menu.Items.Add(CommandItem("Services.Pause", "❚❚", "#D97706", _vm.PauseCommand));
                menu.Items.Add(CommandItem("Services.Continue", ServiceCommandGlyphs.ContinueGlyph, "#0EA5E9", _vm.ContinueCommand));
                menu.Items.Add(new Separator());
                menu.Items.Add(CommandItem("Services.Restart", "⟲", "#2563EB", _vm.RestartCommand));
                menu.Items.Add(new Separator());
                menu.Items.Add(CommandItem("Services.MoveUp", "▲", "#2563EB", _vm.MoveUpCommand));
                menu.Items.Add(CommandItem("Services.MoveDown", "▼", "#2563EB", _vm.MoveDownCommand));
            }
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