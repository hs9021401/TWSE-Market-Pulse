using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace TwseWidget.Services;

public static class DragSurfacePolicy
{
    public static bool ShouldBeginDrag(bool isInteractiveElement) => !isInteractiveElement;

    public static bool ShouldBeginDrag(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Window)
            {
                return true;
            }

            if (source is Control)
            {
                return false;
            }

            source = source switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(source),
                _ => LogicalTreeHelper.GetParent(source)
            };
        }

        return true;
    }
}
