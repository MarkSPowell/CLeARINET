using System.Globalization;
using Avalonia.Data.Converters;

namespace Clearinet.DesktopUi.Converters;

/// <summary>
/// Formats raw bytes as a classic hex + ASCII dump (16 bytes per line, an
/// 8-digit offset prefix, unprintable bytes shown as '.') for the Hex
/// inspector tab. Deliberately lives here, in the desktop app, rather than
/// in Clearinet.Extensibility.Inspection.HexContent -- that record's job is
/// just to hand back bytes; how they're laid out on screen is a rendering
/// concern, which is the whole point of the inspector contract's "data in,
/// rendering out" split (see IInspector's remarks).
/// </summary>
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
