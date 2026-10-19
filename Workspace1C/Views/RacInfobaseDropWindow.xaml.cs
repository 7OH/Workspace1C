using System.Windows;
using Workspace1C.Localization;

namespace Workspace1C
{
    /// <summary>
    /// Диалог удаления информационной базы кластера (issue #3, комментарий 7OH от
    /// 2026-10-17): повторяет выбор консоли 1С — «Просто удалить из кластера» или
    /// «Удалить с удалением на сервере СУБД». В кластере бывают только клиент-серверные
    /// базы (комментарий 7OH от 2026-10-18 20:24), поэтому вариант «с удалением на
    /// сервере СУБД» (rac «infobase drop --drop-database») доступен всегда; для
    /// неизвестного типа СУБД он просто не показывается — без сообщений про файловую базу.
    /// Рядом с именем базы выводится распознанный из summary тип и сервер СУБД (issue #3,
    /// часть 1) — если вариант не появился, сразу видно, что распарсилось.
    /// </summary>
    public partial class RacInfobaseDropWindow : Window
    {
        /// <summary>Удалять ли заодно базу данных на сервере СУБД (флаг rac «--drop-database»).</summary>
        public bool DropDatabase { get; private set; }

        public RacInfobaseDropWindow(
            string infobaseName, bool canDropDatabase, string? dbmsText = null, string? dbServer = null)
        {
            InitializeComponent();
            QuestionText.Text = string.Format(
                LocalizationManager.T("ServerMonitor.Infobase.Drop.QuestionFormat"),
                string.IsNullOrWhiteSpace(infobaseName) ? "—" : infobaseName);

            // Диагностическая подпись «Тип СУБД: …; Сервер СУБД: …» — только при непустом
            // типе СУБД (иначе пустая строка не должна ломать диалог).
            if (!string.IsNullOrWhiteSpace(dbmsText))
            {
                DbmsInfoText.Text = string.Format(
                    LocalizationManager.T("ServerMonitor.Infobase.Drop.DbmsFormat"),
                    dbmsText, string.IsNullOrWhiteSpace(dbServer) ? "—" : dbServer);
                DbmsInfoText.Visibility = System.Windows.Visibility.Visible;
            }

            // Вариант «с удалением на сервере СУБД» показываем только клиент-серверным
            // базам; файловых баз в кластере не бывает, поэтому никаких подсказок про них нет.
            if (canDropDatabase)
                DropDatabaseRadio.Visibility = System.Windows.Visibility.Visible;
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