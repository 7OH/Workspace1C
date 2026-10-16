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
    /// Диалог настроек приложения (Avalonia/Linux), часть «Язык интерфейса».
    /// Отдельных членов верхнего уровня здесь нет: выбор языка строится внутри
    /// BuildRoot на вкладке «Настройки», а пересборка при смене языка выполняется
    /// в Core (OnLanguageChanged). Файл создан зеркально WPF-partial
    /// SettingsWindow.Language.cs, чтобы структура вкладок совпадала.
    /// </summary>
    public partial class SettingsWindow
    {
    }
}
#endif