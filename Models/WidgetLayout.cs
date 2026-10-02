namespace TwseWidget.Models;

public enum WidgetLayout
{
    Full,
    Mini,
    Ticker
}

public sealed record WidgetSettings
{
    public WidgetLayout Layout { get; init; } = WidgetLayout.Full;

    public double? Left { get; init; }

    public double? Top { get; init; }

    public double FullWidth { get; init; } = 360;

    public double FullHeight { get; init; } = 240;

    public bool IsTopmost { get; init; } = true;
}
