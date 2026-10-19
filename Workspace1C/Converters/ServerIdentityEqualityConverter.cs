using System;
using System.Globalization;
using System.Windows.Data;
using Workspace1C.Models;

namespace Workspace1C.Converters;

/// <summary>
/// WPF-конвертер подсветки подключённого сервера (issue #3, комментарий 7OH 19.10):
/// MultiBinding в DataTrigger стиля строки списка «Сервера управления» — значения:
/// подключённый сервер из DataContext списка (@{Binding DataContext.ConnectedServer})
/// и текущий элемент (@{Binding}); результат — true, если Id совпадают
/// (делегирует чистому <see cref="ServerIdentityEquality.EqualsById"/>). При отсутствии
/// подключения (ConnectedServer == null) возвращается false — триггер не срабатывает.
/// </summary>
public sealed class ServerIdentityEqualityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && values[0] is ManagementServer connected && values[1] is ManagementServer candidate
            ? ServerIdentityEquality.EqualsById(connected, candidate)
            : false;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}