using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Clearinet.ProxyCore.Sessions;

namespace Clearinet.DesktopUi.Controls;

/// <summary>
/// The time range the waterfall column spans: from the first shown session
/// starting to the last one finishing, like a browser's developer tools.
/// One shared instance, kept up to date by the main window's view model;
/// every <see cref="WaterfallBar"/> redraws when it changes.
/// </summary>
public sealed class WaterfallScale
{
    private bool _changePending;

    public static WaterfallScale Shared { get; } = new();

    public DateTimeOffset Start { get; private set; }

    public DateTimeOffset End { get; private set; }

    public bool IsEmpty => End <= Start;

    public double SpanMilliseconds => Math.Max(1, (End - Start).TotalMilliseconds);

    /// <summary>Raised (on the UI thread, at most once per change burst) when the range moves.</summary>
    public event Action? Changed;

    /// <summary>Recomputes the range from <paramref name="sessions"/>.</summary>
    public void Reset(IEnumerable<Session> sessions)
    {
        var any = false;
        DateTimeOffset start = default, end = default;
        foreach (var session in sessions)
        {
            var sessionEnd = SessionStatistics.End(session);
            if (!any || session.StartedAt < start)
            {
                start = session.StartedAt;
            }

            if (!any || sessionEnd > end)
            {
                end = sessionEnd;
            }

            any = true;
        }

        Set(any ? start : default, any ? end : default);
    }

    /// <summary>Widens the range to include <paramref name="session"/>.</summary>
    public void Include(Session session)
    {
        var sessionEnd = SessionStatistics.End(session);
        if (IsEmpty && Start == default)
        {
            Set(session.StartedAt, sessionEnd);
            return;
        }

        Set(session.StartedAt < Start ? session.StartedAt : Start, sessionEnd > End ? sessionEnd : End);
    }

    private void Set(DateTimeOffset start, DateTimeOffset end)
    {
        if (start == Start && end == End)
        {
            return;
        }

        Start = start;
        End = end;

        // Sessions can arrive by the hundred; redraw once for the lot.
        if (!_changePending)
        {
            _changePending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _changePending = false;
                Changed?.Invoke();
            }, DispatcherPriority.Background);
        }
    }
}

/// <summary>
/// One session's bar in the waterfall column: where it starts within
/// <see cref="WaterfallScale.Shared"/>, and a coloured segment per phase
/// (see <see cref="TimingPalette"/>). Hovering shows the breakdown.
/// </summary>
public sealed class WaterfallBar : Control
{
    public static readonly StyledProperty<Session?> SessionProperty =
        AvaloniaProperty.Register<WaterfallBar, Session?>(nameof(Session));

    private const double BarHeight = 8;

    static WaterfallBar()
    {
        AffectsRender<WaterfallBar>(SessionProperty);
    }

    public Session? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SessionProperty)
        {
            ToolTip.SetTip(this, Session is { } session ? TimingPalette.Describe(session) : null);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        WaterfallScale.Shared.Changed += InvalidateVisual;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        WaterfallScale.Shared.Changed -= InvalidateVisual;
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, BarHeight);

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // Transparent, but drawn, so hovering anywhere in the cell shows the breakdown.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var scale = WaterfallScale.Shared;
        if (Session is not { Timings: { } timings } session || scale.IsEmpty || Bounds.Width <= 0)
        {
            return;
        }

        var width = Bounds.Width;
        var top = Math.Max(0, (Bounds.Height - BarHeight) / 2);
        var x = (session.StartedAt - scale.Start).TotalMilliseconds / scale.SpanMilliseconds * width;
        foreach (var (_, _, brush, milliseconds) in TimingPalette.Phases(timings))
        {
            var segment = milliseconds / scale.SpanMilliseconds * width;
            if (segment <= 0)
            {
                continue;
            }

            context.FillRectangle(brush, new Rect(x, top, Math.Max(segment, 1), BarHeight));
            x += segment;
        }
    }
}

/// <summary>
/// A single bar within a track: starts <see cref="StartFraction"/> of the
/// way across and is <see cref="LengthFraction"/> of it long (at least one
/// pixel). Used by the Timing tab and the Statistics tab.
/// </summary>
public sealed class PhaseBar : Control
{
    public static readonly StyledProperty<double> StartFractionProperty =
        AvaloniaProperty.Register<PhaseBar, double>(nameof(StartFraction));

    public static readonly StyledProperty<double> LengthFractionProperty =
        AvaloniaProperty.Register<PhaseBar, double>(nameof(LengthFraction));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<PhaseBar, IBrush?>(nameof(Fill));

    static PhaseBar()
    {
        AffectsRender<PhaseBar>(StartFractionProperty, LengthFractionProperty, FillProperty);
    }

    public double StartFraction
    {
        get => GetValue(StartFractionProperty);
        set => SetValue(StartFractionProperty, value);
    }

    public double LengthFraction
    {
        get => GetValue(LengthFractionProperty);
        set => SetValue(LengthFractionProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 10);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Fill is null || Bounds.Width <= 0 || LengthFraction <= 0)
        {
            return;
        }

        var x = Math.Clamp(StartFraction, 0, 1) * Bounds.Width;
        var length = Math.Max(1, Math.Clamp(LengthFraction, 0, 1) * Bounds.Width);
        context.FillRectangle(Fill, new Rect(x, 0, Math.Min(length, Bounds.Width - x + 1), Bounds.Height));
    }
}
