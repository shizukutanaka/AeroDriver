using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AeroDriver.UI.Converters
{
    /// <summary>値が null のとき Visible、非 null のとき Collapsed（プレースホルダー表示用）。</summary>
    public sealed class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value == null ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>値が非 null のとき Visible、null のとき Collapsed（内容表示用）。</summary>
    public sealed class NotNullToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value != null ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
