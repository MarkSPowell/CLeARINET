namespace Violens.Compatibility.FiddlerScript;

/// <summary>
/// A <c>[BindUIColumn(...)]</c> method, from any of Fiddler's four
/// overloads; arguments an overload doesn't have are null or false.
/// </summary>
/// <param name="MethodName">The <c>Handlers</c> method: takes a session, returns a string.</param>
/// <param name="ColumnTitle">The column header.</param>
/// <param name="Width">The starting width, if given.</param>
/// <param name="DisplayOrder">The requested position, if given. Not honoured yet: script columns go after the built-in ones.</param>
/// <param name="SortNumerically">Whether to sort as numbers. Not honoured yet.</param>
public sealed record UIColumnDescriptor(
    string MethodName, string ColumnTitle, int? Width, int? DisplayOrder, bool SortNumerically);
