#if LINUX
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
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
    /// серверов (только Описание) с кнопками «Добавить/Изменить/Удалить»; подключение —
    /// по кнопке «Подключиться» или двойному клику по строке (НЕ по активации строки).
    /// Первая строка правой панели — кнопки «Подключиться/Обновить/Диагностика сети»,
    /// вторая — выбор кластера, автообновление и статус. Вкладки «Рабочие процессы /
    /// Сеансы / Соединения / Блокировки / Регламентные задания / Базы / Информация о
    /// кластере». Сортировка колонок — кликом по заголовку (issue #3). Вся логика —
    /// в чистой ViewModel <see cref="ServerMonitorViewModel"/>. Адрес/порт/логин/пароль
    /// хранятся в справочнике (server_list.json) и на форме не показываются.
    /// </summary>
    public sealed class ServerMonitorWindow : ModalWindowBase
    {
        // Окно большое и изменяемого размера — показываем кнопку «Развернуть»
        // в собственном chrome (issue #324; на Windows системная кнопка уже есть).
        protected override bool SupportsMaximizeButton => true;

        private readonly ServerMonitorViewModel _vm;
        private readonly ComboBox _clusterCombo;
        private readonly ListBox _serversList;
        private TextBlock? _infobasesEmptyHint;

        public ServerMonitorWindow()
        {
            Title = LocalizationManager.T("ServerMonitor.Title");
            Width = 1280;
            Height = 760;
            MinWidth = 980;
            MinHeight = 560;
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
                // Строки списка серверов крупнее (issue #3): как строки в списке баз —
                // выше высота и крупнее шрифт (строку удобно выделять для правки).
                ItemTemplate = new FuncDataTemplate<ManagementServer>((server, _) =>
                {
                    var text = new TextBlock
                    {
                        Text = server.Description,
                        FontSize = 14,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    return text;
                })
            };
            _serversList.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
            {
                Setters =
                {
                    new Setter(ListBoxItem.MinHeightProperty, 40d),
                    new Setter(ListBoxItem.PaddingProperty, new Thickness(8, 4)),
                    new Setter(ListBoxItem.VerticalContentAlignmentProperty, VerticalAlignment.Center)
                }
            });
            _serversList.Bind(ListBox.ItemsSourceProperty, new Binding("Servers"));
            _serversList.Bind(ListBox.SelectedItemProperty, new Binding("SelectedServer", BindingMode.TwoWay));
            // Подключение по двойному клику (issue #3) — простое выделение строки для
            // правки подключение НЕ запускает (SelectionChanged остаётся для выбора).
            _serversList.DoubleTapped += (_, _) =>
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

            // ---- Первая строка панели (issue #3): «Подключиться», «Обновить», «Диагностика». ----
            var connectButton = BuildActionButton(LocalizationManager.T("ServerMonitor.Connect"), "🔗",
                () => _vm.ConnectCommand.Execute(null));
            var refreshButton = BuildActionButton(LocalizationManager.T("ServerMonitor.Refresh"), "⟳", _vm.Refresh);

            // «Диагностика сети…» (0.3.9.232, функция 12): DNS/ICMP/TCP-проверка
            // адреса:порт монитора и стандартных портов 1С. Работает до подключения rac.
            var diagnosticsButton = BuildActionButton(
                LocalizationManager.T("ServerMonitor.NetworkDiagnostics"), "🛜", OpenNetworkDiagnostics);

            var actionPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 0, 0, 8),
                Children = { connectButton, refreshButton, diagnosticsButton }
            };

            // ---- Вторая строка панели: кластер, автообновление, статус подключения. ----
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
                    status
                }
            };

            // ---- Вкладки данных кластера (заголовки ужаты, issue #3). ----
            // issue #3: сеансы/соединения/блокировки биндятся на ОТФИЛЬТРОВАННЫЕ коллекции —
            // выбор базы фильтрует их на клиенте без перезагрузки кластера.
            var tabs = new TabControl { Margin = new Thickness(0, 12, 0, 0) };
            tabs.Items.Add(CreateTab(LocalizationManager.T("ServerMonitor.Tabs.Processes"),
                BuildContent(() => BuildSortableTable(_vm.Processes, ProcessColumns, BuildProcessRow))));
            tabs.Items.Add(CreateTab(LocalizationManager.T("ServerMonitor.Tabs.Sessions"),
                BuildTabWithAction(
                    BuildSortableTable(_vm.FilteredSessions, SessionColumns, BuildSessionRow, "SelectedSession"),
                    BuildActionButton(LocalizationManager.T("ServerMonitor.TerminateSession"), "✕",
                        () => _vm.TerminateSessionCommand.Execute(null)))));
            tabs.Items.Add(CreateTab(LocalizationManager.T("ServerMonitor.Tabs.Connections"),
                BuildTabWithAction(
                    BuildSortableTable(_vm.FilteredConnections, ConnectionColumns, BuildConnectionRow, "SelectedConnection"),
                    BuildActionButton(LocalizationManager.T("ServerMonitor.DisconnectConnection"), "⛓",
                        () => _vm.DisconnectConnectionCommand.Execute(null)))));
            tabs.Items.Add(CreateTab(LocalizationManager.T("ServerMonitor.Tabs.Locks"),
                BuildSortableTable(_vm.FilteredLocks, LockColumns, BuildLockRow)));
            // Базы кластера (issue #3): список, добавление/удаление, выбор базы
            // фильтрует сеансы/соединения/блокировки на клиенте.
            tabs.Items.Add(CreateTab(LocalizationManager.T("ServerMonitor.Tabs.Infobases"), BuildInfobasesTab()));
            tabs.Items.Add(CreateTab(LocalizationManager.T("ServerMonitor.Tabs.Info"), BuildInfoPane()));

            // Индикатор фильтра по базе (issue #3): виден при выбранной базе, показывает
            // имя базы и кнопку «Сбросить» (клиентская фильтрация, без rac-вызовов).
            var filterText = new TextBlock
            {
                FontSize = 12,
                Opacity = 0.65,
                VerticalAlignment = VerticalAlignment.Center,
                [!TextBlock.TextProperty] = new Binding("InfobaseFilterText")
            };
            var clearFilterButton = BuildActionButton(
                LocalizationManager.T("ServerMonitor.Infobase.FilterReset"), "✕", _vm.ClearInfobaseFilter);
            var filterPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 0, 0, 8),
                IsVisible = _vm.HasInfobaseFilter,
                Children = { filterText, clearFilterButton }
            };
            _vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ServerMonitorViewModel.HasInfobaseFilter))
                    filterPanel.IsVisible = _vm.HasInfobaseFilter;
            };

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
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto)
                }
            };
            // Слева — справочник «Сервера управления» (issue #3), справа — данные кластера.
            Place(root, serversPanel, 0, 0, 5);
            Place(root, actionPanel, 1, 0);
            Place(root, clusterPanel, 1, 1);
            Place(root, filterPanel, 1, 2);
            Place(root, tabs, 1, 3);
            Place(root, new StackPanel { Children = { error, autoRefreshHint }, Spacing = 2 }, 1, 4);
            Content = root;
        }

        private TabItem CreateTab(string header, Control content) => new()
        {
            Header = header,
            Padding = new Thickness(14, 6),
            FontSize = 12.5,
            Content = content
        };

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

        /// <summary>Колонка таблицы: заголовок, свойство отображения, свойство сортировки (может быть null), ширина.</summary>
        private sealed record TableColumn(string Header, string DisplayPath, string? SortPath, GridLength Width);

        /// <summary>
        /// Таблица с заголовками колонок и сортировкой по клику (issue #3): шапка из
        /// кнопок поверх ListBox; при клике данные пересобираются в отсортированном
        /// порядке (та же схема, что в MainViewModel — без ICollectionView). При
        /// изменении исходной коллекции (автообновление) сортировка применяется заново.
        /// </summary>
        private Control BuildSortableTable<T>(
            ObservableCollection<T> source,
            IReadOnlyList<TableColumn> columns,
            Func<object, Control> rowFactory,
            string? selectedBinding = null)
        {
            var list = new ListBox
            {
                ItemTemplate = new FuncDataTemplate<object>((item, _) => rowFactory(item))
            };
            // Тексты строк по вертикали по центру (issue #324): контейнер ListBoxItem
            // центрирует содержимое, MinHeight задаёт единую комфортную высоту строки.
            list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
            {
                Setters =
                {
                    new Setter(ListBoxItem.MinHeightProperty, 36d),
                    new Setter(ListBoxItem.VerticalContentAlignmentProperty, VerticalAlignment.Center)
                }
            });

            string? sortPath = null;
            var sortDescending = false;

            // Пересобирает ItemsSource с учётом текущей сортировки (или возвращает источник).
            void Rebuild()
            {
                var previous = list.SelectedItem;
                IReadOnlyList<object> items;
                if (sortPath is null)
                {
                    items = source.Cast<object>().ToList();
                }
                else
                {
                    var comparer = Comparer<object?>.Create(CompareSortValues);
                    var keyed = source.Cast<object>().Select(x => (Item: x, Key: GetSortValue(x, sortPath)));
                    keyed = sortDescending
                        ? keyed.OrderByDescending(t => t.Key, comparer)
                        : keyed.OrderBy(t => t.Key, comparer);
                    items = keyed.Select(t => t.Item).ToList();
                }

                list.ItemsSource = items;
                // Восстанавливаем выбор, если объект остался в списке (после автообновления
                // выбор по идентификатору восстанавливает сама ViewModel).
                if (previous is not null && items.Contains(previous))
                    list.SelectedItem = previous;
            }

            // Заголовки-кнопки: клик сортирует по колонке (повторный клик меняет направление).
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var width in columns)
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition(width.Width));
            var arrows = new Dictionary<TableColumn, TextBlock>();

            for (var i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                // Стрелка сортировки (issue #3): акцентный цвет, не переносится и не
                // «съедается» длинным заголовком — заголовок усекается (TextTrimming),
                // а стрелка всегда видна справа в отдельной авто-колонке.
                var arrow = new TextBlock
                {
                    Text = string.Empty,
                    FontSize = 10,
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.NoWrap
                };
                ThemeBrushes.Bind(arrow, TextBlock.ForegroundProperty, "AccentBrush");
                arrows[col] = arrow;
                var headerText = new TextBlock
                {
                    Text = col.Header,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                var headerPanel = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                        new ColumnDefinition(GridLength.Auto)
                    }
                };
                Grid.SetColumn(headerText, 0);
                Grid.SetColumn(arrow, 1);
                headerPanel.Children.Add(headerText);
                headerPanel.Children.Add(arrow);
                var button = new Button
                {
                    Content = headerPanel,
                    Padding = new Thickness(6, 2),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };
                button.Styled(ControlThemes.ModernButton);
                ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
                button.Click += (_, _) =>
                {
                    if (col.SortPath is null)
                        return;
                    if (sortPath == col.SortPath)
                        sortDescending = !sortDescending;
                    else
                    {
                        sortPath = col.SortPath;
                        sortDescending = false;
                    }
                    foreach (var pair in arrows)
                        pair.Value.Text = pair.Key == col
                            ? (sortDescending ? "▼" : "▲")
                            : string.Empty;
                    Rebuild();
                };
                Grid.SetColumn(button, i);
                headerGrid.Children.Add(button);
            }

            // Исходная коллекция меняется (автообновление/переключение баз) — применяем сортировку заново.
            if (source is INotifyCollectionChanged notify)
            {
                notify.CollectionChanged += (_, _) =>
                {
                    if (sortPath is not null)
                        Rebuild();
                };
            }

            list.ItemsSource = source;
            if (selectedBinding is not null)
                list.Bind(ListBox.SelectedItemProperty, new Binding(selectedBinding, BindingMode.TwoWay));

            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(headerGrid, Dock.Top);
            dock.Children.Add(headerGrid);
            dock.Children.Add(list);
            return dock;
        }

        private static object? GetSortValue(object item, string path)
        {
            var type = item.GetType();
            var prop = type.GetProperty(path, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop is null || !prop.CanRead)
                return null;
            try
            {
                return prop.GetValue(item);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Сравнение значений сортировки: числа/даты — как IComparable, строки — без учёта регистра.</summary>
        private static int CompareSortValues(object? a, object? b)
        {
            if (a is null || b is null)
                return (a is null, b is null) switch
                {
                    (true, true) => 0,
                    (true, false) => -1,
                    _ => 1
                };
            if (a is IComparable comparable && a.GetType() == b.GetType())
                return comparable.CompareTo(b);
            return string.Compare(
                Convert.ToString(a, CultureInfo.InvariantCulture),
                Convert.ToString(b, CultureInfo.InvariantCulture),
                StringComparison.CurrentCultureIgnoreCase);
        }

        /// <summary>Внутренний LayoutRoot обёртки (для прямого включения без SortableTable).</summary>
        private static Control BuildContent(Func<Control> factory) => factory();

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

        // ===================== Колонки вкладок =====================

        private static GridLength Px(double value) => new(value, GridUnitType.Pixel);
        private static GridLength Star(double value) => new(value, GridUnitType.Star);

        /// <summary>Колонки вкладки «Рабочие процессы».</summary>
        private static readonly IReadOnlyList<TableColumn> ProcessColumns = new[]
        {
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Process.Type"), "Type", "Type", Px(90)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Process.Host"), "Host", "Host", Star(1.2)),
            new TableColumn("PID", "Pid", "Pid", Px(70)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Process.Port"), "Port", "Port", Px(60)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Process.StartedAt"), "StartedAtText", "StartedAtText", Px(130)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Process.Memory"), "MemorySizeText", "MemorySizeText", Px(90)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Process.Threads"), "Threads", "Threads", Px(80)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Process.Cpu"), "CpuText", "CpuText", Px(80)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Process.Running"), "RunningText", "Running", Px(80)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Process.Infobases"), "Infobases", "Infobases", Px(60)),
            new TableColumn("●", string.Empty, null, Px(30))
        };

        /// <summary>Колонки вкладки «Сеансы».</summary>
        private static readonly IReadOnlyList<TableColumn> SessionColumns = new[]
        {
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.User"), "User", "User", Star(1.2)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.Infobase"), "InfobaseName", "InfobaseName", Star(1.1)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.Host"), "Host", "Host", Star(1.1)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.App"), "AppId", "AppId", Px(90)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.StartedAt"), "StartedAtText", "StartedAtText", Px(130)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.LastActive"), "LastActiveAtText", "LastActiveAtText", Px(130)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.State"), "StateText", "State", Px(100)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.Blocked"), "BlockedSymbol", "BlockedByLs", Px(40)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.Memory"), "MemoryText", "MemoryText", Px(90)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.Duration"), "DurationAllText", "DurationAllText", Px(90)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.DurationCurrent"), "DurationCurrentText", "DurationCurrentText", Px(90)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.Hibernate"), "HibernateText", "Hibernate", Px(80))
        };

        /// <summary>Колонки вкладки «Соединения».</summary>
        private static readonly IReadOnlyList<TableColumn> ConnectionColumns = new[]
        {
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Connection.Host"), "Host", "Host", Star(1.2)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Connection.Port"), "Port", "Port", Px(70)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Connection.Connector"), "Connector", "Connector", Px(100)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Connection.Established"), "EstablishedAtText", "EstablishedAtText", Px(130)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Connection.LastActive"), "LastConnectionTimeText", "LastConnectionTimeText", Px(130)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Connection.Duration"), "DurationText", "DurationText", Px(100)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Connection.Descr"), "Descr", "Descr", Star(1.3)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Connection.Blocked"), "BlockedText", "Blocked", Px(110))
        };

        /// <summary>Колонки вкладки «Блокировки».</summary>
        private static readonly IReadOnlyList<TableColumn> LockColumns = new[]
        {
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Lock.Object"), "Object", "Object", Star(2)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Lock.Waiting"), "WaitingSymbol", "Waiting", Px(90)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Lock.Blocking"), "BlockingSymbol", "Blocking", Px(90)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Lock.Session"), "SessionId", "SessionId", Star(1.1)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Lock.Connection"), "ConnectionId", "ConnectionId", Star(1.1)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Lock.Transaction"), "TransactionId", "TransactionId", Star(1.1))
        };

        /// <summary>Колонки вкладки «Базы» (issue #3).</summary>
        private static readonly IReadOnlyList<TableColumn> InfobaseColumns = new[]
        {
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Infobase.Name"), "Name", "Name", Star(1.2)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Infobase.Descr"), "Descr", "Descr", Star(1.5)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Infobase.Dbms"), "DbmsText", "Dbms", Px(110)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Infobase.DbServer"), "DbServer", "DbServer", Star(1.1)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Infobase.DbName"), "DbName", "DbName", Star(1)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Infobase.DbUser"), "DbUser", "DbUser", Px(120)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Infobase.Locale"), "Locale", "Locale", Px(80)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Infobase.SecurityLevel"), "SecurityLevel", "SecurityLevel", Px(105)),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Infobase.Licensed"), "LicensedText", "Licensed", Px(95))
        };

        // ===================== Вкладки-конструкторы =====================

        /// <summary>
        /// Вкладка «Информация о кластере» (issue #324, C3): свойства «свойство — значение»,
        /// правится безопасный набор параметров (TextBox у <see cref="RacClusterPropertyRow.IsEditable"/>),
        /// сверху кнопка «Сохранить изменения» (rac «cluster update»). Локализованные ключи —
        /// в <see cref="RacClusterPropertyRow.DisplayName"/> (Issue #3: человекочитаемость).
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

        /// <summary>
        /// Вкладка «Базы» (issue #3): список информационных баз кластера с кнопками
        /// «Добавить базу» (rac «infobase create») и «Удалить базу» (rac «infobase drop»,
        /// подтверждение — в VM). Выбор базы фильтрует сеансы/соединения/блокировки
        /// на клиенте (без rac-вызовов).
        /// </summary>
        private Control BuildInfobasesTab()
        {
            var addButton = BuildActionButton(LocalizationManager.T("ServerMonitor.Infobase.Add"), "＋", OnAddInfobase);
            var deleteButton = BuildActionButton(LocalizationManager.T("ServerMonitor.Infobase.Delete"), "🗑",
                () => _vm.DropInfobaseCommand.Execute(null));
            // Кнопка «Удалить» активна только при выбранной базе и подключении.
            deleteButton.Bind(Button.IsEnabledProperty, new Binding("CanDropInfobase"));

            _infobasesEmptyHint = new TextBlock
            {
                Text = LocalizationManager.T("ServerMonitor.Infobase.Empty"),
                FontSize = 12,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
                IsVisible = !_vm.HasInfobases
            };

            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 0, 0, 8),
                Children = { addButton, deleteButton }
            };

            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(toolbar, Dock.Top);
            DockPanel.SetDock(_infobasesEmptyHint, Dock.Top);
            dock.Children.Add(toolbar);
            dock.Children.Add(_infobasesEmptyHint);
            dock.Children.Add(BuildSortableTable(_vm.Infobases, InfobaseColumns, BuildInfobaseRow, "SelectedInfobase"));
            return dock;
        }

        /// <summary>«Добавить базу»: диалог создания (rac «infobase create») → VM → обновление списков.</summary>
        private void OnAddInfobase()
        {
            var dialog = new RacInfobaseCreateWindow();
            if (!dialog.ShowSync(this) || dialog.Result is not { } request)
                return;
            _ = _vm.CreateInfobaseAsync(request);
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

        /// <summary>Строка вкладки «Базы» (issue #3).</summary>
        private static Control BuildInfobaseRow(object item)
        {
            var row = (RacInfobaseRow)item;
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1.2, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(1.5, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(110)),
                    new ColumnDefinition(new GridLength(1.1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(120)),
                    new ColumnDefinition(new GridLength(80)),
                    new ColumnDefinition(new GridLength(105)),
                    new ColumnDefinition(new GridLength(95))
                }
            };
            AddCell(grid, CellText("Name", bold: true), 0);
            AddCell(grid, CellText("Descr"), 1);
            AddCell(grid, CellText("DbmsText"), 2);
            AddCell(grid, CellText("DbServer"), 3);
            AddCell(grid, CellText("DbName"), 4);
            AddCell(grid, CellText("DbUser"), 5);
            AddCell(grid, CellText("Locale"), 6);
            AddCell(grid, CellText("SecurityLevel"), 7);
            AddCell(grid, CellText("LicensedText"), 8);
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
            if (e.PropertyName == nameof(ServerMonitorViewModel.HasInfobases) && _infobasesEmptyHint is not null)
            {
                // Подсказка «баз нет» видна только при пустом списке.
                _infobasesEmptyHint.IsVisible = !_vm.HasInfobases;
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