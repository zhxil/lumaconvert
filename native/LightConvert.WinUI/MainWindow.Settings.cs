using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LightConvert.WinUI;

public sealed partial class MainWindow
{
    private void SettingsPage_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSettingsLayout();

    private void UpdateSettingsLayout()
    {
        if (SettingsPage.ActualWidth <= 0) return;
        // ActualWidth is already in logical pixels. Applying the monitor DPI again
        // here would push the settings controls outside the scroll viewport.
        SettingsContent.Width = Math.Clamp(SettingsPage.ActualWidth - SettingsPage.Padding.Left - SettingsPage.Padding.Right, 0, 820);
    }
    private void ApplySettings()
    {
        bool wasReady = ready;
        ready = false;
        OutputBox.Text = preferences.OutputFolder;
        DefaultPresetBox.SelectedIndex = preferences.Preset;
        SessionPresetBox.SelectedIndex = preferences.Preset;
        presetIndex = preferences.Preset;
        RecursiveToggle.IsOn = preferences.RecursiveFolders;
        ThumbnailsToggle.IsOn = preferences.ShowThumbnails;
        AutoOpenToggle.IsOn = preferences.OpenFolderOnComplete;
        ConfirmCloseToggle.IsOn = preferences.ConfirmClose;
        ready = wasReady;
        UpdateOutputSummary();
        UpdatePresetHint();
    }
    private void UpdatePresetHint()
    {
        if (PresetHint is null) return;
        PresetHint.Text = presetIndex switch
        {
            0 => "原分辨率 · 优先保留细节",
            2 => "图片最长边 2560 · 视频最长边 1280",
            _ => "图片最长边 3840 · 视频最长边 1920"
        };
        ToolTipService.SetToolTip(SessionPresetBox, PresetHint.Text);
    }
    private void UpdateOutputSummary()
    {
        if (OutputSummary is null) return;
        string folderName = Path.GetFileName(OutputBox.Text.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        OutputSummary.Text = string.IsNullOrEmpty(folderName) ? OutputBox.Text : folderName;
        ToolTipService.SetToolTip(OutputSummary, OutputBox.Text);
    }
    private void OutputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!ready) return;
        UpdateOutputSummary();
        if (!Path.IsPathFullyQualified(OutputBox.Text.Trim()))
        {
            SettingsNotice.Message = "请输入完整的输出目录，或点击浏览选择。";
            SettingsNotice.IsOpen = true;
            return;
        }
        preferences = preferences with { OutputFolder = OutputBox.Text.Trim() };
        SaveSettings();
    }
    private void DefaultPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        preferences = preferences with { Preset = Math.Clamp(DefaultPresetBox.SelectedIndex, 0, 2) };
        SessionPresetBox.SelectedIndex = preferences.Preset;
        SaveSettings();
    }
    private void Setting_Toggled(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        bool changed = preferences.ShowThumbnails != ThumbnailsToggle.IsOn;
        preferences = preferences with { RecursiveFolders = RecursiveToggle.IsOn, ShowThumbnails = ThumbnailsToggle.IsOn,
            OpenFolderOnComplete = AutoOpenToggle.IsOn, ConfirmClose = ConfirmCloseToggle.IsOn };
        if (changed)
        {
            foreach (var item in items)
            {
                if (!preferences.ShowThumbnails) item.Thumbnail = null;
                else { item.PreviewUnavailable = false; if (visibleThumbnails.Contains(item)) _ = LoadThumbnailAsync(item); }
            }
            previewItem = null;
            UpdatePreview(FileList.SelectedItem as QueueItem);
        }
        SaveSettings();
    }
    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (ready) NavigateTo(args.IsSettingsSelected);
    }
    private void NavigateTo(bool settings)
    {
        ConversionPage.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        SettingsPage.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        if (settings) UpdateSettingsLayout();
        TaskFooter.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        Root.RowDefinitions[3].Height = new GridLength(settings ? 0 : 86);
        var selected = settings ? Navigation.SettingsItem : ConvertNav;
        if (!ReferenceEquals(Navigation.SelectedItem, selected)) Navigation.SelectedItem = selected;
    }
    private void ShowSettings_Click(object sender, RoutedEventArgs e) => NavigateTo(true);
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void RemoveCompleted_Click(object sender, RoutedEventArgs e)
    {
        if (busy || importing) return;
        foreach (var item in items.Where(i => i.Completed).ToArray()) items.Remove(item);
        UpdateQueue();
    }
    private async void RetryFailed_Click(object sender, RoutedEventArgs e)
    {
        if (busy || importing) return;
        try { await StartBatchAsync(true); }
        catch (Exception ex) { await ShowErrorAsync("无法重试", ex.Message); }
    }
    private async void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        if (busy || importing) return;
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "恢复默认设置？", Content = "会恢复输出目录、默认压缩方式与其他偏好。队列和文件不会改变。", PrimaryButtonText = "恢复默认", CloseButtonText = "取消" };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        { preferences = new(); ApplySettings(); SaveSettings(); }
    }
    private async void Help_Click(object sender, RoutedEventArgs e) => await ShowErrorAsync("使用说明",
        "1. 添加或拖入文件／文件夹。\n2. 选中队列项查看预览。\n3. 在右侧选择本次压缩方式，点击开始转换。\n\n设置页可更改默认输出目录、默认压缩、子文件夹扫描与缩略图等偏好。\n\n文件菜单：添加文件、添加文件夹、打开输出目录。\n任务菜单：移除选中、移除完成项、重试失败项、清空队列。\nCtrl+O 添加文件；Ctrl+Shift+O 添加文件夹；F1 打开说明。\n\n原文件始终保留。图片输出 JPG，视频输出 MP4。文件大小和色彩请以试转结果为准。");
    private async void About_Click(object sender, RoutedEventArgs e) => await ShowErrorAsync("轻转 2.2.1",
        "iPhone 图片与视频转换工具\n\nC# · WinUI 3 · 本地处理\n支持 HEIC / HEIF / JPG / PNG / MOV / MP4\n\n照片卡片与导出箭头构成应用图标。\n逐显示器 DPI 感知让文字在高分屏上保持清晰。\n\n第三方组件许可见程序目录 licenses 文件夹。");
}
