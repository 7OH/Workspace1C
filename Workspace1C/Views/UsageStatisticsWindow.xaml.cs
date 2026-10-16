using System.Collections.Generic;
using System.Windows;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Статистика использования» (0.3.9.95, Windows/WPF): аналитика по истории
    /// запусков всех баз — таблица (число запусков, последний/первый запуск, дней
    /// с последнего запуска), фильтр-переключатель «Только используемые», сводка
    /// и распределение запусков по дням недели. Вся логика — в чистой ViewModel
    /// <see cref="UsageStatisticsViewModel"/>.
    /// </summary>
    public partial class UsageStatisticsWindow : Window
    {
        private readonly UsageStatisticsViewModel _vm;

        /// <param name="infobases">Все базы списка (с их историей запусков).</param>
        public UsageStatisticsWindow(IEnumerable<Infobase> infobases)
        {
            InitializeComponent();

            _vm = new UsageStatisticsViewModel(infobases);
            DataContext = _vm;
            Title = LocalizationManager.T("Stats.Title");
            BasesGrid.ItemsSource = _vm.Rows;
        }

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}