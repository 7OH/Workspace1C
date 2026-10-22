#if LINUX
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
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
    /// Окно «Миграция баз» (1.0.0.51, issue #41, Avalonia/Linux): перенос информационных
    /// баз из кластера-источника (окно «Импорт из кластера 1С») в выбранный кластер
    /// другого сервера 1С. Тонкая обёртка над <see cref="ClusterMigrationViewModel"/>
    /// (аналогично <see cref="ClusterImportWindow"/>): выбор сервера/кластера назначения
    /// (сервер-источник скрыт), учётные данные СУБД, вопрос «Хотите заменить строку
    /// подключения?» после успешного переноса и закрытие с результатом.
    /// После <c>ShowDialogSync()==true</c> результат доступен через <see cref="Result"/>.
    /// </summary>
    public partial class ClusterMigrationWindow : ModalWindowBase
    {
        private readonly ClusterMigrationViewModel _vm;
        private readonly ComboBox _serverCombo;
        private readonly ComboBox _clusterCombo;
        private readonly TextBlock _progressText;
        private readonly ProgressBar _progressBar;
        private readonly Button _migrateButton;
        private readonly TextBox _dbPasswordBox;

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
            AvaloniaXamlLoader.Load(this);

            _vm = new ClusterMigrationViewModel(
                rac,
                sourceParams,
                sourceClusterId,
                rows,
                availableServers,
                sourceServer,
                action => Dispatcher.UIThread.Post(action));

            Title = LocalizationManager.T("ClusterMigration.Title");
            DataContext = _vm;
            _vm.PropertyChanged += OnVmPropertyChanged;
            Closed += (_, _) => _vm.PropertyChanged -= OnVmPropertyChanged;

            this.FindControl<TextBlock>("BaseCountText")!.Text = string.Format(
                LocalizationManager.T("ClusterMigration.BaseCountFormat"), _vm.BaseCount);

            // ---- Выбор сервера назначения: список БЕЗ сервера-источника. ----
            _serverCombo = this.FindControl<ComboBox>("ServerCombo")!;
            _serverCombo.ItemsSource = _vm.AvailableServers;
            _serverCombo.ItemTemplate = new FuncDataTemplate<ManagementServer>((server, _) =>
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = server.Description, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = $"{server.Address}:{server.Port}", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center }
                    }
                });
            _serverCombo.SelectionChanged += (_, _) =>
            {
                if (_serverCombo.SelectedItem is ManagementServer server)
                    _vm.SelectedServer = server;
            };
            this.FindControl<TextBlock>("ServersEmptyHint")!
                .Bind(IsVisibleProperty, new Binding("!HasAvailableServers"));

            // ---- Выбор кластера назначения: список из VM, синхронизация в обе стороны. ----
            _clusterCombo = this.FindControl<ComboBox>("ClusterCombo")!;
            _clusterCombo.Bind(ItemsControl.ItemsSourceProperty, new Binding("Clusters"));
            _clusterCombo.ItemTemplate = new FuncDataTemplate<RacCluster>((cluster, _) =>
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = cluster.Name, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = $"[{cluster.Port}]", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center }
                    }
                });
            _clusterCombo.SelectionChanged += (_, _) =>
            {
                if (_clusterCombo.SelectedItem is RacCluster cluster)
                    _vm.SelectedClusterId = cluster.Id;
            };

            // ---- Статусные строки и прогресс. ----
            this.FindControl<TextBlock>("StatusText")!
                .Bind(TextBlock.TextProperty, new Binding("StatusText"));
            this.FindControl<TextBlock>("ErrorText")!
                .Bind(TextBlock.TextProperty, new Binding("ErrorMessage"));
            _progressText = this.FindControl<TextBlock>("ProgressText")!;
            _progressBar = this.FindControl<ProgressBar>("BusyProgress")!;

            // ---- Учётные данные СУБД и «Не удалять в старом». ----
            var dbUserBox = this.FindControl<TextBox>("DbUserBox")!;
            dbUserBox.Bind(TextBox.TextProperty, new Binding("DbUser", BindingMode.TwoWay));
            _dbPasswordBox = this.FindControl<TextBox>("DbPasswordBox")!;
            _dbPasswordBox.TextChanged += (_, _) => _vm.DbPassword = _dbPasswordBox.Text ?? string.Empty;
            var keepCheck = this.FindControl<CheckBox>("KeepInSourceCheck")!;
            keepCheck.Bind(CheckBox.IsCheckedProperty, new Binding("KeepInSource", BindingMode.TwoWay));

            // ---- Кнопки «Перенести» (зелёная, как «Импортировать») и «Отмена». ----
            _migrateButton = BuildPrimaryButton(LocalizationManager.T("ClusterMigration.MigrateButton"), "➜",
                () => _vm.MigrateCommand.Execute(null));
            this.FindControl<ContentControl>("MigrateHost")!.Content = _migrateButton;

            var cancelButton = BuildSecondaryButton(LocalizationManager.T("Common.Cancel"),
                () => { DialogResult = false; Close(); });
            this.FindControl<ContentControl>("CancelHost")!.Content = cancelButton;

            UpdateCommandStates();
        }

        /// <summary>
        /// Результат миграции (при <c>ShowDialogSync()==true</c>): перенесённые базы, новый
        /// сервер/порт и ответ на вопрос о замене строки подключения.
        /// </summary>
        public ClusterMigrationResult? Result => _vm.Result;

        /// <summary>Показывает окно модально (синхронно) — обёртка над <see cref="ModalWindowBase.ShowDialogSync(Window?)"/>.</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

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
                case nameof(ClusterMigrationViewModel.SelectedClusterId):
                    SyncSelectedCluster();
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
            _migrateButton.IsEnabled = !busy
                && _vm.HasAvailableServers
                && _vm.SelectedServer is not null
                && _vm.SelectedClusterId is not null
                && _vm.BaseCount > 0;
            // Сервер можно сменить, только когда запрос не идёт (кластеры грузятся при выборе).
            _serverCombo.IsEnabled = !busy;
            _clusterCombo.IsEnabled = !busy;
            _progressBar.IsVisible = busy;
            _progressText.Text = _vm.ProgressText;
        }

        /// <summary>Автовыбор единственного кластера VM синхронизируется с комбобоксом
        /// (как в <see cref="ClusterImportWindow"/>): исключение обратных срабатываний.</summary>
        private void SyncSelectedCluster()
        {
            if (_vm.SelectedClusterId is not Guid id)
                return;
            if (_clusterCombo.SelectedItem is RacCluster current && current.Id == id)
                return;
            _clusterCombo.SelectedItem = _vm.Clusters.FirstOrDefault(c => c.Id == id);
        }

        /// <summary>
        /// Перенос завершён без ошибок (issue #41): вопрос «Хотите заменить строку
        /// подключения?» → если ДА — результат помечается (MainViewModel заменит сервер/порт
        /// в списке приложения после закрытия окна импорта) → окно закрывается.
        /// </summary>
        private void OnMigrationCompleted()
        {
            if (_vm.Result is null)
                return;

            if (AppServices.GetRequiredService<IDialogService>().Confirm(
                    LocalizationManager.T("ClusterMigration.ConfirmReplaceConnection"),
                    LocalizationManager.T("ClusterMigration.ReplaceConnectionTitle")))
            {
                _vm.SetReplaceConnectionStrings(true);
            }

            DialogResult = true;
            Close();
        }

        private Button BuildPrimaryButton(string text, string icon, Action onClick)
        {
            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = icon, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White },
                        new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White }
                    }
                },
                Padding = new Thickness(16, 5)
            };
            button.Styled(ControlThemes.ModernButton);
            button.Background = new SolidColorBrush(Color.Parse("#16A34A"));
            button.Foreground = Brushes.White;
            button.Click += (_, _) => onClick();
            return button;
        }

        private Button BuildSecondaryButton(string text, Action onClick)
        {
            var button = new Button { Content = text, Padding = new Thickness(14, 6) };
            button.Styled(ControlThemes.SecondaryButton);
            button.Click += (_, _) => onClick();
            return button;
        }
    }
}
#endif
