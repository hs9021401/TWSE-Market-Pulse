using System.Windows;

namespace TwseWidget.Services;

internal static class WidgetPlacementPolicy
{
    internal static Rect Fit(Rect window, IEnumerable<Rect> workingAreas)
    {
        var area = workingAreas.Where(rect => !rect.IsEmpty && rect.Width > 0 && rect.Height > 0)
            .OrderByDescending(rect =>
            {
                var intersection = Rect.Intersect(window, rect);
                return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
            })
            .ThenBy(rect =>
            {
                var dx = window.Left - Math.Clamp(window.Left, rect.Left, rect.Right);
                var dy = window.Top - Math.Clamp(window.Top, rect.Top, rect.Bottom);
                return dx * dx + dy * dy;
            })
            .FirstOrDefault();
        if (area.Width <= 0 || area.Height <= 0)
        {
            return window;
        }
        var width = Math.Min(window.Width, area.Width);
        var height = Math.Min(window.Height, area.Height);
        return new Rect(
            Math.Clamp(window.Left, area.Left, area.Right - width),
            Math.Clamp(window.Top, area.Top, area.Bottom - height),
            width, height);
    }
}
