/*
 * ============================================================================
 * AutoTrade-X - Value Converters
 * ============================================================================
 */

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AutoTradeX.UI.Converters;

/// <summary>
/// แปลง bool เป็น Visibility
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            // ถ้ามี parameter "inverse" จะกลับค่า
            var inverse = parameter?.ToString()?.ToLower() == "inverse";
            if (inverse) boolValue = !boolValue;

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Visible;
        }
        return false;
    }
}

/// <summary>
/// แปลง bool กลับค่า
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }
}

/// <summary>
/// แปลง null เป็น Visibility
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNull = value == null;
        var inverse = parameter?.ToString()?.ToLower() == "inverse";

        if (inverse) isNull = !isNull;

        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // One-way binding - ConvertBack not typically used for visibility converters
        // Return null to indicate the value could be either null or non-null
        return null!;
    }
}

/// <summary>
/// แปลงค่า decimal เป็นสี (บวก=เขียว, ลบ=แดง)
/// </summary>
public class PnLToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal decimalValue)
        {
            return decimalValue >= 0 ? "#10B981" : "#EF4444";
        }
        return "#666680";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // One-way binding - Cannot determine decimal value from color
        // Return 0 as default
        return 0m;
    }
}
