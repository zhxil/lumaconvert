using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace LightConvert.WinUI;

public sealed partial class MainWindow
{
    // Native scroll viewers handle the normal route. Only consume otherwise-unhandled wheels.
    private void OnPointerWheelFallback(object sender, PointerRoutedEventArgs e)
    {
        if (e.Handled) return;
        var point = e.GetCurrentPoint(Root);
        if (point.Properties.IsHorizontalMouseWheel) return;
        ScrollViewer? viewer = null;
        for (var node = e.OriginalSource as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is ScrollViewer candidate && candidate.ScrollableHeight > 0) { viewer = candidate; break; }
        viewer ??= SettingsPage.Visibility == Visibility.Visible ? SettingsPage : null;
        if (viewer is null || viewer.ScrollableHeight <= 0) return;
        var offset = Math.Clamp(viewer.VerticalOffset - point.Properties.MouseWheelDelta / 120.0 * 72, 0, viewer.ScrollableHeight);
        if (Math.Abs(offset - viewer.VerticalOffset) < .1) return;
        viewer.ChangeView(null, offset, null, true);
        e.Handled = true;
    }
}
