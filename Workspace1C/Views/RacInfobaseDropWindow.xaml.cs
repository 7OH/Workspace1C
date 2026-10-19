using System.Windows;
using Workspace1C.Localization;

namespace Workspace1C
{
    /// <summary>
    /// Диалог удаления информационной базы кластера (issue #3, комментарий 7OH от
    /// 2026-10-17): повторяет выбор консоли 1С — «Просто удалить из кластера» или
    /// «Удалить с удалением на сервере СУБД». В кластере бывают только клиент-серверные
    /// базы (комментарий 7OH от 2026-10-18 20:24 и 19.10 19:12), поэтому вариант
    /// «с удалением на сервере СУБД» (rac «infobase drop --drop-database») доступен
    /// всегда — независимо от распознанного типа СУБД (1.0.0.21). Рядом с именем базы
    /// выводится распознанный из summary тип и сервер СУБД (issue #3, часть 1) —
    /// диагностическая подпись.
    /// </summary>
    public partial class RacInfobaseDropWindow : Window
    {
        /// <summary>Удалять ли заодно базу данных на сервере СУБД (флаг rac «--drop-database»).</summary>
        public bool DropDatabase { get; private set; }

        public RacInfobaseDropWindow(
            string infobaseName, bool canDropDatabase, string? dbmsText = null, string? dbServer = null)
        {
            InitializeComponent();

            // Кнопка «развернуть» в собственной шапке не нужна (issue #3, 7OH 19.10):
            // ресайз (ResizeMode по умолчанию CanResize) сохраняется, максимизация
            // подавляется — как у RacInfobaseCreateWindow/ServerEditWindow.
            WindowChromeHelper.SetSuppressMaximizeButton(this, true);

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

            // Вариант «с удалением на сервере СУБД» показываем всем базам списка кластера
            // (в кластере бывают только клиент-серверные базы, 7OH 19.10 19:12); флаг
            // canDropDatabase приходит от VM и для баз кластера всегда true (1.0.0.21).
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