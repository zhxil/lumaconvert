using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.System;

namespace LightConvert.WinUI;

public sealed partial class MainWindow
{
    private async void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not QueueItem { OutputPath: not null } item) return;
        try
        {
            double width = Math.Clamp(Root.ActualWidth - 140, 400, 900);
            using var view = new ComparisonView(converter, item, width - 48, Root.ActualHeight - 400);
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "压缩前后", Content = view,
                CloseButtonText = "完成", SecondaryButtonText = "打开结果", DefaultButton = ContentDialogButton.Close };
            dialog.Resources["ContentDialogMaxWidth"] = width;
            dialog.Opened += async (_, _) => await view.InitializeAsync();
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Secondary) await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(item.OutputPath));
        }
        catch (Exception error) { await ShowErrorAsync("无法对比", error.Message); }
    }
}
