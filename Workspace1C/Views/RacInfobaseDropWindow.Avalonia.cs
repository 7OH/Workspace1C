#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Диалог удаления информационной базы кластера (issue #3, комментарий 7OH от
    /// 2026-10-17) — Avalonia/Linux-версия WPF-окна <see cref="RacInfobaseDropWindow"/>:
    /// выбор «Просто удалить из кластера» или «Удалить с удалением на сервере СУБД».
    /// В кластере бывают только клиент-серверные базы (7OH 18.10 20:24), поэтому
    /// вариант «с удалением на сервере СУБД» доступен всегда; для неизвестного типа
    /// СУБД он просто скрыт. Команда rac: «infobase drop [--drop-database]».
    /// </summary>
    public sealed class RacInfobaseDropWindow : ModalWindowBase
    {
        /// <summary>Удалять ли заодно базу данных на сервере СУБД (флаг rac «--drop-database»).</summary>
        public bool DropDatabase { get; private set; }

        private RadioButton? _withDatabaseRadio;

        public RacInfobaseDropWindow(string infobaseName, bool canDropDatabase)
        {
            Title = LocalizationManager.T("ServerMonitor.Infobase.Drop.Title");
            Width = 560;
            Height = 300;
            MinWidth = 520;
            MinHeight = 260;
            FontSize = 13;
            CanResize = false;
            Content = BuildRoot(infobaseName, canDropDatabase);
        }

        /// <summary>Показывает окно модально (синхронно).</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private Control BuildRoot(string infobaseName, bool canDropDatabase)
        {
            var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };

            // Кнопки внизу.
            var drop = new Button { Content = LocalizationManager.T("ServerMonitor.Infobase.Drop.Confirm"), Width = 140, Height = 36, IsDefault = true };
            drop.Styled(ControlThemes.DialogConfirmButton);
            drop.Click += (_, _) => OnDrop();
            var cancel = new Button { Content = LocalizationManager.T("Updates.Cancel"), Width = 120, Height = 36 };
            cancel.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(cancel, Button.BackgroundProperty, "ItemHoverBrush");
            cancel.Click += (_, _) =>
            {
                DialogResult = false;
                Close();
            };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 12, 0, 0),
                Children = { drop, cancel }
            };
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            // Вопрос.
            var question = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
                Text = string.Format(
                    LocalizationManager.T("ServerMonitor.Infobase.Drop.QuestionFormat"),
                    string.IsNullOrWhiteSpace(infobaseName) ? "—" : infobaseName)
            };

            // Варианты удаления.
            RadioButton Radio(string textKey, string group, bool isChecked = false)
            {
                var radio = new RadioButton
                {
                    Content = LocalizationManager.T(textKey),
                    GroupName = group,
                    IsChecked = isChecked,
                    Margin = new Thickness(0, 2, 0, 8)
                };
                return radio;
            }

            var fromCluster = Radio("ServerMonitor.Infobase.Drop.OnlyFromCluster", "DropOption", isChecked: true);
            _withDatabaseRadio = Radio("ServerMonitor.Infobase.Drop.WithDatabase", "DropOption");
            var withDatabase = _withDatabaseRadio;
            // Вариант «с удалением на сервере СУБД» показываем только клиент-серверным
            // базам; файловых баз в кластере не бывает — подсказок про них нет.
            withDatabase.IsVisible = canDropDatabase;

            var options = new StackPanel
            {
                Spacing = 2,
                Children = { fromCluster, withDatabase }
            };
            var group = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10),
                Margin = new Thickness(0, 8, 0, 0),
                Child = options
            };
            ThemeBrushes.Bind(group, Border.BorderBrushProperty, "BorderColorBrush");

            var form = new StackPanel { Children = { question, group } };
            root.Children.Add(form);
            return root;
        }

        private void OnDrop()
        {
            DropDatabase = _withDatabaseRadio?.IsChecked == true;
            DialogResult = true;
            Close();
        }
    }
}
#endif