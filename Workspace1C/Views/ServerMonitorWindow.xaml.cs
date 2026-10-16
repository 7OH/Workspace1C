using System;
using System.Windows;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;

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
            ProcessesGrid.ItemsSource = _vm.Processes;
            SessionsGrid.ItemsSource = _vm.Sessions;
            ConnectionsGrid.ItemsSource = _vm.Connections;
            LocksGrid.ItemsSource = _vm.Locks;
            JobsGrid.ItemsSource = _vm.FilteredJobs;

            // Активация строки справочника → подключение к кластеру (issue #3).
            ServersList.SelectionChanged += OnServersList_SelectionChanged;

            // Таймер автообновления останавливается при закрытии окна (без утечки).
            Closed += (_, _) => _vm.Dispose();
        }

        /// <summary>
        /// Активация строки справочника «Сервера управления» (issue #3): подключаемся
        /// к кластеру выбранного сервера (адрес/порт/логин/пароль берутся из записи).
        /// </summary>
        private async void OnServersList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ServersList.SelectedItem is ManagementServer server)
                await _vm.ConnectToServerAsync(server);
        }

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

        private void OnPauseJob_Click(object sender, RoutedEventArgs e) =>
            _vm.PauseJobCommand.Execute(null);

        private void OnResumeJob_Click(object sender, RoutedEventArgs e) =>
            _vm.ResumeJobCommand.Execute(null);

        private void OnJobDetails_Click(object sender, RoutedEventArgs e)
        {
            var row = _vm.SelectedJob;
            if (row is null)
                return;
            new JobDetailsWindow(row.DetailsText) { Owner = this }.ShowDialog();
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
