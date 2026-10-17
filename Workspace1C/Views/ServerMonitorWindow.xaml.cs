using System;
using System.Windows;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using System.Windows.Controls;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Серверы 1С» (0.3.9.124, Windows/WPF): встроенный монитор серверов 1С
    /// через утилиту rac. Слева — справочник «Сервера управления» (issue #3): список
    /// серверов (только Описание) с кнопками «Добавить/Изменить/Удалить»; при активации
    /// строки выполняется подключение к кластеру. Справа — выбор кластера, вкладки
    /// «Рабочие процессы / Сеансы / Соединения / Блокировки / Регламентные задания /
    /// Информация о кластере» и статус-строка. Вся логика — в чистой ViewModel
    /// <see cref="ServerMonitorViewModel"/>. Адрес/порт/логин/пароль хранятся в
    /// справочнике (server_list.json) и на форме не показываются.
    /// </summary>
    public partial class ServerMonitorWindow : Window
    {
        private readonly ServerMonitorViewModel _vm;

        public ServerMonitorWindow()
        {
            InitializeComponent();

            var rac = AppServices.GetRequiredService<IRacClient>();
            var dialogs = AppServices.GetRequiredService<IDialogService>();
            var serverListStore = AppServices.GetRequiredService<IServerListStore>();

            _vm = new ServerMonitorViewModel(
                rac,
                dialogs,
                action => Application.Current?.Dispatcher.BeginInvoke(action),
                serverListStore);

            DataContext = _vm;
            Title = LocalizationManager.T("ServerMonitor.Title");
            // Кнопка «Закрыть» на панели подключения убрана (issue #324) — есть кнопка ✕
            // в заголовке окна. Esc сохранён: раньше его давала IsCancel у панельной
            // кнопки, теперь обрабатываем вручную (фокус может быть в текстовом поле).
            PreviewKeyDown += OnWindowPreviewKeyDown;
            // issue #3: таблицы сеансов/соединений/блокировок биндятся на ОТФИЛЬТРОВАННЫЕ
            // коллекции — выбор базы фильтрует их на клиенте без перезагрузки кластера.
            // Список баз и заголовки вкладок привязаны в XAML (Infobases, *TabText).
            SessionsGrid.ItemsSource = _vm.FilteredSessions;
            ConnectionsGrid.ItemsSource = _vm.FilteredConnections;
            LocksGrid.ItemsSource = _vm.FilteredLocks;
            ProcessesGrid.ItemsSource = _vm.Processes;

            // Состояние окна запоминается (issue #3, комментарий 7OH): максимизировано или нет.
            try
            {
                var repository = AppServices.GetRequiredService<IInfobaseRepository>();
                var settings = repository.LoadSettings();
                if (settings.ServerMonitorMaximized)
                    WindowState = WindowState.Maximized;
            }
            catch
            {
                // Запоминание состояния — вспомогательная возможность, сбой не важен.
            }

            // Таймер автообновления останавливается при закрытии окна (без утечки);
            // состояние «максимизировано» сохраняется в настройки.
            Closed += (_, _) =>
            {
                _vm.Dispose();
                try
                {
                    var repository = AppServices.GetRequiredService<IInfobaseRepository>();
                    var settings = repository.LoadSettings();
                    settings.ServerMonitorMaximized = WindowState == WindowState.Maximized;
                    repository.SaveSettings(settings);
                }
                catch
                {
                    // Запоминание состояния — вспомогательная возможность, сбой не важен.
                }
            };
        }

        /// <summary>
        /// Двойной клик по строке справочника «Сервера управления» (issue #3): подключение
        /// к кластеру сервера (адрес/порт/логин/пароль из записи). Простое выделение строки
        /// подключение НЕ запускает — строку можно спокойно выделять для правки.
        /// </summary>
        private async void OnServersList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ServersList.SelectedItem is ManagementServer server)
                await _vm.ConnectToServerAsync(server);
        }

        /// <summary>
        /// «Подключиться» (issue #3): подключение к кластеру выбранного сервера
        /// справочника (если строка выделена); без выделения — по текущим полям.
        /// </summary>
        private void OnConnect_Click(object sender, RoutedEventArgs e) =>
            _vm.ConnectCommand.Execute(null);

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void OnRefresh_Click(object sender, RoutedEventArgs e) => _vm.Refresh();

        private void OnTerminateSession_Click(object sender, RoutedEventArgs e) =>
            _vm.TerminateSessionCommand.Execute(null);

        private void OnDisconnectConnection_Click(object sender, RoutedEventArgs e) =>
            _vm.DisconnectConnectionCommand.Execute(null);

        /// <summary>
        /// «Сбросить» фильтр по базе (issue #3): клиентская операция — показываются
        /// сеансы/соединения/блокировки всех баз кластера, без rac-вызовов.
        /// </summary>
        private void OnClearInfobaseFilter_Click(object sender, RoutedEventArgs e) =>
            _vm.ClearInfobaseFilter();

        /// <summary>
        /// Сортировка колонок DataGrid (issue #3): явно управляем индикаторами — стрелка
        /// показывается у активной колонки, у остальных сбрасывается. Саму сортировку
        /// выполняет DataGrid (e.Handled = false), направление он переключает сам.
        /// </summary>
        private void OnGridSorting(object sender, DataGridSortingEventArgs e)
        {
            var grid = (DataGrid)sender;
            foreach (var column in grid.Columns)
            {
                if (column != e.Column)
                    column.SortDirection = null;
            }
            e.Handled = false;
        }

        /// <summary>
        /// «Сохранить изменения» на вкладке «Информация о кластере» (issue #324, C3):
        /// rac «cluster update» по изменённым параметрам, перечитывание данных.
        /// </summary>
        private void OnSaveClusterInfo_Click(object sender, RoutedEventArgs e) =>
            _vm.SaveClusterPropertiesCommand.Execute(null);

        /// <summary>
        /// «Диагностика сети…» (0.3.9.232, функция 12): открывает окно диагностики
        /// с адресом/портом выбранного сервера справочника. Не требует успешного
        /// подключения rac — диагностика нужна именно до подключения.
        /// </summary>
        private void OnDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            var target = NetworkDiagnosticsTargets.FromServerMonitor(_vm.ServerAddress, _vm.ServerPort);
            var vm = new NetworkDiagnosticsViewModel(
                AppServices.GetRequiredService<INetworkDiagnosticsService>(),
                target,
                action => Application.Current?.Dispatcher.BeginInvoke(action));
            new NetworkDiagnosticsWindow(vm) { Owner = this }.ShowDialog();
        }

        // ===================== Базы кластера (issue #3) =====================

        /// <summary>
        /// «Добавить базу»: модальный диалог создания информационной базы (rac
        /// «infobase create») → создание в выбранном кластере → обновление списков.
        /// </summary>
        private async void OnAddInfobase_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new RacInfobaseCreateWindow() { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is not { } request)
                return;
            await _vm.CreateInfobaseAsync(request);
        }

        /// <summary>
        /// «Изменить базу» (issue #3, комментарий 7OH): модальный диалог правки свойств
        /// (поля предзаполнены, rac «infobase update») → изменение → обновление списков.
        /// </summary>
        private async void OnEditInfobase_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedInfobase is not { } row || row.InfobaseId == Guid.Empty)
                return;
            var dialog = new RacInfobaseCreateWindow(SummaryFromRow(row)) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.UpdateResult is not { } update)
                return;
            await _vm.UpdateSelectedInfobaseAsync(update);
        }

        /// <summary>Строит «infobase summary» из строки списка для предзаполнения диалога правки.</summary>
        private static RacInfobaseSummary SummaryFromRow(RacInfobaseRow row) => new()
        {
            InfobaseId = row.InfobaseId,
            Name = row.Name,
            // RacInfobaseRow подставляет «—» для пустых строк — в диалог возвращаем пустоту.
            Descr = Unplacehold(row.Descr),
            Dbms = row.Dbms,
            DbServer = row.DbServer,
            DbName = row.DbName,
            DbUser = Unplacehold(row.DbUser),
            Locale = Unplacehold(row.Locale),
            SecurityLevel = row.SecurityLevel
        };

        private static string Unplacehold(string value) =>
            string.Equals(value, "—", StringComparison.Ordinal) ? string.Empty : value;

        /// <summary>
        /// «Удалить базу»: подтверждение (в VM) → rac «infobase drop» → обновление.
        /// </summary>
        private void OnDeleteInfobase_Click(object sender, RoutedEventArgs e) =>
            _vm.DropInfobaseCommand.Execute(null);

        // ===================== Справочник «Сервера управления» (issue #3) =====================

        /// <summary>«Добавить»: модальный редактор новой записи → добавление в справочник.</summary>
        private void OnAddServer_Click(object sender, RoutedEventArgs e)
        {
            var edit = new ServerEditWindow() { Owner = this };
            if (edit.ShowDialog() != true || edit.Result is not { } created)
                return;
            _vm.AddServer(created);
        }

        /// <summary>«Изменить»: модальный редактор выбранной записи (правка на копии).</summary>
        private void OnEditServer_Click(object sender, RoutedEventArgs e)
        {
            if (ServersList.SelectedItem is not ManagementServer server)
                return;
            var edit = new ServerEditWindow(CloneServer(server)) { Owner = this };
            if (edit.ShowDialog() != true || edit.Result is not { } updated)
                return; // Отмена — модель не изменялась.
            _vm.UpdateServer(updated);
        }

        /// <summary>«Удалить»: подтверждение → удаление записи из справочника.</summary>
        private void OnDeleteServer_Click(object sender, RoutedEventArgs e)
        {
            if (ServersList.SelectedItem is not ManagementServer server)
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
