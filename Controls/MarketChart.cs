using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TwseWidget.Models;

namespace TwseWidget.Controls;

public enum ChartPresentation
{
    Full,
    Mini,
    Ticker
}

public sealed class MarketChart : FrameworkElement
{
    public static readonly DependencyProperty PresentationProperty = DependencyProperty.Register(
        nameof(Presentation),
        typeof(ChartPresentation),
        typeof(MarketChart),
        new FrameworkPropertyMetadata(
            ChartPresentation.Full,
            FrameworkPropertyMetadataOptions.AffectsRender,
            OnPresentationChanged),
        value => value is ChartPresentation.Full or ChartPresentation.Mini or ChartPresentation.Ticker);

    private static readonly Brush GridBrush = new SolidColorBrush(Color.FromArgb(48, 150, 184, 210));
    private static readonly Brush AxisBrush = new SolidColorBrush(Color.FromArgb(105, 162, 190, 216));
    private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromArgb(170, 151, 174, 194));
    private static readonly Brush CrosshairBrush = new SolidColorBrush(Color.FromRgb(255, 84, 84));
    private static readonly Brush CrosshairBackground = new SolidColorBrush(Color.FromArgb(225, 10, 19, 32));
    private IReadOnlyList<ChartPoint> _points = Array.Empty<ChartPoint>();
    private bool _isPositive;
    private bool _hasData;
    private bool _isPointerOverPlot;
    private double _pointerX;

    public ChartPresentation Presentation
    {
        get => (ChartPresentation)GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

    static MarketChart()
    {
        GridBrush.Freeze();
        AxisBrush.Freeze();
        LabelBrush.Freeze();
        CrosshairBrush.Freeze();
        CrosshairBackground.Freeze();
    }

    public void SetData(IReadOnlyList<ChartPoint> points, bool isPositive, bool hasData)
    {
        _points = points.ToArray();
        _isPositive = isPositive;
        _hasData = hasData;
        UpdateHoverToolTip(GetPlotRect());
        InvalidateVisual();
    }

    private static void OnPresentationChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var chart = (MarketChart)dependencyObject;
        chart._isPointerOverPlot = false;
        chart.ToolTip = null;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var plot = GetPlotRect();
        var position = e.GetPosition(this);
        _isPointerOverPlot = _hasData &&
                             _points.Count > 0 &&
                             plot.Contains(position);
        if (_isPointerOverPlot)
        {
            _pointerX = Math.Clamp(position.X, plot.Left, plot.Right);
        }

        UpdateHoverToolTip(plot);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _isPointerOverPlot = false;
        ToolTip = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (Presentation == ChartPresentation.Full)
        {
            RenderChart(drawingContext);
            return;
        }

        // Compact markers and labels must stay inside the space assigned by the host.
        drawingContext.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        try
        {
            RenderChart(drawingContext);
        }
        finally
        {
            drawingContext.Pop();
        }
    }

    private void RenderChart(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        var isFull = Presentation == ChartPresentation.Full;
        var isTicker = Presentation == ChartPresentation.Ticker;
        if (isFull ? width < 80 || height < 40 : width < 4 || height < 4)
        {
            return;
        }

        var plot = GetPlotRect();
        // A transparent surface keeps the axis-free sparkline responsive to hover.
        if (!isFull)
        {
            drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        }

        if (isFull)
        {
            DrawGrid(drawingContext, plot);
        }
        else if (!isTicker)
        {
            drawingContext.DrawLine(new Pen(GridBrush, 1),
                new Point(plot.Left, plot.Top + plot.Height / 2),
                new Point(plot.Right, plot.Top + plot.Height / 2));
            drawingContext.DrawLine(new Pen(AxisBrush, 1),
                new Point(plot.Left, plot.Bottom),
                new Point(plot.Right, plot.Bottom));
        }

        if (!_hasData || _points.Count == 0)
        {
            if (isFull)
            {
                DrawText(
                    drawingContext,
                    "WAITING FOR TWSE MARKET TICKS",
                    new Point(plot.Left + 8, plot.Top + plot.Height / 2 - 8),
                    10,
                    LabelBrush);
            }
            else if (plot.Width >= 45 && plot.Height >= 14)
            {
                DrawText(drawingContext, "WAITING",
                    new Point(plot.Left + 2, plot.Top + (plot.Height - 12) / 2), 9, LabelBrush);
            }

            DrawPresentationXAxis(drawingContext, plot);
            return;
        }

        var min = _points.Min(point => point.Value);
        var max = _points.Max(point => point.Value);
        var range = max - min;
        var padding = range == 0 ? Math.Max(Math.Abs(max) * 0.0006m, 1m) : range * 0.18m;
        min -= padding;
        max += padding;
        range = max - min;

        var sessionStart = GetSessionStart();
        var sessionEnd = new DateTimeOffset(
            _points[^1].Timestamp.Date.AddHours(13).AddMinutes(30),
            _points[^1].Timestamp.Offset);
        var sessionSeconds = Math.Max(1, (sessionEnd - sessionStart).TotalSeconds);

        var coordinates = _points
            .Select(point =>
            {
                var elapsedSeconds = (point.Timestamp - sessionStart).TotalSeconds;
                var xRatio = Math.Clamp(elapsedSeconds / sessionSeconds, 0, 1);
                var yRatio = (double)((point.Value - min) / range);
                return new Point(
                    plot.Left + plot.Width * xRatio,
                    plot.Bottom - plot.Height * yRatio);
            })
            .ToArray();

        var lineColor = _isPositive ? Color.FromRgb(57, 242, 183) : Color.FromRgb(71, 205, 255);
        var lineBrush = new SolidColorBrush(lineColor);
        var glowBrush = new SolidColorBrush(Color.FromArgb(70, lineColor.R, lineColor.G, lineColor.B));
        var areaBrush = new LinearGradientBrush(
            Color.FromArgb(85, lineColor.R, lineColor.G, lineColor.B),
            Color.FromArgb(0, lineColor.R, lineColor.G, lineColor.B),
            new Point(0, 0),
            new Point(0, 1));

        if (!isTicker)
        {
            var area = new StreamGeometry();
            using (var areaContext = area.Open())
            {
                areaContext.BeginFigure(new Point(coordinates[0].X, plot.Bottom), true, true);
                areaContext.LineTo(coordinates[0], true, false);
                for (var index = 1; index < coordinates.Length; index++)
                {
                    areaContext.LineTo(coordinates[index], true, false);
                }

                areaContext.LineTo(new Point(coordinates[^1].X, plot.Bottom), true, false);
            }

            drawingContext.DrawGeometry(areaBrush, null, area);
        }

        if (coordinates.Length > 1)
        {
            var line = new StreamGeometry();
            using (var lineContext = line.Open())
            {
                lineContext.BeginFigure(coordinates[0], false, false);
                for (var index = 1; index < coordinates.Length; index++)
                {
                    lineContext.LineTo(coordinates[index], true, false);
                }
            }

            if (isFull)
            {
                drawingContext.DrawGeometry(null, new Pen(glowBrush, 7) { LineJoin = PenLineJoin.Round }, line);
            }

            drawingContext.DrawGeometry(null, new Pen(lineBrush, isFull ? 2 : 1.5) { LineJoin = PenLineJoin.Round }, line);
        }

        var latest = coordinates[^1];
        if (isFull)
        {
            drawingContext.DrawLine(
                new Pen(new SolidColorBrush(Color.FromArgb(105, lineColor.R, lineColor.G, lineColor.B)), 1),
                new Point(latest.X, plot.Top),
                new Point(latest.X, plot.Bottom));
            drawingContext.DrawEllipse(lineBrush, null, latest, 4.5, 4.5);
            drawingContext.DrawEllipse(
                Brushes.Transparent,
                new Pen(new SolidColorBrush(Color.FromArgb(170, lineColor.R, lineColor.G, lineColor.B)), 1),
                latest,
                8,
                8);
        }
        else
        {
            drawingContext.DrawEllipse(lineBrush, null, latest, 2, 2);
        }

        if (_isPointerOverPlot)
        {
            DrawCrosshair(
                drawingContext,
                plot,
                sessionStart,
                sessionSeconds,
                min,
                max);
        }

        if (isFull)
        {
            DrawYAxis(drawingContext, plot, min, max);
        }

        DrawPresentationXAxis(drawingContext, plot);
        lineBrush.Freeze();
        glowBrush.Freeze();
        areaBrush.Freeze();
    }

    private Rect GetPlotRect()
    {
        if (Presentation == ChartPresentation.Full)
        {
            return new Rect(8, 8, Math.Max(1, ActualWidth - 16), Math.Max(1, ActualHeight - 30));
        }

        var padding = Presentation == ChartPresentation.Ticker ? 2 : 4;
        var left = Math.Min(padding, ActualWidth / 4);
        var top = Math.Min(padding, ActualHeight / 4);
        var bottom = Presentation == ChartPresentation.Mini && ActualHeight >= 28 ? 16 : top;
        return new Rect(left, top,
            Math.Max(1, ActualWidth - left * 2),
            Math.Max(1, ActualHeight - top - bottom));
    }

    private DateTimeOffset GetSessionStart() => new(
        _points[^1].Timestamp.Date.AddHours(9),
        _points[^1].Timestamp.Offset);

    private static string FormatHoverLabel(DateTimeOffset timestamp, decimal value) =>
        $"{timestamp.ToString("HH:mm", CultureInfo.InvariantCulture)}  {value.ToString("#,##0.00", CultureInfo.InvariantCulture)}";

    private void UpdateHoverToolTip(Rect plot)
    {
        if (Presentation == ChartPresentation.Full || !_isPointerOverPlot || !_hasData || _points.Count == 0)
        {
            ToolTip = null;
            return;
        }

        var xRatio = Math.Clamp((_pointerX - plot.Left) / plot.Width, 0, 1);
        var timestamp = GetSessionStart().AddSeconds(xRatio * TimeSpan.FromHours(4.5).TotalSeconds);
        ToolTip = FormatHoverLabel(timestamp, InterpolateValue(timestamp));
    }

    private void DrawCrosshair(
        DrawingContext drawingContext,
        Rect plot,
        DateTimeOffset sessionStart,
        double sessionSeconds,
        decimal min,
        decimal max)
    {
        var pointerX = Math.Clamp(_pointerX, plot.Left, plot.Right);
        var xRatio = Math.Clamp((pointerX - plot.Left) / plot.Width, 0, 1);
        var timestamp = sessionStart.AddSeconds(xRatio * sessionSeconds);
        var value = InterpolateValue(timestamp);
        var yRatio = (double)((value - min) / (max - min));
        var point = new Point(
            pointerX,
            Math.Clamp(plot.Bottom - plot.Height * yRatio, plot.Top, plot.Bottom));

        drawingContext.DrawLine(
            new Pen(CrosshairBrush, 1) { DashStyle = DashStyles.Dash },
            new Point(pointerX, plot.Top),
            new Point(pointerX, plot.Bottom));
        drawingContext.DrawEllipse(CrosshairBrush, null, point,
            Presentation == ChartPresentation.Full ? 3.5 : 2.5,
            Presentation == ChartPresentation.Full ? 3.5 : 2.5);
        if (Presentation == ChartPresentation.Full)
        {
            drawingContext.DrawEllipse(
                Brushes.Transparent,
                new Pen(new SolidColorBrush(Color.FromArgb(190, 255, 84, 84)), 1),
                point,
                7,
                7);
        }

        if (Presentation == ChartPresentation.Ticker ||
            (Presentation == ChartPresentation.Mini && (plot.Width < 32 || plot.Height < 20)))
        {
            return;
        }

        var label = FormatHoverLabel(timestamp, value);
        var formatted = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Consolas"),
            9,
            CrosshairBrush,
            1.0);
        if (Presentation == ChartPresentation.Mini)
        {
            formatted.MaxTextWidth = Math.Max(1, plot.Width - 8);
            formatted.MaxLineCount = 1;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }

        var labelWidth = formatted.Width + 8;
        var labelX = pointerX + 6;
        if (labelX + labelWidth > plot.Right)
        {
            labelX = pointerX - labelWidth - 6;
        }

        labelX = Math.Clamp(labelX, plot.Left, Math.Max(plot.Left, plot.Right - labelWidth));
        var labelRect = new Rect(labelX, plot.Top + 2, labelWidth, formatted.Height + 4);
        drawingContext.DrawRoundedRectangle(
            CrosshairBackground,
            new Pen(new SolidColorBrush(Color.FromArgb(170, 255, 84, 84)), 1),
            labelRect,
            4,
            4);
        drawingContext.DrawText(formatted, new Point(labelRect.Left + 4, labelRect.Top + 2));
    }

    private decimal InterpolateValue(DateTimeOffset timestamp)
    {
        if (_points.Count == 0)
        {
            return 0;
        }

        if (timestamp <= _points[0].Timestamp)
        {
            return _points[0].Value;
        }

        for (var index = 1; index < _points.Count; index++)
        {
            var previous = _points[index - 1];
            var next = _points[index];
            if (timestamp > next.Timestamp)
            {
                continue;
            }

            var seconds = (next.Timestamp - previous.Timestamp).TotalSeconds;
            if (seconds <= 0)
            {
                return next.Value;
            }

            var ratio = Math.Clamp(
                (decimal)((timestamp - previous.Timestamp).TotalSeconds / seconds),
                0,
                1);
            return previous.Value + (next.Value - previous.Value) * ratio;
        }

        return _points[^1].Value;
    }

    private static void DrawGrid(DrawingContext drawingContext, Rect plot)
    {
        for (var row = 0; row <= 4; row++)
        {
            var y = plot.Top + plot.Height * row / 4;
            drawingContext.DrawLine(new Pen(row == 4 ? AxisBrush : GridBrush, 1),
                new Point(plot.Left, y),
                new Point(plot.Right, y));
        }

        for (var column = 0; column <= 4; column++)
        {
            var x = plot.Left + plot.Width * column / 4;
            drawingContext.DrawLine(new Pen(GridBrush, 1),
                new Point(x, plot.Top),
                new Point(x, plot.Bottom));
        }
    }

    private static void DrawYAxis(DrawingContext drawingContext, Rect plot, decimal min, decimal max)
    {
        DrawText(drawingContext, FormatAxis(max), new Point(plot.Left + 2, plot.Top - 1), 9, LabelBrush);
        DrawText(drawingContext, FormatAxis(min), new Point(plot.Left + 2, plot.Bottom - 12), 9, LabelBrush);
    }

    private static void DrawXAxis(DrawingContext drawingContext, Rect plot)
    {
        var labels = new[] { "09:00", "10:00", "11:00", "12:00", "13:30" };
        for (var index = 0; index < labels.Length; index++)
        {
            var x = plot.Left + plot.Width * index / (labels.Length - 1);
            DrawText(drawingContext, labels[index], new Point(x - 16, plot.Bottom + 5), 9, LabelBrush);
        }
    }

    private void DrawPresentationXAxis(DrawingContext drawingContext, Rect plot)
    {
        if (Presentation == ChartPresentation.Full)
        {
            DrawXAxis(drawingContext, plot);
            return;
        }

        if (Presentation != ChartPresentation.Mini || ActualHeight < 28 || plot.Width < 65)
        {
            return;
        }

        var start = new FormattedText("09:00", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, LabelBrush, 1.0);
        var end = new FormattedText("13:30", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, LabelBrush, 1.0);
        drawingContext.DrawText(start, new Point(plot.Left, plot.Bottom + 3));
        drawingContext.DrawText(end, new Point(plot.Right - end.Width, plot.Bottom + 3));
    }

    private static string FormatAxis(decimal value) =>
        value.ToString("#,##0", CultureInfo.InvariantCulture);

    private static void DrawText(
        DrawingContext drawingContext,
        string text,
        Point location,
        double fontSize,
        Brush brush)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            brush,
            1.0);
        drawingContext.DrawText(formatted, location);
    }
}
