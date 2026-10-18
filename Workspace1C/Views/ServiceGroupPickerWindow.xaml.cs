using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Workspace1C.Localization;

namespace Workspace1C
{
    /// <summary>
    /// Диалог «Перенести в группу» окна «Управление службами» (issue #11, 1.0.0.14):
    /// выбор места для службы — «Без группы» (корневой список) или одна из групп.
    /// <see cref="SelectedGroupName"/>: null — отмена; «» (пусто) — «Без группы»;
    /// иначе — имя выбранной группы.
    /// </summary>
    public partial class ServiceGroupPickerWindow : Window
    {
        private string? _selectedName;

        /// <summary>Выбранное место: имя группы или «» (пусто) — «Без группы»; null — отмена.</summary>
        public string? SelectedGroupName { get; private set; }

        /// <param name="serviceName">Имя переносимой службы (для заголовка вопроса).</param>
        /// <param name="groupNames">Имена групп для выбора (порядок — как в списке).</param>
        /// <param name="currentGroup">Группа, в которой служба сейчас; null — вне групп.</param>
        public ServiceGroupPickerWindow(string serviceName, IReadOnlyList<string> groupNames, string? currentGroup)
        {
            InitializeComponent();
            Title = LocalizationManager.T("Services.GroupPicker.Title");
            QuestionText.Text = string.Format(
                LocalizationManager.T("Services.GroupPicker.QuestionFormat"),
                string.IsNullOrWhiteSpace(serviceName) ? "—" : serviceName);

            _selectedName = string.IsNullOrEmpty(currentGroup) ? string.Empty : currentGroup;
            NoGroupRadio.IsChecked = string.IsNullOrEmpty(currentGroup);
            NoGroupRadio.Checked += (_, _) => _selectedName = string.Empty;

            foreach (var name in groupNames)
            {
                var radio = new RadioButton
                {
                    GroupName = "Target",
                    Content = name,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0)
                };
                radio.IsChecked = string.Equals(name, currentGroup, System.StringComparison.OrdinalIgnoreCase);
                radio.Checked += (_, _) => _selectedName = name;
                GroupHost.Children.Add(radio);
            }
        }

        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            SelectedGroupName = _selectedName ?? string.Empty;
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