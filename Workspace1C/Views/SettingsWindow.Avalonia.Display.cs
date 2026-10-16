#if LINUX
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Диалог настроек приложения (Avalonia/Linux), часть «Отображение»:
    /// модели колонок списка баз.
    /// </summary>
    public partial class SettingsWindow
    {
        /// <summary>
        /// Строка списка колонок: ключ колонки, локализованное имя и флаг видимости.
        /// Один элемент объединяет порядок и видимость колонки — оба редактируются
        /// в одном списке на вкладке «Отображение».
        /// </summary>
        private sealed class ColumnOrderItem
        {
            public string Key { get; }
            public string Display { get; }
            public bool Visible { get; set; }
            public string IconKey { get; }

            public ColumnOrderItem(string key, string display, bool visible, string iconKey)
            {
                Key = key;
                Display = display;
                Visible = visible;
                IconKey = iconKey;
            }

            public override string ToString() => Display;
        }
    }
}
#endif