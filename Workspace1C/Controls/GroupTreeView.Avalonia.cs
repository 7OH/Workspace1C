#if LINUX
using Avalonia.Controls;

namespace Workspace1C.Controls
{
    /// <summary>
    /// Avalonia-версия контейнера дерева групп (Linux). Фактическое содержимое дерева
    /// размещается в MainWindow; контейнер готов к переносу TreeView сюда без смены
    /// DataContext (наследуется).
    /// </summary>
    public class GroupTreeView : UserControl
    {
        public GroupTreeView()
        {
            Content = new ContentControl { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        }
    }
}
#endif