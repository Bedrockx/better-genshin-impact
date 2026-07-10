using System;
using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Converters;

/// <summary>
/// 将布尔值转换为按钮外观样式
/// true -> Primary (强调样式)
/// false -> Secondary (普通样式)
/// </summary>
[ValueConversion(typeof(bool), typeof(ControlAppearance))]
public sealed class BoolToButtonAppearanceConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? ControlAppearance.Primary : ControlAppearance.Secondary;
        }
        return ControlAppearance.Secondary;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
