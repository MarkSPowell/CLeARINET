using System;
using System.Text;

namespace Clearinet.CompatShim;

/// <summary>
/// Members confirmed by metadata, static throughout (matches every call
/// site's <c>static</c> calling convention): <c>UrlDecode(string, Encoding)</c>
/// (ContentBlock), <c>getResponseBodyEncoding(Session)</c> and
/// <c>GetStringFromArrayRemovingBOM(byte[], Encoding)</c> (JSFormat),
/// <c>TrimAfter(string, char)</c> (JSFormat), <c>TrimBefore(string, string)</c>
/// and <c>TrimBefore(string, char)</c> (Differ), <c>ReadSessionArchive(string, bool)</c>
/// and <c>WriteSessionArchive(string, Session[], string, bool)</c>
/// (SAZClipboard, Differ), <c>CopyToClipboard(string)</c> (Differ).
/// </summary>
public static class Utilities
{
    public static string UrlDecode(string input, Encoding encoding) =>
        input == null ? null : Uri.UnescapeDataString(input);

    /// <summary>
    /// Real Fiddler's own encoding-detection heuristic (charset header,
    /// BOM sniffing, etc.) isn't reproduced here -- returns UTF-8 as a
    /// simple, real, always-available default pending Phase 2's real
    /// proxied-body data to validate against.
    /// </summary>
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
            // Diagnostic only, doesn't change the return value -- see
            // TrimBefore's own remarks below for why this is here.
            FiddlerApplication.Log.LogFormat("Utilities.TrimAfter: input was null, returning null.", Array.Empty<object>());
            return null;
        }
        var idx = input.IndexOf(delimiter);
        return idx < 0 ? input : input.Substring(0, idx);
    }

    /// <summary>
    /// Null input passes straight through as null (real Fiddler's own
    /// null-handling here isn't confirmed by metadata or documentation --
    /// this is a defensible placeholder, not confirmed behavior). Logged
    /// diagnostically while chasing a real NullReferenceException inside
    /// Differ.dll's own code with zero shim-method frames in the stack: a
    /// caller passing a null header value in here (say, from a missing
    /// header's indexer lookup) and not checking before using the result
    /// would look exactly like that crash, and this makes the last such
    /// pass-through before a crash visible on the Log tab without reading
    /// Differ's IL.
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

    /// <summary>See the <c>TrimBefore(string, string)</c> overload's own remarks.</summary>
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

    /// <summary>
    /// Real .saz read (unencrypted archives only) -- see
    /// <see cref="SazArchive"/> for the implementation and its own remarks,
    /// in particular: password-protected/encrypted .saz files aren't
    /// supported yet, and this throws rather than silently returning
    /// nothing for one.
    /// </summary>
    public static Session[] ReadSessionArchive(string filename, bool decrypt) => SazArchive.Read(filename, decrypt);

    /// <summary>
    /// Real .saz write (unencrypted archives only) -- see
    /// <see cref="SazArchive"/>'s own remarks.
    /// </summary>
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
