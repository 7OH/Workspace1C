#if LINUX
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Управление службами» (issue #11, Avalonia/Linux): список выбранных служб
    /// (services.json), команды через <c>systemctl</c>, подменю «Все сервисы»,
    /// «Обновить» и «Настроить» (чеклист). Системного диалога на Linux нет — кнопка
    /// скрыта (CanOpenSystemDialog == false); пауза/восстановление не поддерживаются
    /// (кнопки неактивны). Вся логика — в чистой ViewModel
    /// <see cref="ServicesWindowViewModel"/> (платформа — Linux).
    /// </summary>
    public sealed class ServicesWindow : ModalWindowBase
    {
        protected override bool SupportsMaximizeButton => true;

        private readonly ServicesWindowViewModel _vm;
        private readonly ListBox _servicesList;

        public ServicesWindow()
        {
            Title = LocalizationManager.T("Services.Title");
            Width = 640;
            Height = 560;
            MinWidth = 480;
            MinHeight = 400;
            FontSize = 13;
            CanResize = true;
            ShowInTaskbar = true;

            var services = AppServices.GetRequiredService<IServiceControllerService>();
            var store = AppServices.GetRequiredService<IServicesStore>();
            var dialogs = AppServices.GetRequiredService<IDialogService>();

            _vm = new ServicesWindowViewModel(
                services,
                store,
                dialogs,
                action => Dispatcher.UIThread.Post(action),
                ServicePlatform.Linux);

            DataContext = _vm;
            _vm.ServicesPickerProvider = ShowServicesPicker;

            // ---- Первая строка: «Настроить» и «Системный диалог» (скрыт на Linux). ----
            var configureButton = BuildActionButton(
                LocalizationManager.T("Services.Configure"), "⚙", () => _vm.ConfigureCommand.Execute(null));
            var systemDialogButton = BuildActionButton(
                LocalizationManager.T("Services.SystemDialog"), "🗔", () => _vm.OpenSystemDialogCommand.Execute(null));
            systemDialogButton.IsVisible = _vm.CanOpenSystemDialog;

            var topButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 0, 0, 10),
                Children = { configureButton, systemDialogButton }
            };

            // ---- Заголовок списка «Службы». ----
            var listTitle = new TextBlock
            {
                Text = LocalizationManager.T("Services.ListTitle"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };

            // ---- Строка кнопок над списком (без групп — итерация 2 issue #11). ----
            var refreshButton = BuildIconButton("⟳", LocalizationManager.T("Services.Refresh"),
                () => _vm.RefreshCommand.Execute(null));
            var startButton = BuildIconButton("▶", LocalizationManager.T("Services.Start"),
                () => _vm.StartCommand.Execute(null));
            startButton.Bind(Button.IsEnabledProperty, new Binding("CanStart"));
            var stopButton = BuildIconButton("⏹", LocalizationManager.T("Services.Stop"),
                () => _vm.StopCommand.Execute(null));
            stopButton.Bind(Button.IsEnabledProperty, new Binding("CanStop"));
            var pauseButton = BuildIconButton("⏸", LocalizationManager.T("Services.Pause"),
                () => _vm.PauseCommand.Execute(null));
            pauseButton.Bind(Button.IsEnabledProperty, new Binding("CanPause"));
            var continueButton = BuildIconButton("⏵", LocalizationManager.T("Services.Continue"),
                () => _vm.ContinueCommand.Execute(null));
            continueButton.Bind(Button.IsEnabledProperty, new Binding("CanContinue"));
            var restartButton = BuildIconButton("🔄", LocalizationManager.T("Services.Restart"),
                () => _vm.RestartCommand.Execute(null));
            restartButton.Bind(Button.IsEnabledProperty, new Binding("CanRestart"));

            // Подменю «Все сервисы» (Запустить / Остановить / Перезапустить).
            Button? allServicesButton = null;
            allServicesButton = BuildActionButton(LocalizationManager.T("Services.AllServices"),
                "▾",
                () =>
                {
                    var menu = new ContextMenu();
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
                    menu.Open(allServicesButton!);
                });

            // Группы (issue #11, итерация 2): кнопки «Добавить группу / Изменить группу /
            // Удалить группу / Переместить выше / ниже» скрыты — место зарезервировано;
            // блок действий начинается с разделителя.
            var actionsRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 0, 0, 6),
                Children =
                {
                    refreshButton,
                    Divider(),
                    startButton,
                    stopButton,
                    Divider(),
                    pauseButton,
                    continueButton,
                    Divider(),
                    restartButton,
                    Divider(),
                    allServicesButton
                }
            };

            // ---- Подсказка при пустом списке. ----
            var emptyHint = new TextBlock
            {
                Text = LocalizationManager.T("Services.Empty"),
                FontSize = 12,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
                [!IsVisibleProperty] = new Binding("!HasRows")
            };

            // ---- Список служб. ----
            _servicesList = new ListBox
            {
                ItemTemplate = new FuncDataTemplate<ServiceInfoRow>((row, _) => BuildServiceRow(row))
            };
            _servicesList.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
            {
                Setters =
                {
                    new Setter(ListBoxItem.MinHeightProperty, 26d),
                    new Setter(ListBoxItem.PaddingProperty, new Thickness(8, 2)),
                    new Setter(ListBoxItem.VerticalContentAlignmentProperty, VerticalAlignment.Center)
                }
            });
            _servicesList.Bind(ItemsControl.ItemsSourceProperty, new Binding("Rows"));
            _servicesList.Bind(ListBox.SelectedItemProperty, new Binding("SelectedRow", BindingMode.TwoWay));

            // ---- Статус-строка окна. ----
            var statusText = new TextBlock
            {
                FontSize = 12,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            statusText.Bind(TextBlock.TextProperty, new Binding("StatusText"));

            // ---- Компоновка (порядок: верх — элементы, низ — статус, остаток — список). ----
            var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
            DockPanel.SetDock(topButtons, Dock.Top);
            DockPanel.SetDock(listTitle, Dock.Top);
            DockPanel.SetDock(actionsRow, Dock.Top);
            DockPanel.SetDock(emptyHint, Dock.Top);
            DockPanel.SetDock(statusText, Dock.Bottom);
            root.Children.Add(topButtons);
            root.Children.Add(listTitle);
            root.Children.Add(actionsRow);
            root.Children.Add(emptyHint);
            root.Children.Add(statusText);
            root.Children.Add(_servicesList);
            Content = root;

            Opened += async (_, _) => await _vm.LoadAsync();
        }

        /// <summary>«Настроить»: модальный чеклист всех служб; null — отмена.</summary>
        private IReadOnlyList<string>? ShowServicesPicker()
        {
            var window = new ServicesPickerWindow();
            if (!window.ShowSync(this))
                return null;
            return window.SelectedServices;
        }

        // ===================== Построители =====================

        /// <summary>Кнопка действия: значок + подпись.</summary>
        private static Button BuildActionButton(string text, string icon, Action onClick)
        {
            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = icon, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }
                    }
                },
                Padding = new Thickness(12, 5)
            };
            button.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            button.Click += (_, _) => onClick();
            return button;
        }

        /// <summary>Узкая кнопка-иконка с подсказкой.</summary>
        private static Button BuildIconButton(string icon, string tooltip, Action onClick)
        {
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = icon,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 13
                },
                Padding = new Thickness(8, 5)
            };
            ToolTip.SetTip(button, tooltip);
            button.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            button.Click += (_, _) => onClick();
            return button;
        }

        /// <summary>Вертикальный разделитель в строке кнопок.</summary>
        private static Border Divider() => new()
        {
            Width = 1,
            Height = 18,
            Margin = new Thickness(0, 0, 6, 0)
        };

        /// <summary>
        /// Строка списка: кружок статуса (обновляется при смене StatusColorHex) +
        /// отображаемое имя + статус; контекстное меню — тот же набор команд.
        /// </summary>
        private Control BuildServiceRow(ServiceInfoRow row)
        {
            var dot = new Ellipse
            {
                Width = 9,
                Height = 9,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            void ApplyStatusColor()
            {
                try
                {
                    dot.Fill = new SolidColorBrush(Color.Parse(row.StatusColorHex));
                }
                catch
                {
                    dot.Fill = Brushes.Gray;
                }
            }

            ApplyStatusColor();
            row.PropertyChanged += OnRowPropertyChanged;

            void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(ServiceInfoRow.StatusColorHex))
                    ApplyStatusColor();
            }

            var name = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            name.Bind(TextBlock.TextProperty, new Binding(nameof(ServiceInfoRow.DisplayName)));

            var status = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                Opacity = 0.65,
                Margin = new Thickness(12, 0, 0, 0)
            };
            status.Bind(TextBlock.TextProperty, new Binding(nameof(ServiceInfoRow.StatusText)));

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                    new ColumnDefinition(GridLength.Auto)
                }
            };
            Grid.SetColumn(dot, 0);
            Grid.SetColumn(name, 1);
            Grid.SetColumn(status, 2);
            grid.Children.Add(dot);
            grid.Children.Add(name);
            grid.Children.Add(status);

            // Правый клик по строке выбирает её (команды действуют на SelectedRow).
            grid.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(grid).Properties.IsRightButtonPressed)
                {
                    _servicesList.SelectedItem = row;
                    _vm.SelectedRow = row;
                }
            };

            // Контекстное меню строки: тот же набор команд, что и кнопки.
            var menu = new ContextMenu();
            AddRowMenuCommand(menu, LocalizationManager.T("Services.Start"), row, _vm.StartCommand);
            AddRowMenuCommand(menu, LocalizationManager.T("Services.Stop"), row, _vm.StopCommand);
            menu.Items.Add(new Separator());
            AddRowMenuCommand(menu, LocalizationManager.T("Services.Pause"), row, _vm.PauseCommand);
            AddRowMenuCommand(menu, LocalizationManager.T("Services.Continue"), row, _vm.ContinueCommand);
            menu.Items.Add(new Separator());
            AddRowMenuCommand(menu, LocalizationManager.T("Services.Restart"), row, _vm.RestartCommand);
            grid.ContextMenu = menu;

            return grid;
        }

        private static void AddRowMenuCommand(ContextMenu menu, string header, ServiceInfoRow row, System.Windows.Input.ICommand command)
        {
            // Пункт добавляется с командой VM (не биндингом): контекстное меню лежит вне
            // визуального дерева, команда уже резолвлена в конструкторе строки.
            menu.Items.Add(new MenuItem { Header = header, Command = command });
        }
    }
}
#endif