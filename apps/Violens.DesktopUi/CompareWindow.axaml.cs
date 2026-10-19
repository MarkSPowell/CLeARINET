using Avalonia.Controls;
using Avalonia.Interactivity;
using Violens.DesktopUi.ViewModels;
using Violens.AppCore.Compare;
using Violens.ProxyCore.Sessions;

namespace Violens.DesktopUi;

/// <summary>
/// Compare Sessions: the older of two sessions on the left, the newer on
/// the right, request and response on their own tabs. Changing an option
/// compares again. Not modal, so several comparisons can stay open while
/// capturing goes on.
/// </summary>
public partial class CompareWindow : Window
{
    private readonly Session? _left;
    private readonly Session? _right;
    private IReadOnlyList<int> _requestStarts = [];
    private IReadOnlyList<int> _responseStarts = [];
    private int _version;

    public CompareWindow()
    {
        InitializeComponent();
    }

    public CompareWindow(Session left, Session right)
        : this()
    {
        (_left, _right) = left.Id <= right.Id ? (left, right) : (right, left);
        Title = $"Compare #{_left.Id} and #{_right.Id}";
        LeftLabel.Text = Describe(_left);
        RightLabel.Text = Describe(_right);
        _ = CompareAsync();
    }

    private static string Describe(Session session) =>
        $"#{session.Id}  {session.Request.Method} {SessionUrl.Of(session)}  →  {session.Response.StatusCode} {session.Response.ReasonPhrase}".TrimEnd();

    private void OptionChanged(object? sender, RoutedEventArgs e) => _ = CompareAsync();

    private async Task CompareAsync()
    {
        if (_left is null || _right is null)
        {
            return;
        }

        var options = new SessionCompareOptions(
            DecodeBodies: DecodeBodiesBox.IsChecked == true,
            FormatBodies: FormatBodiesBox.IsChecked == true,
            SortHeaders: SortHeadersBox.IsChecked == true);
        var version = ++_version;
        SummaryText.Text = "Comparing…";

        var (left, right) = (_left, _right);
        SessionComparisonResult result;
        List<CompareRow> requestRows, responseRows;
        try
        {
            (result, requestRows, responseRows) = await Task.Run(() =>
            {
                var compared = SessionComparison.Compare(left, right, options);
                return (compared,
                    compared.Request.Rows.Select(CompareRow.From).ToList(),
                    compared.Response.Rows.Select(CompareRow.From).ToList());
            });
        }
        catch (Exception ex)
        {
            if (version == _version)
            {
                SummaryText.Text = $"Couldn't compare these sessions: {ex.Message}";
            }

            return;
        }

        if (version != _version)
        {
            return; // an option changed again while this ran
        }

        RequestList.ItemsSource = requestRows;
        ResponseList.ItemsSource = responseRows;
        _requestStarts = result.Request.DifferenceStarts();
        _responseStarts = result.Response.DifferenceStarts();
        RequestTab.Header = TabHeader("Request", result.Request);
        ResponseTab.Header = TabHeader("Response", result.Response);
        SummaryText.Text = Summary(result);

        // Open on the side that differs, if only one does.
        if (result.Request.Identical && !result.Response.Identical)
        {
            SidesTabs.SelectedItem = ResponseTab;
        }
    }

    private static string TabHeader(string side, DiffResult result) =>
        result.Identical ? $"{side} (same)" : $"{side} ({result.Differences} {(result.Differences == 1 ? "difference" : "differences")})";

    private static string Summary(SessionComparisonResult result)
    {
        if (result.Differences == 0)
        {
            return "The requests and responses are the same, as compared.";
        }

        var simplified = result.Request.Simplified || result.Response.Simplified
            ? " Parts are too different to line up line by line, so they're shown as one block."
            : string.Empty;
        return $"Request: {Count(result.Request)}. Response: {Count(result.Response)}.{simplified}";

        static string Count(DiffResult side) => side.Identical
            ? "same"
            : $"{side.Differences} {(side.Differences == 1 ? "difference" : "differences")}";
    }

    private void NextButton_Click(object? sender, RoutedEventArgs e) => Move(forward: true);

    private void PreviousButton_Click(object? sender, RoutedEventArgs e) => Move(forward: false);

    /// <summary>Selects and scrolls to the next (or previous) difference on the tab that's showing.</summary>
    private void Move(bool forward)
    {
        var onResponse = SidesTabs.SelectedItem == ResponseTab;
        var list = onResponse ? ResponseList : RequestList;
        var starts = onResponse ? _responseStarts : _requestStarts;
        if (starts.Count == 0)
        {
            return;
        }

        var current = list.SelectedIndex;
        int target;
        if (forward)
        {
            target = starts.FirstOrDefault(s => s > current, -1);
            if (target < 0)
            {
                target = starts[0]; // wrap around
            }
        }
        else
        {
            target = starts.LastOrDefault(s => s < current, -1);
            if (target < 0)
            {
                target = starts[^1];
            }
        }

        list.SelectedIndex = target;
        list.ScrollIntoView(target);
    }
}
