using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace LightConvert.WinUI;

public sealed partial class MainWindow
{
    private void GoToConversion_Click(object sender, RoutedEventArgs e) => NavigateToPage("convert");
    private void Library_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView { SelectedItem: QueueItem item }) FileList.SelectedItem = item;
    }
    private void HelpPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ScrollViewer { Content: FrameworkElement content } viewer && viewer.ActualWidth > 0)
            content.Width = Math.Clamp(viewer.ActualWidth - viewer.Padding.Left - viewer.Padding.Right, 0, 820);
    }

    private void AppShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs e)
    {
        if (sender.Key == VirtualKey.F1) Help_Click(this, new RoutedEventArgs());
        else if ((sender.Modifiers & VirtualKeyModifiers.Shift) != 0) AddFolder_Click(this, new RoutedEventArgs());
        else AddFiles_Click(this, new RoutedEventArgs());
        e.Handled = true;
    }
}
