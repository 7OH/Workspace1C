#if LINUX
using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Workspace1C.Models;

namespace Workspace1C.Converters
{
    /// <summary>
    /// Avalonia-конвертер подсветки подключённого сервера (issue #3, комментарий 7OH
    /// 19.10): строке списка «Сервера управления» даёт полужирное начертание, когда
    /// значение привязки (<c>ConnectedServer</c> из DataContext окна) совпадает по Id
    /// с записью, переданной конструктору. Запись фиксируется при создании шаблона
    /// строки (FuncDataTemplate), поэтому достаточно однозначной привязки к
    /// <c>ConnectedServer</c>. Правило равенства — чистое
    /// <see cref="ServerIdentityEquality.EqualsById"/>; при null/несовпадении —
    /// обычное начертание (жирным ничего не выделяется).
    /// </summary>
    public sealed class ServerConnectedFontWeightConverter : IValueConverter
    {
        private readonly ManagementServer _candidate;

        public ServerConnectedFontWeightConverter(ManagementServer candidate) => _candidate = candidate;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            ServerIdentityEquality.EqualsById(value as ManagementServer, _candidate)
                ? FontWeight.Bold
                : FontWeight.Normal;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
#endif