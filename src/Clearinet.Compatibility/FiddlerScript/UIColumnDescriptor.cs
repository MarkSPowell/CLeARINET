namespace Clearinet.Compatibility.FiddlerScript;

/// <summary>
/// One <c>[BindUIColumn(...)]</c>-declared method, scanned from a script's
/// own source -- real Fiddler documents four overloads
/// (<c>BindUIColumn(colName)</c>, <c>BindUIColumn(colName, bSortNumerically)</c>,
/// <c>BindUIColumn(colName, iColWidth)</c>,
/// <c>BindUIColumn(colName, iColWidth, iDisplayOrder)</c> -- Telerik's own
/// "Add Columns to Web Sessions List" page), all folded into this one
/// record with the arguments that overload doesn't supply left
/// <see langword="null"/>/<see langword="false"/>. See
/// <see cref="FiddlerScriptDirectiveScanner"/>'s own remarks on how the
/// scanner tells the two-argument overloads apart (a boolean-looking second
/// argument is <see cref="SortNumerically"/>, a numeric one is
/// <see cref="Width"/>).
/// </summary>
/// <param name="MethodName">The <c>Handlers</c>-class static method this column's value comes from -- takes one <c>Session</c>-shaped parameter, returns a string. See <see cref="FiddlerScriptRunner.ComputeUIColumnValue"/> for how it's actually called.</param>
/// <param name="ColumnTitle">The grid column's own header text.</param>
/// <param name="Width">The column's starting pixel width, if the script specified one -- <see langword="null"/> otherwise (the grid falls back to a sensible default).</param>
/// <param name="DisplayOrder">
/// Where the script asked this column to sit among other columns, if
/// specified. Scanned but not currently honored by the desktop UI -- every
/// script-provided column is appended after the built-in ones, in
/// declaration order (see <c>MainWindow.axaml.cs</c>'s own remarks) --
/// flagged here rather than silently dropped from the descriptor entirely.
/// </param>
/// <param name="SortNumerically">Whether the grid should sort this column's values as numbers rather than text -- scanned but not currently honored (see <see cref="DisplayOrder"/>'s own remarks; same reasoning).</param>
public sealed record UIColumnDescriptor(
    string MethodName, string ColumnTitle, int? Width, int? DisplayOrder, bool SortNumerically);
