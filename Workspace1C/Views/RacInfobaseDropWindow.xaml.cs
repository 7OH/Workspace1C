using System.Windows;
using Workspace1C.Localization;

namespace Workspace1C
{
    /// <summary>
    /// Диалог удаления информационной базы кластера (issue #3, комментарий 7OH от
    /// 2026-10-17): повторяет выбор консоли 1С — «Просто удалить из кластера» или
    /// «Удалить с удалением на сервере СУБД». Для файловых баз доступен только первый
    /// вариант (rac «infobase drop» без флага <c>--drop-database</c>).
    /// </summary>
    public partial class RacInfobaseDropWindow : Window
    {
        /// <summary>Удалять ли заодно базу данных на сервере СУБД (флаг rac «--drop-database»).</summary>
        public bool DropDatabase { get; private set; }

        public RacInfobaseDropWindow(string infobaseName, bool canDropDatabase)
        {
            InitializeComponent();
            QuestionText.Text = string.Format(
                LocalizationManager.T("ServerMonitor.Infobase.Drop.QuestionFormat"),
                string.IsNullOrWhiteSpace(infobaseName) ? "—" : infobaseName);

            if (canDropDatabase)
            {
                DropDatabaseRadio.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                // Файловая база: вариант «с удалением на сервере СУБД» недоступен.
                FileBaseHint.Visibility = System.Windows.Visibility.Visible;
            }
        }

        private void OnDrop_Click(object sender, RoutedEventArgs e)
        {
            DropDatabase = DropDatabaseRadio.IsChecked == true;
            DialogResult = true;
            Close();
        }

        private void OnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}