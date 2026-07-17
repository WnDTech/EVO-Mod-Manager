using System.Globalization;
using System.Windows.Data;
using System.Windows;
using EVO.ModManager.Core.Models;

namespace EVO.ModManager.App.Converters;

public class ModTypeToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ModType type)
            return type switch
            {
                ModType.Car => "🏎️",
                ModType.Track => "🏁",
                ModType.Skin => "🎨",
                ModType.Sound => "🔊",
                ModType.App => "📱",
                ModType.Misc => "📦",
                _ => "❓"
            };
        return "❓";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToStatusTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool enabled)
            return enabled ? "Enabled" : "Disabled";
        return "Unknown";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToToggleTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool enabled)
            return enabled ? "Disable" : "Enable";
        return "Toggle";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToStatusColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool enabled)
            return enabled ? System.Windows.Media.Color.FromRgb(0x2E, 0xCC, 0x71)
                           : System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C);
        return System.Windows.Media.Color.FromRgb(0xA0, 0xA0, 0xA0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BytesToSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long bytes)
            return bytes switch
            {
                < 1024 => $"{bytes} B",
                < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
                < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
                _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
            };
        return "0 B";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}


public class ConflictTypeToDescriptionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ConflictType type)
            return type switch
            {
                ConflictType.NameCollision => "Name Collision",
                ConflictType.HashMatch => "Hash Match",
                ConflictType.SourceConflict => "Source Conflict",
                _ => type.ToString()
            };
        return "Unknown";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ConflictResolutionToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ConflictType type)
            return type switch
            {
                ConflictType.NameCollision => System.Windows.Media.Color.FromRgb(0xF3, 0x9C, 0x12),
                ConflictType.HashMatch => System.Windows.Media.Color.FromRgb(0xE9, 0x45, 0x60),
                ConflictType.SourceConflict => System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C),
                _ => System.Windows.Media.Color.FromRgb(0xA0, 0xA0, 0xA0)
            };
        return System.Windows.Media.Color.FromRgb(0xA0, 0xA0, 0xA0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}


public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? Visibility.Collapsed : Visibility.Visible;
        return Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

