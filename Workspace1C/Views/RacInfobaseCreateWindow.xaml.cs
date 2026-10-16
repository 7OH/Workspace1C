using System;
using System.Windows;
using System.Windows.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Диалог создания информационной базы в кластере (issue #3, вкладка «Базы»
    /// монитора серверов 1С): поля диалога → <see cref="RacInfobaseCreateRequest"/> →
    /// команда rac «infobase create». Файловая база — тип СУБД «FILE» (сервер СУБД =
    /// путь к каталогу, имя БД = имя файла, обычно «1Cv8.1CD»).
    /// </summary>
    public partial class RacInfobaseCreateWindow : Window
    {
        /// <summary>Результат диалога (null при отмене).</summary>
        public RacInfobaseCreateRequest? Result { get; private set; }

        public RacInfobaseCreateWindow()
        {
            InitializeComponent();
            Title = LocalizationManager.T("ServerMonitor.Infobase.CreateTitle");
            EditorTitle.Text = Title;

            // Типы СУБД для rac «infobase create»: FILE — файловая база, остальные — серверные.
            DbmsBox.ItemsSource = new[]
            {
                RacDbms.MssqlServer,
                RacDbms.PostgreSql,
                RacDbms.IbmDb2,
                RacDbms.OracleDatabase,
                RacDbms.Sqlite,
                RacDbms.ServerOdbc,
                RacDbms.File
            };
            DbmsBox.SelectedItem = RacDbms.MssqlServer;
            SecurityLevelBox.SelectedIndex = -1; // «по умолчанию»
        }

        private void OnDbmsChanged(object sender, SelectionChangedEventArgs e)
        {
            // Для файловой базы показываем подсказку про каталог/файл.
            var isFile = string.Equals(DbmsBox.SelectedItem as string, RacDbms.File, StringComparison.OrdinalIgnoreCase);
            FileHint.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                ShowValidation(LocalizationManager.T("ServerMonitor.Infobase.NameRequired"));
                NameBox.Focus();
                return;
            }

            var dbms = DbmsBox.SelectedItem as string ?? RacDbms.MssqlServer;
            var isFile = string.Equals(dbms, RacDbms.File, StringComparison.OrdinalIgnoreCase);
            var dbServer = DbServerBox.Text?.Trim() ?? string.Empty;
            var dbName = DbNameBox.Text?.Trim() ?? string.Empty;
            if (dbServer.Length == 0)
            {
                ShowValidation(LocalizationManager.T("ServerMonitor.Infobase.DbServerRequired"));
                DbServerBox.Focus();
                return;
            }
            if (dbName.Length == 0)
            {
                ShowValidation(LocalizationManager.T("ServerMonitor.Infobase.DbNameRequired"));
                DbNameBox.Focus();
                return;
            }

            Result = new RacInfobaseCreateRequest
            {
                Name = name,
                Description = DescriptionBox.Text?.Trim() ?? string.Empty,
                IsFile = isFile,
                Dbms = dbms,
                DbServer = dbServer,
                DbName = dbName,
                DbUser = DbUserBox.Text?.Trim() ?? string.Empty,
                DbPassword = DbPasswordBox.Password ?? string.Empty,
                Locale = LocaleBox.Text?.Trim() ?? string.Empty,
                SecurityLevel = SecurityLevelBox.SelectedIndex >= 0
                    ? (int?)SecurityLevelBox.SelectedItem
                    : null,
                CreateDb = CreateDbBox.IsChecked == true && !isFile,
                ScheduledJobsDeny = ScheduledJobsDenyBox.IsChecked == true
            };
            DialogResult = true;
        }

        private void ShowValidation(string message)
        {
            MessageBox.Show(message, LocalizationManager.T("ServerMonitor.Infobase.CreateTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}