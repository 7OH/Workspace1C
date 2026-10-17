using System.Windows;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Свойства информационной базы» монитора «Серверы 1С» (issue #3, 7OH 17.10).
    /// Логика — в <see cref="RacInfobasePropertiesViewModel"/>; окно лишь связывает пароли
    /// (PasswordBox не поддерживает привязку) и при открытии сразу читает полные свойства.
    /// </summary>
    public partial class RacInfobasePropertiesWindow : Window
    {
        private readonly RacInfobasePropertiesViewModel _vm;

        /// <summary>Запрос rac «infobase update» при сохранении (null при отмене).</summary>
        public RacInfobaseUpdateRequest? Result { get; private set; }

        public RacInfobasePropertiesWindow(RacInfobasePropertiesViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
            AdminPasswordBox.Password = vm.InfobasePassword;
            Loaded += async (_, _) => await _vm.RefreshAsync();
        }

        private void OnAdminPasswordChanged(object sender, RoutedEventArgs e) =>
            _vm.InfobasePassword = AdminPasswordBox.Password;

        private void OnDbPasswordChanged(object sender, RoutedEventArgs e) =>
            _vm.DbPassword = DbPasswordBox.Password;

        private async void OnRefreshClick(object sender, RoutedEventArgs e) => await _vm.RefreshAsync();

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var request = _vm.BuildRequest(out var error);
            if (request is null)
            {
                MessageBox.Show(this, error ?? string.Empty,
                    LocalizationManager.T("ServerMonitor.Infobase.Props.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Result = request;
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
