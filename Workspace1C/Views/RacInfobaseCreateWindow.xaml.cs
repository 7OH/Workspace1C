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
    /// (при изменении rac её не поддерживает). Типы СУБД — только клиент-серверные
    /// (комментарий 7OH от 2026-10-18 20:24): файловых баз в кластере не бывает.
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

            // Кнопка «развернуть» в собственной шапке не нужна (issue #3, 7OH 19.10):
            // ресайз (ResizeMode по умолчанию CanResize) сохраняется, максимизация
            // подавляется — как у RacInfobasePropertiesWindow/ServerEditWindow.
            WindowChromeHelper.SetSuppressMaximizeButton(this, true);

            Title = LocalizationManager.T("ServerMonitor.Infobase.CreateTitle");
            EditorTitle.Text = Title;

            // Типы СУБД для rac «infobase create/update» (комментарий 7OH от 2026-10-18 20:24):
            // в кластере бывают только клиент-серверные базы — ровно значения rac «--dbms».
            DbmsBox.ItemsSource = new[]
            {
                RacDbms.MssqlServer,
                RacDbms.PostgreSql,
                RacDbms.IbmDb2,
                RacDbms.OracleDatabase
            };
            DbmsBox.SelectedItem = RacDbms.MssqlServer;
            SecurityLevelBox.SelectedIndex = -1; // «по умолчанию»

            // Локаль — список выбора (issue #3, 7OH 19.10): пустое значение rac не
            // принимает, поэтому по умолчанию выбран Русский.
            LocaleBox.ItemsSource = RacLocales.Values;
            LocaleBox.SelectedItem = RacLocales.Default;
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
            LocaleBox.SelectedItem = SelectLocale(summary.Locale);

            var dbms = NormalizeDbms(summary.Dbms);
            DbmsBox.SelectedItem = dbms;
            SecurityLevelBox.SelectedIndex = summary.SecurityLevel is >= 0 and <= 2
                ? summary.SecurityLevel
                : -1;

            // «Создать базу на сервере СУБД» применимо только при создании.
            CreateDbBox.Visibility = Visibility.Collapsed;
        }

        /// <summary>Приводит тип СУБД из вывода rac к константе диалога (регистронезависимо).</summary>
        internal static string NormalizeDbms(string? summaryDbms)
        {
            var value = (summaryDbms ?? string.Empty).Trim().ToLowerInvariant();
            return value switch
            {
                "mssqlserver" => RacDbms.MssqlServer,
                "postgresql" => RacDbms.PostgreSql,
                "ibmdb2" => RacDbms.IbmDb2,
                "oracledatabase" => RacDbms.OracleDatabase,
                _ => RacDbms.MssqlServer
            };
        }

        /// <summary>
        /// Выбирает локаль из списка: нормализует код из rac («ru_RU» → «ru») и, если
        /// его нет в списке, добавляет в ItemsSource, чтобы не потерять значение.
        /// </summary>
        private string SelectLocale(string? summaryLocale)
        {
            var normalized = RacLocales.Normalize(summaryLocale);
            if (LocaleBox.Items.Contains(normalized))
                return normalized;
            LocaleBox.Items.Add(normalized);
            return normalized;
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
                    Dbms = dbms,
                    DbServer = dbServer,
                    DbName = dbName,
                    DbUser = DbUserBox.Text?.Trim() ?? string.Empty,
                    DbPassword = DbPasswordBox.Password ?? string.Empty,
                    Locale = LocaleBox.SelectedItem as string ?? RacLocales.Default,
                    // Элементы списка — ComboBoxItem «0/1/2»: значение = индекс (issue #3:
                    // приведение SelectedItem к int? бросало InvalidCastException).
                    SecurityLevel = SecurityLevelBox.SelectedIndex >= 0
                        ? SecurityLevelBox.SelectedIndex
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
                    Dbms = dbms,
                    DbServer = dbServer,
                    DbName = dbName,
                    DbUser = DbUserBox.Text?.Trim() ?? string.Empty,
                    DbPassword = DbPasswordBox.Password ?? string.Empty,
                    Locale = LocaleBox.SelectedItem as string ?? RacLocales.Default,
                    // Элементы списка — ComboBoxItem «0/1/2»: значение = индекс (issue #3:
                    // приведение SelectedItem к int? бросало InvalidCastException).
                    SecurityLevel = SecurityLevelBox.SelectedIndex >= 0
                        ? SecurityLevelBox.SelectedIndex
                        : null,
                    CreateDb = CreateDbBox.IsChecked == true,
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