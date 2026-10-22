using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Миграция баз» (1.0.0.51, issue #41, Windows/WPF): перенос информационных баз
    /// из кластера-источника (окно «Импорт из кластера 1С») в выбранный кластер другого
    /// сервера 1С. Вся логика — в чистой ViewModel <see cref="ClusterMigrationViewModel"/>;
    /// окно — тонкая обёртка: выбор сервера/кластера назначения (сервер-источник скрыт),
    /// учётные данные СУБД (пароль из PasswordBox в память VM), вопрос «Хотите заменить
    /// строку подключения?» после успешного переноса и закрытие с результатом.
    /// После <c>ShowDialog()==true</c> результат доступен через <see cref="Result"/>.
    /// </summary>
    public partial class ClusterMigrationWindow : Window
    {
        private readonly ClusterMigrationViewModel _vm;

        /// <param name="rac">Клиент rac (кластеры назначения, «infobase create/drop»).</param>
        /// <param name="sourceParams">Подключение к серверу-источнику (удаление баз из старого кластера).</param>
        /// <param name="sourceClusterId">Идентификатор кластера-источника.</param>
        /// <param name="rows">Отмеченные строки импорта с исходными записями rac.</param>
        /// <param name="availableServers">Справочник «Серверы 1С» (сервер-источник будет исключён).</param>
        /// <param name="sourceServer">Запись сервера-источника для исключения из списка назначения.</param>
        public ClusterMigrationWindow(
            IRacClient rac,
            RacConnectionParams sourceParams,
            Guid sourceClusterId,
            IReadOnlyList<ClusterImportRow> rows,
            IReadOnlyList<ManagementServer> availableServers,
            ManagementServer? sourceServer)
        {
            InitializeComponent();

            _vm = new ClusterMigrationViewModel(
                rac,
                sourceParams,
                sourceClusterId,
                rows,
                availableServers,
                sourceServer,
                action => Application.Current?.Dispatcher.BeginInvoke(action));

            DataContext = _vm;
            Title = LocalizationManager.T("ClusterMigration.Title");
            BaseCountText.Text = string.Format(
                LocalizationManager.T("ClusterMigration.BaseCountFormat"), _vm.BaseCount);

            // Остальные элементы (ServerCombo/ClusterCombo/KeepInSourceCheck/StatusText/
            // ErrorText) привязаны в XAML — код их не трогает (DataContext уже установлен).
            _vm.PropertyChanged += OnVmPropertyChanged;
            Closed += (_, _) => _vm.PropertyChanged -= OnVmPropertyChanged;
            UpdateCommandStates();
        }

        /// <summary>
        /// Результат миграции (при <c>ShowDialog()==true</c>): перенесённые базы, новый
        /// сервер/порт и ответ на вопрос о замене строки подключения.
        /// </summary>
        public ClusterMigrationResult? Result => _vm.Result;

        private void OnDbPasswordChanged(object sender, RoutedEventArgs e) =>
            _vm.DbPassword = DbPasswordBox.Password;

        private void OnMigrate_Click(object sender, RoutedEventArgs e)
        {
            // Наличие баз с теми же именами в приёмнике (issue #41, 1.0.0.52):
            // предупреждаем и спрашиваем подтверждение — повторный перенос после
            // сбоя легален, но пользователь должен знать о конфликте заранее.
            if (_vm.HasConflicts)
            {
                var answer = MessageBox.Show(
                    string.Format(
                        LocalizationManager.T("ClusterMigration.ConfirmConflictsFormat"),
                        _vm.ConflictingBaseNames.Count,
                        string.Join(", ", _vm.ConflictingBaseNames)),
                    LocalizationManager.T("ClusterMigration.ConfirmConflictsTitle"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes)
                    return;
            }

            _vm.MigrateCommand.Execute(null);
        }

        private void OnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ClusterMigrationViewModel.IsBusy):
                case nameof(ClusterMigrationViewModel.HasAvailableServers):
                case nameof(ClusterMigrationViewModel.SelectedServer):
                case nameof(ClusterMigrationViewModel.ProgressText):
                    UpdateCommandStates();
                    break;
                case nameof(ClusterMigrationViewModel.MigrationCompleted):
                    OnMigrationCompleted();
                    break;
            }
        }

        /// <summary>
        /// Единый пересчёт кнопок и индикатора занятости: «Перенести» доступна при выбранных
        /// сервере/кластере назначения и хотя бы одной базе, пока не выполняется запрос.
        /// </summary>
        private void UpdateCommandStates()
        {
            var busy = _vm.IsBusy;
            MigrateButton.IsEnabled = !busy
                && _vm.HasAvailableServers
                && _vm.SelectedServer is not null
                && _vm.SelectedClusterId is not null
                && _vm.BaseCount > 0;
            // Сервер можно сменить, только когда запрос не идёт (кластеры грузятся при выборе).
            ServerCombo.IsEnabled = !busy;
            ClusterCombo.IsEnabled = !busy;
            BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Перенос завершён без ошибок (issue #41): вопрос «Хотите заменить строку
        /// подключения?» → если ДА — результат помечается (MainViewModel заменит сервер/порт
        /// в списке приложения после закрытия окна импорта) → окно закрывается, окно импорта
        /// обновит список баз кластера.
        /// </summary>
        private void OnMigrationCompleted()
        {
            if (_vm.Result is null)
                return;

            var replace = MessageBox.Show(
                LocalizationManager.T("ClusterMigration.ConfirmReplaceConnection"),
                LocalizationManager.T("ClusterMigration.ReplaceConnectionTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes;
            if (replace)
                _vm.SetReplaceConnectionStrings(true);

            DialogResult = true;
            Close();
        }
    }
}
