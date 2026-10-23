using System.Globalization;
using Avalonia.Data.Converters;

namespace Violens.DesktopUi.Converters;

/// <summary>Hex + ASCII dump for the Hex tab: 16 bytes per line with an 8-digit offset.</summary>
public sealed class HexDumpConverter : IValueConverter
{
    public static readonly HexDumpConverter Instance = new();

    private const int BytesPerLine = 16;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] bytes)
        {
            return null;
        }

        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var lines = new List<string>(bytes.Length / BytesPerLine + 1);
        for (var offset = 0; offset < bytes.Length; offset += BytesPerLine)
        {
            var lineLength = Math.Min(BytesPerLine, bytes.Length - offset);
            lines.Add(FormatLine(bytes, offset, lineLength));
        }

        return string.Join(Environment.NewLine, lines);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Hex dump display is one-way; there's nothing to edit here yet.");

    private static string FormatLine(byte[] bytes, int offset, int lineLength)
    {
        var hexParts = new string[lineLength];
        var asciiChars = new char[lineLength];
        for (var i = 0; i < lineLength; i++)
        {
            var b = bytes[offset + i];
            hexParts[i] = b.ToString("x2", CultureInfo.InvariantCulture);
            asciiChars[i] = b is >= 0x20 and < 0x7f ? (char)b : '.';
        }

        var hex = string.Join(' ', hexParts).PadRight((BytesPerLine * 3) - 1);
        return $"{offset:x8}  {hex}  {new string(asciiChars)}";
    }
}
