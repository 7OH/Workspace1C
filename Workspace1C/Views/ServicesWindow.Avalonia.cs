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
            Width = 900;
            Height = 600;
            MinWidth = 760;
            MinHeight = 400;
            FontSize = 13;
            CanResize = true;
            // Окно модальное (issue #11, 7OH 17.10) — в панели задач не показывается.
            ShowInTaskbar = false;

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

            // Редактирование групп (issue #11, итерация 2; окно — Задача 3): провайдер
            // открывает модальное окно группы и возвращает готовую группу или null.
            _vm.GroupEditorProvider = ShowGroupEditor;

            // ---- Первая строка: «Настроить» и «Системный диалог» (скрыт на Linux). ----
            var configureButton = BuildActionButton(
                LocalizationManager.T("Services.Configure"), "⚙", () => _vm.ConfigureCommand.Execute(null), "#6B7280");
            var systemDialogButton = BuildActionButton(
                LocalizationManager.T("Services.SystemDialog"), "🗔", () => _vm.OpenSystemDialogCommand.Execute(null), "#2563EB");
            systemDialogButton.IsVisible = _vm.CanOpenSystemDialog;
            // «Выдать права» (issue #11): на Linux выдача прав не поддерживается — скрыта.
            var grantRightsButton = BuildActionButton(
                LocalizationManager.T("Services.GrantRights"), "🛡", () => _vm.GrantRightsCommand.Execute(null), "#7C3AED");
            grantRightsButton.IsVisible = _vm.CanGrantRights;

            var topButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 0, 0, 10),
                Children = { configureButton, systemDialogButton, grantRightsButton }
            };

            // ---- Заголовок списка «Службы». ----
            var listTitle = new TextBlock
            {
                Text = LocalizationManager.T("Services.ListTitle"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };

            // ---- Строка кнопок над списком (issue #11, итерация 2 — группы служб). ----
            // Порядок задаёт пользователь (issue #11, 7OH 17.10): «Переместить выше / ниже»
            // работает для строк обоих типов (службы и группы).
            var moveUpButton = BuildIconButton("▲", LocalizationManager.T("Services.MoveUp"),
                () => _vm.MoveUpCommand.Execute(null), "#2563EB");
            moveUpButton.Bind(Button.IsEnabledProperty, new Binding("CanMoveUp"));
            var moveDownButton = BuildIconButton("▼", LocalizationManager.T("Services.MoveDown"),
                () => _vm.MoveDownCommand.Execute(null), "#2563EB");
            moveDownButton.Bind(Button.IsEnabledProperty, new Binding("CanMoveDown"));
            // Группы служб: «Добавить/Изменить/Удалить группу». Кнопки неактивны, пока окно
            // не установит GroupEditorProvider (окно редактирования группы — Задача 3).
            var addGroupButton = BuildIconButton("📦", LocalizationManager.T("Services.GroupAddHint"),
                () => _vm.AddGroupCommand.Execute(null), "#7C3AED");
            addGroupButton.Bind(Button.IsEnabledProperty, new Binding("CanAddGroup"));
            var editGroupButton = BuildIconButton("✎", LocalizationManager.T("Services.GroupEditHint"),
                () => _vm.EditGroupCommand.Execute(null), "#7C3AED");
            editGroupButton.Bind(Button.IsEnabledProperty, new Binding("CanEditGroup"));
            var deleteGroupButton = BuildIconButton("🗑", LocalizationManager.T("Services.GroupDeleteHint"),
                () => _vm.DeleteGroupCommand.Execute(null), "#DC2626");
            deleteGroupButton.Bind(Button.IsEnabledProperty, new Binding("CanDeleteGroup"));
            var refreshButton = BuildIconButton("⟳", LocalizationManager.T("Services.Refresh"),
                () => _vm.RefreshCommand.Execute(null), "#2563EB");
            // Команды — только цветные иконки, без подписей (замечание 7OH 18.10);
            // подписи — подсказками, в контекстном меню подписи остаются.
            // «Восстановить» — треугольник с палочкой справа (ServiceCommandGlyphs.
            // ContinueGlyph): палочку дорисовать к текущему глифу «Запустить».
            var startButton = BuildCommandIconButton(ServiceCommandGlyphs.StartGlyph,
                LocalizationManager.T("Services.Start"),
                () => _vm.StartCommand.Execute(null), ServiceCommandGlyphs.ColorHex(ServiceAction.Start));
            startButton.Bind(Button.IsEnabledProperty, new Binding("CanStart"));
            var stopButton = BuildCommandIconButton(ServiceCommandGlyphs.StopGlyph,
                LocalizationManager.T("Services.Stop"),
                () => _vm.StopCommand.Execute(null), ServiceCommandGlyphs.ColorHex(ServiceAction.Stop));
            stopButton.Bind(Button.IsEnabledProperty, new Binding("CanStop"));
            var pauseButton = BuildCommandIconButton(ServiceCommandGlyphs.PauseGlyph,
                LocalizationManager.T("Services.Pause"),
                () => _vm.PauseCommand.Execute(null), ServiceCommandGlyphs.ColorHex(ServiceAction.Pause));
            pauseButton.Bind(Button.IsEnabledProperty, new Binding("CanPause"));
            var continueButton = BuildCommandIconButton(ServiceCommandGlyphs.ContinueGlyph,
                LocalizationManager.T("Services.Continue"),
                () => _vm.ContinueCommand.Execute(null), ServiceCommandGlyphs.ColorHex(ServiceAction.Continue));
            continueButton.Bind(Button.IsEnabledProperty, new Binding("CanContinue"));
            var restartButton = BuildCommandIconButton(ServiceCommandGlyphs.RestartGlyph,
                LocalizationManager.T("Services.Restart"),
                () => _vm.RestartCommand.Execute(null), ServiceCommandGlyphs.ColorHex(ServiceAction.Restart));
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

            var actionsRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 0, 0, 6),
                Children =
                {
                    moveUpButton,
                    moveDownButton,
                    Divider(),
                    addGroupButton,
                    editGroupButton,
                    deleteGroupButton,
                    Divider(),
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

            // ---- Список служб и групп (смешанные строки, issue #11, итерация 2). ----
            _servicesList = new ListBox
            {
                ItemTemplate = new FuncDataTemplate<ServiceListRow>((row, _) => BuildRow(row))
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

        /// <summary>Показывает окно модально (синхронно, issue #11).</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        /// <summary>«Настроить»: модальный чеклист всех служб; null — отмена.</summary>
        private IReadOnlyList<string>? ShowServicesPicker()
        {
            var window = new ServicesPickerWindow();
            if (!window.ShowSync(this))
                return null;
            return window.SelectedServices;
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
            var window = new ServiceGroupEditWindow(editingGroup, availableServices);
            if (!window.ShowSync(this))
                return null;
            return window.Result;
        }

        // ===================== Построители =====================

        /// <summary>Кнопка действия: значок (цветной, issue #11) + подпись.</summary>
        private static Button BuildActionButton(string text, string icon, Action onClick, string? iconColorHex = null)
        {
            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = icon,
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = iconColorHex is null ? null : new SolidColorBrush(Color.Parse(iconColorHex))
                        },
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
        private static Button BuildIconButton(string icon, string tooltip, Action onClick, string? iconColorHex = null)
        {
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = icon,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 13,
                    Foreground = iconColorHex is null ? null : new SolidColorBrush(Color.Parse(iconColorHex))
                },
                Padding = new Thickness(8, 5)
            };
            ToolTip.SetTip(button, tooltip);
            button.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            button.Click += (_, _) => onClick();
            return button;
        }

        /// <summary>
        /// Кнопка команды службы: только цветная иконка, без подписи (замечание 7OH
        /// 18.10: «выключить надписи у кнопок управления состоянием на панели»);
        /// подпись — подсказкой. По ширине чуть больше кнопки «Добавить группу»
        /// (Padding 13,5 и MinWidth 44 против 8,5 у обычной кнопки-иконки).
        /// </summary>
        private static Button BuildCommandIconButton(string icon, string tooltip, Action onClick, string iconColorHex)
        {
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = icon,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.Parse(iconColorHex))
                },
                Padding = new Thickness(13, 5),
                MinWidth = 44
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
        /// Строка списка (служба или группа, issue #11, итерация 2): кружок статуса
        /// (обновляется при смене StatusColorHex) + иконка-индикатор группы (только для
        /// групп) + отображаемое имя + статус; контекстное меню — по типу строки.
        /// </summary>
        private Control BuildRow(ServiceListRow row)
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
                if (e.PropertyName == nameof(ServiceListRow.StatusColorHex))
                    ApplyStatusColor();
            }

            // Иконка-индикатор группы (видна только для строк-групп).
            var groupIcon = new TextBlock
            {
                Text = "📦",
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#7C3AED")),
                IsVisible = row.IsGroup
            };

            var name = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            name.Bind(TextBlock.TextProperty, new Binding(nameof(ServiceListRow.DisplayName)));

            var status = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                Opacity = 0.65,
                Margin = new Thickness(12, 0, 0, 0)
            };
            status.Bind(TextBlock.TextProperty, new Binding(nameof(ServiceListRow.StatusText)));

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                    new ColumnDefinition(GridLength.Auto)
                }
            };
            Grid.SetColumn(dot, 0);
            Grid.SetColumn(groupIcon, 1);
            Grid.SetColumn(name, 2);
            Grid.SetColumn(status, 3);
            grid.Children.Add(dot);
            grid.Children.Add(groupIcon);
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

            // Контекстное меню строки: для службы — команды службы; для группы — команды
            // группы + «Изменить/Удалить группу» + перемещение (issue #11, итерация 2).
            var menu = new ContextMenu();
            if (row.IsGroup)
            {
                AddRowMenuCommand(menu, LocalizationManager.T("Services.Start"), _vm.StartGroupCommand);
                AddRowMenuCommand(menu, LocalizationManager.T("Services.Stop"), _vm.StopGroupCommand);
                menu.Items.Add(new Separator());
                AddRowMenuCommand(menu, LocalizationManager.T("Services.Pause"), _vm.PauseGroupCommand);
                AddRowMenuCommand(menu, LocalizationManager.T("Services.Continue"), _vm.ContinueGroupCommand);
                menu.Items.Add(new Separator());
                AddRowMenuCommand(menu, LocalizationManager.T("Services.Restart"), _vm.RestartGroupCommand);
                menu.Items.Add(new Separator());
                AddRowMenuCommand(menu, LocalizationManager.T("Services.EditGroup"), _vm.EditGroupCommand);
                AddRowMenuCommand(menu, LocalizationManager.T("Services.DeleteGroup"), _vm.DeleteGroupCommand);
                menu.Items.Add(new Separator());
                AddRowMenuCommand(menu, LocalizationManager.T("Services.MoveUp"), _vm.MoveUpCommand);
                AddRowMenuCommand(menu, LocalizationManager.T("Services.MoveDown"), _vm.MoveDownCommand);
            }
            else
            {
                AddRowMenuCommand(menu, LocalizationManager.T("Services.Start"), _vm.StartCommand);
                AddRowMenuCommand(menu, LocalizationManager.T("Services.Stop"), _vm.StopCommand);
                menu.Items.Add(new Separator());
                AddRowMenuCommand(menu, LocalizationManager.T("Services.Pause"), _vm.PauseCommand);
                AddRowMenuCommand(menu, LocalizationManager.T("Services.Continue"), _vm.ContinueCommand);
                menu.Items.Add(new Separator());
                AddRowMenuCommand(menu, LocalizationManager.T("Services.Restart"), _vm.RestartCommand);
                menu.Items.Add(new Separator());
                AddRowMenuCommand(menu, LocalizationManager.T("Services.MoveUp"), _vm.MoveUpCommand);
                AddRowMenuCommand(menu, LocalizationManager.T("Services.MoveDown"), _vm.MoveDownCommand);
            }
            grid.ContextMenu = menu;

            return grid;
        }

        private static void AddRowMenuCommand(ContextMenu menu, string header, System.Windows.Input.ICommand command)
        {
            // Пункт добавляется с командой VM (не биндингом): контекстное меню лежит вне
            // визуального дерева, команда уже резолвлена в конструкторе строки.
            menu.Items.Add(new MenuItem { Header = header, Command = command });
        }
    }
}
#endif