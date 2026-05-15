using System.Globalization;

namespace Downpour.App.Converters;

public class StatusToColorConverter : IValueConverter
{
    public bool IsBackground { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string statusLabel)
            return IsBackground ? Color.FromArgb("#149E9E9E") : Color.FromArgb("#FF9E9E9E");

        switch (statusLabel)
        {
            case "Downloading":
                return IsBackground ? Color.FromArgb("#142196F3") : Color.FromArgb("#FF2196F3");
            case "Seeding":
                return IsBackground ? Color.FromArgb("#144CAF50") : Color.FromArgb("#FF4CAF50");
            case "Paused":
                return IsBackground ? Color.FromArgb("#14FF9800") : Color.FromArgb("#FFFF9800");
        }

        if (statusLabel.StartsWith("Error"))
            return IsBackground ? Color.FromArgb("#14F44336") : Color.FromArgb("#FFF44336");
        return IsBackground ? Color.FromArgb("#149E9E9E") : Color.FromArgb("#FF9E9E9E");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}