using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно редактирования группы служб (issue #11, итерация 2 — группы служб, релиз
    /// 1.0.0.12; Задача 3, Windows/WPF). Форма по требованию 7OH (комментарий к issue #11
    /// от 13:25): сверху «Сохранить»/«Отмена», ниже «Описание», ниже два списка —
    /// «Управляемый список» и «Все службы» — с кнопками-стрелками между ними и
    /// кнопками «Вверх/Вниз» над управляемым списком. Вся логика — в чистой
    /// <see cref="ServiceGroupEditViewModel"/>; окно модальное (ShowDialog, Owner),
    /// по «Сохранить» возвращает <see cref="Result"/>, по «Отмена» — null.
    /// </summary>
    public partial class ServiceGroupEditWindow : Window
    {
        private readonly ServiceGroupEditViewModel _vm;

        /// <param name="editingGroup">Редактируемая группа; null — создание новой.</param>
        /// <param name="availableServices">
        /// Службы общего списка, допустимые для включения (уже отфильтрованы вызывающей
        /// стороной — см. <see cref="ServicesWindowViewModel.AvailableServicesForGroup"/>).
        /// </param>
        public ServiceGroupEditWindow(ServiceGroup? editingGroup, IReadOnlyList<string> availableServices)
        {
            InitializeComponent();

            _vm = new ServiceGroupEditViewModel(editingGroup, availableServices);
            DataContext = _vm;

            Title = editingGroup is null
                ? LocalizationManager.T("Services.AddGroup")
                : LocalizationManager.T("Services.EditGroup");

            // «Сохранить»/«Отмена» закрывают окно через событие VM (паттерн чистых
            // ViewModel): результат — DialogResult по IsSaved.
            _vm.CloseRequested += OnVmCloseRequested;
            PreviewKeyDown += OnWindowPreviewKeyDown;
        }

        /// <summary>Результат «Сохранить»: заполненная группа (имя + члены) или null.</summary>
        public ServiceGroup? Result => _vm.Result;

        /// <summary>Закрывает модальное окно с результатом: true — сохранено, false — отмена.</summary>
        private void OnVmCloseRequested() => DialogResult = _vm.IsSaved;

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