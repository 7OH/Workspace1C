#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Utilities;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Диалог создания информационной базы в кластере (issue #3, вкладка «Базы»
    /// монитора серверов 1С): поля диалога → <see cref="RacInfobaseCreateRequest"/> →
    /// команда rac «infobase create». Avalonia/Linux-версия WPF-окна
    /// <see cref="RacInfobaseCreateWindow"/>. Файловая база — тип СУБД «FILE».
    /// </summary>
    public sealed class RacInfobaseCreateWindow : ModalWindowBase
    {
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

        private readonly TextBox _nameBox = new() { Height = 32, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly TextBox _descriptionBox = new() { Height = 32, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly ComboBox _dbmsBox = new() { Height = 32, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly TextBox _dbServerBox = new() { Height = 32, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly TextBox _dbNameBox = new() { Height = 32, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly TextBox _dbUserBox = new() { Height = 32, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly PasswordBox _dbPasswordBox = new PasswordBox { Height = 32 }.Styled(ControlThemes.ModernPasswordBox);
        private readonly TextBox _localeBox = new() { Height = 32, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly ComboBox _securityLevelBox = new() { Height = 32, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly CheckBox _createDbBox = new() { Content = T("ServerMonitor.Infobase.CreateDb") };
        private readonly CheckBox _scheduledJobsDenyBox = new() { Content = T("ServerMonitor.Infobase.ScheduledJobsDeny") };
        private readonly TextBlock _fileHint;
        private bool _isEditMode;

        /// <summary>Готовый запрос создания базы при подтверждении, иначе <c>null</c>.</summary>
        public RacInfobaseCreateRequest? Result { get; private set; }

        /// <summary>Готовый запрос ИЗМЕНЕНИЯ базы (кнопка «Изменить»), иначе <c>null</c>.</summary>
        public RacInfobaseUpdateRequest? UpdateResult { get; private set; }

        public RacInfobaseCreateWindow()
        {
            Title = T("ServerMonitor.Infobase.CreateTitle");
            Width = 560;
            Height = 620;
            MinWidth = 520;
            MinHeight = 560;
            FontSize = 13;
            CanResize = true;

            _dbmsBox.Styled(ControlThemes.ModernComboBox);
            _securityLevelBox.Styled(ControlThemes.ModernComboBox);
            _dbmsBox.ItemsSource = new[]
            {
                RacDbms.MssqlServer,
                RacDbms.PostgreSql,
                RacDbms.IbmDb2,
                RacDbms.OracleDatabase,
                RacDbms.Sqlite,
                RacDbms.ServerOdbc,
                RacDbms.File
            };
            _dbmsBox.SelectedItem = RacDbms.MssqlServer;
            _securityLevelBox.ItemsSource = new[] { "0", "1", "2" };

            _fileHint = new TextBlock
            {
                Text = T("ServerMonitor.Infobase.FilePathHint"),
                FontSize = 12,
                Margin = new Thickness(150, 0, 0, 0),
                IsVisible = false,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };

            // Для файловой базы — подсказка про каталог/файл.
            _dbmsBox.SelectionChanged += (_, _) =>
                _fileHint.IsVisible = string.Equals(
                    _dbmsBox.SelectedItem as string, RacDbms.File, StringComparison.OrdinalIgnoreCase);

            Content = BuildRoot();
        }

        /// <summary>
        /// Режим ИЗМЕНЕНИЯ существующей базы (issue #3, кнопка «Изменить»): поля
        /// предзаполняются из вывода «infobase summary list»; пароль СУБД rac не
        /// возвращает — остаётся пустым (переданный пароль обновит существующий,
        /// пустой — не трогает). Галочка «создать базу на сервере СУБД» скрывается.
        /// </summary>
        public RacInfobaseCreateWindow(RacInfobaseSummary summary)
            : this()
        {
            // Режим правки: заголовок и скрытие «создать базу на сервере СУБД».
            _isEditMode = true;
            Title = T("ServerMonitor.Infobase.UpdateTitle");
            _createDbBox.IsVisible = false;
            if (summary is null)
                return;

            _nameBox.Text = summary.Name;
            _descriptionBox.Text = summary.Descr;
            _dbServerBox.Text = summary.DbServer;
            _dbNameBox.Text = summary.DbName;
            _dbUserBox.Text = summary.DbUser;
            _localeBox.Text = summary.Locale;

            _dbmsBox.SelectedItem = NormalizeDbms(summary.Dbms);
            _fileHint.IsVisible = string.Equals(
                _dbmsBox.SelectedItem as string, RacDbms.File, StringComparison.OrdinalIgnoreCase);
            if (summary.SecurityLevel is >= 0 and <= 2)
                _securityLevelBox.SelectedItem = summary.SecurityLevel.ToString();
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

        /// <summary>Показывает окно модально (синхронно).</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private static string T(string key) => LocalizationManager.T(key);

        private Control BuildRoot()
        {
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };

            panel.Children.Add(new TextBlock
            {
                Text = Title,
                FontSize = 15,
                FontWeight = Avalonia.Media.FontWeight.SemiBold
            });

            panel.Children.Add(MakeFieldRow(T("ServerMonitor.Infobase.Name"), _nameBox));
            panel.Children.Add(MakeFieldRow(T("ServerMonitor.Infobase.Description"), _descriptionBox));
            panel.Children.Add(MakeFieldRow(T("ServerMonitor.Infobase.Dbms"), _dbmsBox));
            panel.Children.Add(MakeFieldRow(T("ServerMonitor.Infobase.DbServer"), _dbServerBox));
            panel.Children.Add(MakeFieldRow(T("ServerMonitor.Infobase.DbName"), _dbNameBox));
            panel.Children.Add(MakeFieldRow(T("ServerMonitor.Infobase.DbUser"), _dbUserBox));
            panel.Children.Add(MakeFieldRow(T("ServerMonitor.Infobase.DbPassword"), _dbPasswordBox));
            panel.Children.Add(MakeFieldRow(T("ServerMonitor.Infobase.Locale"), _localeBox));
            panel.Children.Add(MakeFieldRow(T("ServerMonitor.Infobase.SecurityLevel"), _securityLevelBox));
            panel.Children.Add(_fileHint);
            panel.Children.Add(_createDbBox);
            panel.Children.Add(_scheduledJobsDenyBox);

            var save = new Button { Content = T("Updates.Save"), Width = 130, Height = 36, IsDefault = true };
            save.Styled(ControlThemes.DialogConfirmButton);
            save.Click += (_, _) => OnSaveClick();
            var cancel = BuildCancelActionButton(120);
            cancel.Click += (_, _) => OnCancelClick();

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 12, 0, 0),
                Children = { save, cancel }
            };
            panel.Children.Add(buttons);

            return panel;
        }

        private static Control MakeFieldRow(string label, Control editor)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150, GridUnitType.Pixel)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.Margin = new Thickness(0, 4, 0, 4);

            var caption = new TextBlock
            {
                Text = label,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(caption, 0);
            grid.Children.Add(caption);

            Grid.SetColumn(editor, 1);
            grid.Children.Add(editor);
            return grid;
        }

        private void OnSaveClick()
        {
            var name = _nameBox.Text?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                _dialogs.ShowWarning(T("ServerMonitor.Infobase.NameRequired"), Title);
                _nameBox.Focus();
                return;
            }

            var dbms = _dbmsBox.SelectedItem as string ?? RacDbms.MssqlServer;
            var isFile = string.Equals(dbms, RacDbms.File, StringComparison.OrdinalIgnoreCase);
            var dbServer = _dbServerBox.Text?.Trim() ?? string.Empty;
            var dbName = _dbNameBox.Text?.Trim() ?? string.Empty;
            if (dbServer.Length == 0)
            {
                _dialogs.ShowWarning(T("ServerMonitor.Infobase.DbServerRequired"), Title);
                _dbServerBox.Focus();
                return;
            }
            if (dbName.Length == 0)
            {
                _dialogs.ShowWarning(T("ServerMonitor.Infobase.DbNameRequired"), Title);
                _dbNameBox.Focus();
                return;
            }

            if (_isEditMode)
            {
                UpdateResult = new RacInfobaseUpdateRequest
                {
                    Name = name,
                    Description = _descriptionBox.Text?.Trim() ?? string.Empty,
                    IsFile = isFile,
                    Dbms = dbms,
                    DbServer = dbServer,
                    DbName = dbName,
                    DbUser = _dbUserBox.Text?.Trim() ?? string.Empty,
                    DbPassword = _dbPasswordBox.Password ?? string.Empty,
                    Locale = _localeBox.Text?.Trim() ?? string.Empty,
                    SecurityLevel = int.TryParse(_securityLevelBox.SelectedItem as string, out var level) ? level : null,
                    ScheduledJobsDeny = _scheduledJobsDenyBox.IsChecked == true
                };
            }
            else
            {
                Result = new RacInfobaseCreateRequest
                {
                    Name = name,
                    Description = _descriptionBox.Text?.Trim() ?? string.Empty,
                    IsFile = isFile,
                    Dbms = dbms,
                    DbServer = dbServer,
                    DbName = dbName,
                    DbUser = _dbUserBox.Text?.Trim() ?? string.Empty,
                    DbPassword = _dbPasswordBox.Password ?? string.Empty,
                    Locale = _localeBox.Text?.Trim() ?? string.Empty,
                    SecurityLevel = int.TryParse(_securityLevelBox.SelectedItem as string, out var level) ? level : null,
                    CreateDb = _createDbBox.IsChecked == true && !isFile,
                    ScheduledJobsDeny = _scheduledJobsDenyBox.IsChecked == true
                };
            }
            DialogResult = true;
            Close();
        }

        private void OnCancelClick()
        {
            DialogResult = false;
            Close();
        }
    }
}
#endif