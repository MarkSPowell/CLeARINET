using System;
using System.Text;

namespace Violens.CompatShim;

/// <summary>
/// The static helpers the samples call: <c>UrlDecode</c> (ContentBlock),
/// <c>getResponseBodyEncoding</c>, <c>GetStringFromArrayRemovingBOM</c> and
/// <c>TrimAfter</c> (JSFormat), <c>TrimBefore</c> (Differ),
/// <c>ReadSessionArchive</c> and <c>WriteSessionArchive</c> (SAZClipboard,
/// Differ) and <c>CopyToClipboard</c> (Differ).
/// </summary>
public static class Utilities
{
    public static string UrlDecode(string input, Encoding encoding) =>
        input == null ? null : Uri.UnescapeDataString(input);

    /// <summary>Always UTF-8; Fiddler's charset detection isn't reproduced.</summary>
    public static Encoding getResponseBodyEncoding(Session session) => Encoding.UTF8;

    public static string GetStringFromArrayRemovingBOM(byte[] bytes, Encoding encoding)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return string.Empty;
        }
        var preamble = (encoding ?? Encoding.UTF8).GetPreamble();
        var offset = 0;
        if (preamble.Length > 0 && bytes.Length >= preamble.Length)
        {
            var hasBom = true;
            for (var i = 0; i < preamble.Length; i++)
            {
                if (bytes[i] != preamble[i])
                {
                    hasBom = false;
                    break;
                }
            }
            if (hasBom)
            {
                offset = preamble.Length;
            }
        }
        return (encoding ?? Encoding.UTF8).GetString(bytes, offset, bytes.Length - offset);
    }

    public static string TrimAfter(string input, char delimiter)
    {
        if (input == null)
        {
            // Diagnostic only.
            FiddlerApplication.Log.LogFormat("Utilities.TrimAfter: input was null, returning null.", Array.Empty<object>());
            return null;
        }
        var idx = input.IndexOf(delimiter);
        return idx < 0 ? input : input.Substring(0, idx);
    }

    /// <summary>
    /// Null in, null out (Fiddler's behaviour here isn't documented). Logged,
    /// to make a null a caller then dereferences visible on the Log tab.
    /// </summary>
    public static string TrimBefore(string input, string delimiter)
    {
        if (input == null || string.IsNullOrEmpty(delimiter))
        {
            if (input == null)
            {
                FiddlerApplication.Log.LogFormat("Utilities.TrimBefore(string): input was null, returning null.", Array.Empty<object>());
            }
            return input;
        }
        var idx = input.IndexOf(delimiter, StringComparison.Ordinal);
        return idx < 0 ? input : input.Substring(idx + delimiter.Length);
    }

    /// <summary>As the string overload.</summary>
    public static string TrimBefore(string input, char delimiter)
    {
        if (input == null)
        {
            FiddlerApplication.Log.LogFormat("Utilities.TrimBefore(char): input was null, returning null.", Array.Empty<object>());
            return null;
        }
        var idx = input.IndexOf(delimiter);
        return idx < 0 ? input : input.Substring(idx + 1);
    }

    /// <summary>Reads a .saz file; encrypted archives throw (see <see cref="SazArchive"/>).</summary>
    public static Session[] ReadSessionArchive(string filename, bool decrypt) => SazArchive.Read(filename, decrypt);

    /// <summary>Writes a .saz file; encryption isn't supported (see <see cref="SazArchive"/>).</summary>
    public static bool WriteSessionArchive(string filename, Session[] sessions, string password, bool encrypt) =>
        SazArchive.Write(filename, sessions, password, encrypt);

    public static bool CopyToClipboard(string text)
    {
        try
        {
            System.Windows.Forms.Clipboard.SetText(text ?? string.Empty);
            return true;
        }
        catch (Exception)
        {
            // Clipboard access can throw transiently on Windows (another
            // process holding it open) -- reported as a soft failure via
            // the bool return, not an unhandled exception into extension code.
            return false;
        }
    }
}
