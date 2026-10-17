using System;
using System.Windows;
using System.Windows.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Диалог создания/изменения информационной базы в кластере (issue #3): поля
    /// диалога → <see cref="RacInfobaseCreateRequest"/> (команда rac «infobase create»)
    /// либо <see cref="RacInfobaseUpdateRequest"/> (rac «infobase update», кнопка
    /// «Изменить»). В режиме правки конструктор принимает <see cref="RacInfobaseSummary"/>
    /// и предзаполняет поля; галочка «создать базу на сервере СУБД» скрывается
    /// (при изменении rac её не поддерживает). Файловая база — тип СУБД «FILE»
    /// (сервер СУБД = путь к каталогу, имя БД = имя файла, обычно «1Cv8.1CD»).
    /// </summary>
    public partial class RacInfobaseCreateWindow : Window
    {
        /// <summary>Результат при СОЗДАНИИ базы (null при отмене).</summary>
        public RacInfobaseCreateRequest? Result { get; private set; }

        /// <summary>Результат при ИЗМЕНЕНИИ базы (null при отмене/создании).</summary>
        public RacInfobaseUpdateRequest? UpdateResult { get; private set; }

        private readonly bool _isEditMode;

        public RacInfobaseCreateWindow()
        {
            InitializeComponent();
            Title = LocalizationManager.T("ServerMonitor.Infobase.CreateTitle");
            EditorTitle.Text = Title;

            // Типы СУБД для rac «infobase create/update»: FILE — файловая база, остальные — серверные.
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

        /// <summary>
        /// Режим ИЗМЕНЕНИЯ существующей базы: поля предзаполняются из вывода
        /// «infobase summary list»; пароль СУБД rac не возвращает — остаётся пустым
        /// (переданный пароль обновит существующий, пустой — не трогает).
        /// </summary>
        public RacInfobaseCreateWindow(RacInfobaseSummary summary)
            : this()
        {
            _isEditMode = true;
            Title = LocalizationManager.T("ServerMonitor.Infobase.UpdateTitle");
            EditorTitle.Text = Title;

            if (summary is null)
                return;

            NameBox.Text = summary.Name;
            DescriptionBox.Text = summary.Descr;
            DbServerBox.Text = summary.DbServer;
            DbNameBox.Text = summary.DbName;
            DbUserBox.Text = summary.DbUser;
            LocaleBox.Text = summary.Locale;

            var dbms = NormalizeDbms(summary.Dbms);
            DbmsBox.SelectedItem = dbms;
            UpdateFileHintVisibility();
            if (dbms != RacDbms.File)
            {
                SecurityLevelBox.SelectedIndex = summary.SecurityLevel is >= 0 and <= 2
                    ? summary.SecurityLevel
                    : -1;
            }

            // «Создать базу на сервере СУБД» применимо только при создании.
            CreateDbBox.Visibility = Visibility.Collapsed;
        }

        /// <summary>Приводит тип СУБД из вывода rac к константе диалога (регистронезависимо).</summary>
        internal static string NormalizeDbms(string? summaryDbms)
        {
            var value = (summaryDbms ?? string.Empty).Trim().ToLowerInvariant();
            return value switch
            {
                "file" => RacDbms.File,
                "mssqlserver" => RacDbms.MssqlServer,
                "postgresql" => RacDbms.PostgreSql,
                "ibmdb2" => RacDbms.IbmDb2,
                "oracledatabase" => RacDbms.OracleDatabase,
                "sqlite" => RacDbms.Sqlite,
                "serverodbc" => RacDbms.ServerOdbc,
                _ => RacDbms.MssqlServer
            };
        }

        private void OnDbmsChanged(object sender, SelectionChangedEventArgs e)
        {
            // Для файловой базы показываем подсказку про каталог/файл.
            UpdateFileHintVisibility();
        }

        /// <summary>Показывает подсказку про каталог/файл для файловой базы.</summary>
        private void UpdateFileHintVisibility()
        {
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

            if (_isEditMode)
            {
                UpdateResult = new RacInfobaseUpdateRequest
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
                    ScheduledJobsDeny = ScheduledJobsDenyBox.IsChecked == true
                };
            }
            else
            {
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
            }
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