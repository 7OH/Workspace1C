#if LINUX
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Серверы 1С» (0.3.9.124, Avalonia/Linux): встроенный монитор серверов 1С
    /// через утилиту rac. Слева — справочник «Сервера управления» (issue #3): список
    /// серверов (только Описание) с кнопками «Добавить/Изменить/Удалить»; при активации
    /// строки выполняется подключение к кластеру. Справа — выбор кластера, вкладки
    /// «Рабочие процессы / Сеансы / Соединения / Блокировки / Регламентные задания /
    /// Информация о кластере» и статус-строка. Вся логика — в чистой ViewModel
    /// <see cref="ServerMonitorViewModel"/>. Адрес/порт/логин/пароль хранятся в
    /// справочнике (server_list.json) и на форме не показываются.
    /// </summary>
    public sealed class ServerMonitorWindow : ModalWindowBase
    {
        // Окно большое и изменяемого размера — показываем кнопку «Развернуть»
        // в собственном chrome (issue #324; на Windows системная кнопка уже есть).
        protected override bool SupportsMaximizeButton => true;

        private readonly ServerMonitorViewModel _vm;
        private readonly ComboBox _clusterCombo;
        private readonly ListBox _serversList;
        private TextBlock? _jobsEmptyHint;

        public ServerMonitorWindow()
        {
            Title = LocalizationManager.T("ServerMonitor.Title");
            Width = 1280;
            Height = 720;
            MinWidth = 980;
            MinHeight = 520;
            FontSize = 13;
            CanResize = true;

            var rac = AppServices.GetRequiredService<IRacClient>();
            var dialogs = AppServices.GetRequiredService<IDialogService>();
            var serverListStore = AppServices.GetRequiredService<IServerListStore>();

            _vm = new ServerMonitorViewModel(
                rac,
                dialogs,
                action => Dispatcher.UIThread.Post(action),
                serverListStore);

            DataContext = _vm;
            _vm.PropertyChanged += OnVmPropertyChanged;
            Closed += (_, _) => _vm.Dispose();

            // ---- Справочник «Сервера управления» (issue #3): список слева. ----
            _serversList = new ListBox
            {
                ItemTemplate = new FuncDataTemplate<ManagementServer>((server, _) =>
                    new TextBlock { Text = server.Description, TextTrimming = TextTrimming.CharacterEllipsis })
            };
            _serversList.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
            {
                Setters =
                {
                    new Setter(ListBoxItem.MinHeightProperty, 36d),
                    new Setter(ListBoxItem.VerticalContentAlignmentProperty, VerticalAlignment.Center)
                }
            });
            _serversList.Bind(ListBox.ItemsSourceProperty, new Binding("Servers"));
            _serversList.Bind(ListBox.SelectedItemProperty, new Binding("SelectedServer", BindingMode.TwoWay));
            // Активация строки → подключение к кластеру (issue #3).
            _serversList.SelectionChanged += (_, _) =>
            {
                if (_serversList.SelectedItem is ManagementServer server)
                    _ = _vm.ConnectToServerAsync(server);
            };

            var addServerButton = BuildActionButton(LocalizationManager.T("ServerMonitor.Servers.Add"), "＋", OnAddServer);
            var editServerButton = BuildActionButton(LocalizationManager.T("ServerMonitor.Servers.Edit"), "✎", OnEditServer);
            var deleteServerButton = BuildActionButton(LocalizationManager.T("ServerMonitor.Servers.Delete"), "🗑", OnDeleteServer);

            var serverButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 8, 0, 0),
                Children = { addServerButton, editServerButton, deleteServerButton }
            };

            var serversPanel = new DockPanel { LastChildFill = true };
            var serversTitle = new TextBlock
            {
                Text = LocalizationManager.T("ServerMonitor.Servers.Title"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            // Подсказка при пустом списке (issue #3): добавьте сервер и выберите его.
            var serversEmptyHint = new TextBlock
            {
                Text = LocalizationManager.T("ServerMonitor.Servers.Empty"),
                FontSize = 12,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
                [!IsVisibleProperty] = new Binding("!HasServers")
            };
            DockPanel.SetDock(serversTitle, Dock.Top);
            DockPanel.SetDock(serversEmptyHint, Dock.Top);
            DockPanel.SetDock(serverButtons, Dock.Bottom);
            serversPanel.Children.Add(serversTitle);
            serversPanel.Children.Add(serversEmptyHint);
            serversPanel.Children.Add(serverButtons);
            serversPanel.Children.Add(_serversList);

            // ---- Панель кластера: выбор кластера, автообновление, кнопки. ----
            var refreshButton = BuildActionButton(LocalizationManager.T("ServerMonitor.Refresh"), "⟳", _vm.Refresh);

            // «Диагностика сети…» (0.3.9.232, функция 12): DNS/ICMP/TCP-проверка
            // адреса:порт монитора и стандартных портов 1С. Работает до подключения rac.
            var diagnosticsButton = BuildActionButton(
                LocalizationManager.T("ServerMonitor.NetworkDiagnostics"), "🛜", OpenNetworkDiagnostics);

            // Автообновление (issue #324, комментарий 17/18): переключатель вкл/выкл + интервал.
            var autoRefreshCheck = new CheckBox
            {
                Content = LocalizationManager.T("ServerMonitor.AutoRefreshToggle"),
                VerticalAlignment = VerticalAlignment.Center,
                IsChecked = _vm.IsAutoRefreshEnabled
            };
            autoRefreshCheck.IsCheckedChanged += (_, _) => _vm.SetAutoRefreshEnabled(autoRefreshCheck.IsChecked == true);
            var intervalLabel = new TextBlock
            {
                Text = LocalizationManager.T("ServerMonitor.AutoRefreshInterval"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var intervalBox = new ComboBox { Width = 72, VerticalAlignment = VerticalAlignment.Center };
            intervalBox.ItemsSource = new[] { 5, 10, 15, 30, 60 };
            intervalBox.SelectedItem = _vm.AutoRefreshIntervalSeconds;
            intervalBox.SelectionChanged += (_, _) =>
            {
                if (intervalBox.SelectedItem is int seconds)
                    _vm.AutoRefreshIntervalSeconds = seconds;
            };

            // ---- Выбор кластера + статус. ----
            _clusterCombo = new ComboBox { Width = 300, VerticalContentAlignment = VerticalAlignment.Center };
            _clusterCombo.Styled(ControlThemes.ModernComboBox);
            _clusterCombo.ItemsSource = _vm.ClusterRows;
            _clusterCombo.ItemTemplate = new FuncDataTemplate<RacClusterRow>((row, _) => new TextBlock { Text = row.DisplayText });
            _clusterCombo.SelectionChanged += (_, _) =>
            {
                if (_clusterCombo.SelectedItem is RacClusterRow row)
                    _vm.SelectedClusterId = row.Id;
            };

            var status = new TextBlock { FontSize = 12, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center };
            status.Bind(TextBlock.TextProperty, new Binding("StatusText"));
            var error = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#DC2626")), TextWrapping = TextWrapping.Wrap };
            error.Bind(TextBlock.TextProperty, new Binding("ErrorMessage"));

            var clusterPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Margin = new Thickness(0, 0, 0, 0),
                Children =
                {
                    new TextBlock { Text = LocalizationManager.T("ServerMonitor.Cluster"), VerticalAlignment = VerticalAlignment.Center },
                    _clusterCombo,
                    // Автообновление + интервал (issue #324): рядом с выбором кластера.
                    autoRefreshCheck,
                    intervalLabel,
                    intervalBox,
                    refreshButton,
                    diagnosticsButton,
                    status
                }
            };

            // ---- Вкладки данных кластера. ----
            var tabs = new TabControl { Margin = new Thickness(0, 12, 0, 0) };
            tabs.Items.Add(new TabItem
            {
                Header = LocalizationManager.T("ServerMonitor.Tabs.Processes"),
                Content = BuildList("Processes", BuildProcessRow)
            });
            tabs.Items.Add(new TabItem
            {
                Header = LocalizationManager.T("ServerMonitor.Tabs.Sessions"),
                Content = BuildTabWithAction(
                    BuildList("Sessions", BuildSessionRow, "SelectedSession"),
                    BuildActionButton(LocalizationManager.T("ServerMonitor.TerminateSession"), "✕",
                        () => _vm.TerminateSessionCommand.Execute(null)))
            });
            tabs.Items.Add(new TabItem
            {
                Header = LocalizationManager.T("ServerMonitor.Tabs.Connections"),
                Content = BuildTabWithAction(
                    BuildList("Connections", BuildConnectionRow, "SelectedConnection"),
                    BuildActionButton(LocalizationManager.T("ServerMonitor.DisconnectConnection"), "⛓",
                        () => _vm.DisconnectConnectionCommand.Execute(null)))
            });
            tabs.Items.Add(new TabItem
            {
                Header = LocalizationManager.T("ServerMonitor.Tabs.Locks"),
                Content = BuildList("Locks", BuildLockRow)
            });
            // Регламентные задания кластера (0.3.9.178): фильтр по базе, кнопки
            // «Приостановить/Возобновить» (подтверждение — в VM) и «Детали».
            tabs.Items.Add(new TabItem
            {
                Header = LocalizationManager.T("ServerMonitor.Tabs.Jobs"),
                Content = BuildJobsTab()
            });
            tabs.Items.Add(new TabItem
            {
                Header = LocalizationManager.T("ServerMonitor.Tabs.Info"),
                Content = BuildInfoPane()
            });

            var autoRefreshHint = new TextBlock { FontSize = 11, Opacity = 0.65, TextWrapping = TextWrapping.Wrap };
            autoRefreshHint.Bind(TextBlock.TextProperty, new Binding("AutoRefreshText"));

            var root = new Grid
            {
                Margin = new Thickness(16),
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(280, GridUnitType.Pixel)),
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star))
                },
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                }
            };
            // Слева — справочник «Сервера управления» (issue #3), справа — данные кластера.
            Place(root, serversPanel, 0, 0, 5);
            Place(root, clusterPanel, 1, 0);
            Place(root, tabs, 1, 1);
            Place(root, new StackPanel { Children = { status, error, autoRefreshHint }, Spacing = 2 }, 1, 2);
            Content = root;
        }

        /// <summary>
        /// «Диагностика сети…» (0.3.9.232, функция 12): открывает окно диагностики
        /// с текущими адресом/портом монитора. Не требует успешного подключения rac —
        /// диагностика нужна именно до подключения.
        /// </summary>
        private void OpenNetworkDiagnostics()
        {
            var target = NetworkDiagnosticsTargets.FromServerMonitor(_vm.ServerAddress, _vm.ServerPort);
            var vm = new NetworkDiagnosticsViewModel(
                AppServices.GetRequiredService<INetworkDiagnosticsService>(),
                target,
                action => Dispatcher.UIThread.Post(action));
            new NetworkDiagnosticsWindow(vm).ShowDialog(this);
        }

        // ===================== Построители =====================

        private static void Place(Grid grid, Control control, int column, int row, int rowSpan = 1)
        {
            Grid.SetColumn(control, column);
            Grid.SetRow(control, row);
            if (rowSpan > 1)
                Grid.SetRowSpan(control, rowSpan);
            grid.Children.Add(control);
        }

        private static ListBox BuildList(string binding, Func<object, Control> rowFactory, string? selectedBinding = null)
        {
            var list = new ListBox
            {
                ItemTemplate = new FuncDataTemplate<object>((item, _) => rowFactory(item))
            };
            // Тексты строк по вертикали по центру (issue #324): контейнер ListBoxItem
            // центрирует содержимое, MinHeight задаёт единую комфортную высоту строки
            // (аналог RowHeight=34 у DataGrid в WPF-версии окна).
            list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
            {
                Setters =
                {
                    new Setter(ListBoxItem.MinHeightProperty, 36d),
                    new Setter(ListBoxItem.VerticalContentAlignmentProperty, VerticalAlignment.Center)
                }
            });
            list.Bind(ListBox.ItemsSourceProperty, new Binding(binding));
            if (selectedBinding is not null)
                list.Bind(ListBox.SelectedItemProperty, new Binding(selectedBinding, BindingMode.TwoWay));
            return list;
        }

        /// <summary>Вкладка с действием: кнопка снизу (завершение сеанса / разрыв соединения), список заполняет остальное.</summary>
        private static Control BuildTabWithAction(Control content, Button button)
        {
            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(button, Dock.Bottom);
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 8, 0, 0);
            dock.Children.Add(button);
            dock.Children.Add(content);
            return dock;
        }

        private static Button BuildActionButton(string text, string icon, System.Action onClick)
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

        /// <summary>
        /// Вкладка «Информация о кластере» (issue #324, C3): свойства «свойство — значение»,
        /// правится безопасный набор параметров (TextBox у <see cref="RacClusterPropertyRow.IsEditable"/>),
        /// сверху кнопка «Сохранить изменения» (rac «cluster update»).
        /// </summary>
        private Control BuildInfoPane()
        {
            var saveButton = BuildActionButton(
                LocalizationManager.T("ServerMonitor.Cluster.Save"), "💾",
                () => _vm.SaveClusterPropertiesCommand.Execute(null));

            var list = new ItemsControl
            {
                ItemTemplate = new FuncDataTemplate<RacClusterPropertyRow>((row, _) =>
                {
                    var name = new TextBlock
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 12, 0),
                        Width = 340,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        [!TextBlock.TextProperty] = new Binding("DisplayName"),
                        [!ToolTip.TipProperty] = new Binding("RawKey")
                    };

                    // Правка — только для безопасного набора параметров; остальные
                    // свойства показываются текстом «как есть».
                    var editBox = new TextBox
                    {
                        Width = 340,
                        Padding = new Thickness(4, 2),
                        VerticalContentAlignment = VerticalAlignment.Center,
                        [!TextBox.TextProperty] = new Binding("EditValue", BindingMode.TwoWay),
                        [!Control.IsVisibleProperty] = new Binding("IsEditable")
                    };
                    var readOnly = new TextBlock
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0),
                        Width = 340,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        [!TextBlock.TextProperty] = new Binding("OriginalValue"),
                        [!Control.IsVisibleProperty] = new Binding("!IsEditable")
                    };

                    var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() } };
                    Grid.SetColumn(name, 0);
                    Grid.SetColumn(editBox, 1);
                    Grid.SetColumn(readOnly, 1);
                    grid.Children.Add(name);
                    grid.Children.Add(editBox);
                    grid.Children.Add(readOnly);
                    return grid;
                })
            };
            list.Bind(ItemsControl.ItemsSourceProperty, new Binding("ClusterProperties"));

            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(saveButton, Dock.Top);
            saveButton.HorizontalAlignment = HorizontalAlignment.Left;
            saveButton.Margin = new Thickness(0, 0, 0, 8);
            dock.Children.Add(saveButton);
            dock.Children.Add(new ScrollViewer
            {
                Content = list,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(10)
            });
            return dock;
        }

        // ===================== Строки вкладок =====================

        private static Control CellText(string property, bool bold = false, string? colorHex = null)
        {
            var text = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = colorHex is null ? null : new SolidColorBrush(Color.Parse(colorHex))
            };
            text.Bind(TextBlock.TextProperty, new Binding(property));
            return text;
        }

        /// <summary>
        /// Вкладка «Регламентные задания»: фильтр по базе, кнопки действий и таблица.
        /// Подсказка пустого списка обновляется в <see cref="OnVmPropertyChanged"/>.
        /// </summary>
        private Control BuildJobsTab()
        {
            var filterCombo = new ComboBox { Width = 260, VerticalContentAlignment = VerticalAlignment.Center };
            filterCombo.Styled(ControlThemes.ModernComboBox);
            filterCombo.ItemsSource = _vm.JobInfobaseFilterRows;
            filterCombo.ItemTemplate = new FuncDataTemplate<RacJobFilterRow>((row, _) =>
                new TextBlock { Text = row.DisplayText });
            filterCombo.SelectionChanged += (_, _) =>
            {
                if (filterCombo.SelectedItem is RacJobFilterRow row)
                    _vm.SelectedJobInfobaseId = row.Id;
            };

            var pauseButton = BuildActionButton(LocalizationManager.T("ServerMonitor.PauseJob"), "⏸",
                () => _vm.PauseJobCommand.Execute(null));
            var resumeButton = BuildActionButton(LocalizationManager.T("ServerMonitor.ResumeJob"), "▶",
                () => _vm.ResumeJobCommand.Execute(null));
            var detailsButton = BuildActionButton(LocalizationManager.T("ServerMonitor.JobDetails"), "ℹ",
                () =>
                {
                    if (_vm.SelectedJob is not null)
                        new JobDetailsWindow(_vm.SelectedJob.DetailsText).ShowDialog(this);
                });

            // Кнопки действий зависят от состояния выбранного задания (VM уведомляет).
            pauseButton.Bind(Button.IsEnabledProperty, new Binding("CanPauseSelectedJob"));
            resumeButton.Bind(Button.IsEnabledProperty, new Binding("CanResumeSelectedJob"));

            _jobsEmptyHint = new TextBlock
            {
                Text = LocalizationManager.T("ServerMonitor.Empty.Jobs"),
                FontSize = 12,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
                IsVisible = !_vm.HasJobs
            };

            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 0, 0, 8),
                Children =
                {
                    new TextBlock
                    {
                        Text = LocalizationManager.T("ServerMonitor.Columns.Job.Infobase"),
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    filterCombo,
                    pauseButton,
                    resumeButton,
                    detailsButton
                }
            };

            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(toolbar, Dock.Top);
            DockPanel.SetDock(_jobsEmptyHint, Dock.Top);
            dock.Children.Add(toolbar);
            dock.Children.Add(_jobsEmptyHint);
            dock.Children.Add(BuildList("FilteredJobs", BuildJobRow, "SelectedJob"));
            return dock;
        }

        private static Control BuildProcessRow(object item)
        {
            var row = (RacProcessRow)item;
            var runningDot = new TextBlock
            {
                Text = row.Running ? "●" : "○",
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse(row.StateColorHex))
            };

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(90)),
                    new ColumnDefinition(new GridLength(1.2, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(70)),
                    new ColumnDefinition(new GridLength(60)),
                    new ColumnDefinition(new GridLength(130)),
                    new ColumnDefinition(new GridLength(90)),
                    new ColumnDefinition(new GridLength(80)),
                    new ColumnDefinition(new GridLength(80)),
                    new ColumnDefinition(new GridLength(80)),
                    new ColumnDefinition(new GridLength(60)),
                    new ColumnDefinition(new GridLength(30))
                }
            };
            AddCell(grid, CellText("Type"), 0);
            AddCell(grid, CellText("Host"), 1);
            AddCell(grid, CellText("Pid", bold: true), 2);
            AddCell(grid, CellText("Port"), 3);
            AddCell(grid, CellText("StartedAtText"), 4);
            AddCell(grid, CellText("MemorySizeText"), 5);
            AddCell(grid, CellText("Threads"), 6);
            AddCell(grid, CellText("CpuText"), 7);
            AddCell(grid, CellText("RunningText"), 8);
            AddCell(grid, CellText("Infobases"), 9);
            AddCell(grid, runningDot, 10);
            return grid;
        }

        private static Control BuildSessionRow(object item)
        {
            var row = (RacSessionRow)item;
            var blockedDot = new TextBlock
            {
                Text = row.BlockedSymbol,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#D97706"))
            };

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1.2, GridUnitType.Star)),
                    // issue #324, C2: колонка «Информационная база» (имя из
                    // «infobase summary list» по infobase-id сеанса).
                    new ColumnDefinition(new GridLength(1.1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(1.1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(90)),
                    new ColumnDefinition(new GridLength(130)),
                    new ColumnDefinition(new GridLength(130)),
                    new ColumnDefinition(new GridLength(100)),
                    new ColumnDefinition(new GridLength(40)),
                    new ColumnDefinition(new GridLength(90)),
                    new ColumnDefinition(new GridLength(90)),
                    new ColumnDefinition(new GridLength(90)),
                    new ColumnDefinition(new GridLength(80))
                }
            };
            AddCell(grid, CellText("User", bold: true), 0);
            AddCell(grid, CellText("InfobaseName"), 1);
            AddCell(grid, CellText("Host"), 2);
            AddCell(grid, CellText("AppId"), 3);
            AddCell(grid, CellText("StartedAtText"), 4);
            AddCell(grid, CellText("LastActiveAtText"), 5);
            AddCell(grid, CellText("StateText", colorHex: row.StateColorHex), 6);
            AddCell(grid, blockedDot, 7);
            AddCell(grid, CellText("MemoryText"), 8);
            AddCell(grid, CellText("DurationAllText"), 9);
            AddCell(grid, CellText("DurationCurrentText"), 10);
            AddCell(grid, CellText("HibernateText"), 11);
            return grid;
        }

        private static Control BuildConnectionRow(object item)
        {
            var row = (RacConnectionRow)item;
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1.2, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(70)),
                    new ColumnDefinition(new GridLength(100)),
                    new ColumnDefinition(new GridLength(130)),
                    new ColumnDefinition(new GridLength(130)),
                    new ColumnDefinition(new GridLength(100)),
                    new ColumnDefinition(new GridLength(1.3, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(110))
                }
            };
            AddCell(grid, CellText("Host"), 0);
            AddCell(grid, CellText("Port"), 1);
            AddCell(grid, CellText("Connector"), 2);
            AddCell(grid, CellText("EstablishedAtText"), 3);
            AddCell(grid, CellText("LastConnectionTimeText"), 4);
            AddCell(grid, CellText("DurationText"), 5);
            AddCell(grid, CellText("Descr"), 6);
            AddCell(grid, CellText("BlockedText", colorHex: row.BlockedColorHex), 7);
            return grid;
        }

        private static Control BuildJobRow(object item)
        {
            var row = (RacJobRow)item;
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1.4, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(1.1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(1.1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(1.1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(110)),
                    new ColumnDefinition(new GridLength(130)),
                    new ColumnDefinition(new GridLength(130)),
                    new ColumnDefinition(new GridLength(80)),
                    new ColumnDefinition(new GridLength(1.2, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(90))
                }
            };
            AddCell(grid, CellText("Name", bold: true), 0);
            AddCell(grid, CellText("InfobaseName"), 1);
            AddCell(grid, CellText("MethodName"), 2);
            AddCell(grid, CellText("Schedule"), 3);
            AddCell(grid, CellText("StateText", colorHex: row.StateColorHex), 4);
            AddCell(grid, CellText("NextStartText"), 5);
            AddCell(grid, CellText("LastStartText"), 6);
            AddCell(grid, CellText("LastSuccessText"), 7);
            AddCell(grid, CellText("ResultText"), 8);
            AddCell(grid, CellText("PredefinedText"), 9);
            return grid;
        }

        private static Control BuildLockRow(object item)
        {
            var row = (RacLockRow)item;
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(90)),
                    new ColumnDefinition(new GridLength(90)),
                    new ColumnDefinition(new GridLength(1.1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(1.1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(1.1, GridUnitType.Star))
                }
            };
            AddCell(grid, CellText("Object", bold: true), 0);
            AddCell(grid, CellText("WaitingSymbol", colorHex: "#D97706"), 1);
            AddCell(grid, CellText("BlockingSymbol", colorHex: "#DC2626"), 2);
            AddCell(grid, CellText("SessionId"), 3);
            AddCell(grid, CellText("ConnectionId"), 4);
            AddCell(grid, CellText("TransactionId"), 5);
            return grid;
        }

        private static void AddCell(Grid grid, Control control, int column)
        {
            Grid.SetColumn(control, column);
            grid.Children.Add(control);
        }

        // ===================== Синхронизация =====================

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ServerMonitorViewModel.HasJobs) && _jobsEmptyHint is not null)
            {
                // Подсказка «заданий нет / нет прав администратора» видна только при пустом списке.
                _jobsEmptyHint.IsVisible = !_vm.HasJobs;
                return;
            }

            // issue #324 (0.3.9.330): ItemsSource комбо кластеров задаётся в конструкторе,
            // когда ClusterRows ещё пуст, — после подключения список надо обновить,
            // иначе в выпадающем списке нечего выбирать.
            if (e.PropertyName == nameof(ServerMonitorViewModel.ClusterRows))
            {
                _clusterCombo.ItemsSource = _vm.ClusterRows;
                return;
            }

            if (e.PropertyName != nameof(ServerMonitorViewModel.SelectedClusterId))
                return;
            if (_vm.SelectedClusterId is Guid id)
            {
                var row = _vm.ClusterRows.FirstOrDefault(r => r.Id == id);
                if (row is not null && !ReferenceEquals(_clusterCombo.SelectedItem, row))
                    _clusterCombo.SelectedItem = row;
            }
        }

        // ===================== Справочник «Сервера управления» (issue #3) =====================

        /// <summary>«Добавить»: модальный редактор новой записи → добавление в справочник.</summary>
        private void OnAddServer()
        {
            var edit = new ServerEditWindow();
            if (!edit.ShowSync(this) || edit.Result is not { } created)
                return;
            _vm.AddServer(created);
        }

        /// <summary>«Изменить»: модальный редактор выбранной записи (правка на копии).</summary>
        private void OnEditServer()
        {
            if (_serversList.SelectedItem is not ManagementServer server)
                return;
            var edit = new ServerEditWindow(CloneServer(server));
            if (!edit.ShowSync(this) || edit.Result is not { } updated)
                return; // Отмена — модель не изменялась.
            _vm.UpdateServer(updated);
        }

        /// <summary>«Удалить»: подтверждение → удаление записи из справочника.</summary>
        private void OnDeleteServer()
        {
            if (_serversList.SelectedItem is not ManagementServer server)
                return;
            if (!AppServices.GetRequiredService<IDialogService>().Confirm(
                    string.Format(LocalizationManager.T("ServerMonitor.Servers.ConfirmDelete"), server.Description),
                    LocalizationManager.T("ServerMonitor.Servers.Title")))
                return;
            _vm.RemoveServer(server);
        }

        /// <summary>Глубокая копия записи (для правки без мутации исходного экземпляра).</summary>
        private static ManagementServer CloneServer(ManagementServer source) => new()
        {
            Id = source.Id,
            Description = source.Description,
            Address = source.Address,
            Port = source.Port,
            User = source.User,
            Password = source.Password,
        };
    }
}
#endif