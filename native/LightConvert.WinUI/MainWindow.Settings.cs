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
        CaptureTimeToggle.IsOn = preferences.PreserveCaptureTime;
        FolderStructureToggle.IsOn = preferences.PreserveFolderStructure;
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
        UpdateOutputStructure();
    }
    private void UpdateOutputStructure()
    {
        if (OutputStructureHint is null) return;
        try
        {
            var item = FileList.SelectedItem as QueueItem;
            var folder = item is null ? OutputBox.Text : LightConvert.Core.OutputLayout.Folder(OutputBox.Text, item.Source, item.ImportedFolder, preferences.PreserveFolderStructure);
            OutputStructureHint.Text = item?.ImportedFolder is not null && preferences.PreserveFolderStructure
                ? Path.GetRelativePath(OutputBox.Text, folder).Replace(Path.DirectorySeparatorChar, '／') : "";
            ToolTipService.SetToolTip(OutputStructureHint, folder);
        }
        catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException) { OutputStructureHint.Text = ""; }
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
            OpenFolderOnComplete = AutoOpenToggle.IsOn, ConfirmClose = ConfirmCloseToggle.IsOn,
            PreserveCaptureTime = CaptureTimeToggle.IsOn, PreserveFolderStructure = FolderStructureToggle.IsOn };
        UpdateOutputStructure();
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
        if (ready) NavigateToPage(args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "convert");
    }
    private void NavigateTo(bool settings) => NavigateToPage(settings ? "settings" : "convert");
    private void NavigateToPage(string page)
    {
        ConversionPage.Visibility = page == "convert" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        FilesPage.Visibility = page == "files" ? Visibility.Visible : Visibility.Collapsed;
        TasksPage.Visibility = page == "tasks" ? Visibility.Visible : Visibility.Collapsed;
        HelpPage.Visibility = page == "help" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "settings") UpdateSettingsLayout();
        TaskFooter.Visibility = page == "convert" ? Visibility.Visible : Visibility.Collapsed;
        Root.RowDefinitions[2].Height = new GridLength(page == "convert" ? 86 : 0);
        object selected = page switch { "settings" => Navigation.SettingsItem, "files" => MenuFilesNav, "tasks" => MenuTasksNav, "help" => MenuHelpNav, _ => ConvertNav };
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
    private void Help_Click(object sender, RoutedEventArgs e) => NavigateToPage("help");
    private void About_Click(object sender, RoutedEventArgs e) => NavigateToPage("help");
}
