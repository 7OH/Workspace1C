using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Настроить» (issue #11, Windows/WPF): модальный чеклист ВСЕХ служб системы
    /// с галочками (уже выбранные отмечены), поиск-фильтр, «Выбрать все/Сбросить».
    /// По OK <see cref="SelectedServices"/> возвращает отмеченные службы (порядок
    /// списка); по отмене — null. Вся логика — в чистой <see cref="ServicesPickerViewModel"/>.
    /// </summary>
    public partial class ServicesPickerWindow : Window
    {
        private readonly ServicesPickerViewModel _vm;

        public ServicesPickerWindow()
        {
            InitializeComponent();

            var services = AppServices.GetRequiredService<IServiceControllerService>();
            var store = AppServices.GetRequiredService<IServicesStore>();

            _vm = new ServicesPickerViewModel(
                services,
                store,
                action => Application.Current?.Dispatcher.BeginInvoke(action));

            DataContext = _vm;
            Title = LocalizationManager.T("Services.Configure");

            OkButton.Click += (_, _) =>
            {
                _vm.OkCommand.Execute(null);
                DialogResult = true;
            };
            CancelButton.Click += (_, _) => DialogResult = false;
            PreviewKeyDown += OnWindowPreviewKeyDown;
        }

        /// <summary>Результат: отмеченные службы (если подтвердили OK), иначе null.</summary>
        public IReadOnlyList<string>? SelectedServices => _vm.OkPressed ? _vm.SelectedNames : null;

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                DialogResult = false;
                e.Handled = true;
            }
        }
    }
}