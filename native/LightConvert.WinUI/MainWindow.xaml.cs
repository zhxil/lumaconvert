using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using LightConvert.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace LightConvert.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<QueueItem> items = [];
    private readonly MediaConverter converter = new(Path.Combine(AppContext.BaseDirectory, "tools"), Path.Combine(AppContext.BaseDirectory, "LightConvert.ImageWorker.exe"));
    private readonly SemaphoreSlim thumbnailSlots = new(1);
    private readonly HashSet<QueueItem> visibleThumbnails = [];
    private readonly HashSet<QueueItem> loadingThumbnails = [];
    private CancellationTokenSource? previewCancellation;
    private QueueItem? previewItem;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? conversionCancellation;
    private int presetIndex = 1;
    private bool busy, importing, closing, allowClose;
    private bool ready;
    private AppSettings preferences = new();
    private readonly bool smokeMode = Environment.GetCommandLineArgs().Contains("--smoke-test");
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LightConvert", "settings.json");
    private nint Handle => WindowNative.GetWindowHandle(this);

    public MainWindow()
    {
        InitializeComponent();
        FileList.ItemsSource = items;
        LibraryList.ItemsSource = items;
        TaskList.ItemsSource = items;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(Handle));
        var display = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        double scale = GetDpiForWindow(Handle) / 96.0;
        int width = Math.Min((int)(1240 * scale), display.WorkArea.Width - 48);
        int height = Math.Min((int)(860 * scale), display.WorkArea.Height - 48);
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(display.WorkArea.X + (display.WorkArea.Width - width) / 2,
            display.WorkArea.Y + (display.WorkArea.Height - height) / 2, width, height));
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Math.Min((int)(940 * scale), display.WorkArea.Width - 48);
            presenter.PreferredMinimumHeight = Math.Min((int)(640 * scale), display.WorkArea.Height - 48);
        }
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "LightConvert.ico");
        if (File.Exists(icon)) appWindow.SetIcon(icon);
        appWindow.Closing += OnClosing;
        Closed += (_, _) => { lifetime.Cancel(); SaveSettings(); };
        OutputBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "轻转输出");
        if (!smokeMode) LoadSettings();
        ApplySettings();
        ready = true;
        Root.AddHandler(UIElement.PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(OnPointerWheelFallback), true);
        Navigation.SelectedItem = ConvertNav;
        if (Navigation.SettingsItem is NavigationViewItem settingsItem) settingsItem.Content = "设置";
        UpdatePresetHint();
        UpdateOutputSummary();
        StartButton.IsEnabled = false;
        // Explicit command-line automation is limited to test fixtures supplied by the caller.
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("--smoke-test")) Root.Loaded += async (_, _) =>
        {
            if (args.Contains("--cache-check")) await VerifyThumbnailCacheAsync(args);
            else await RunSmokeTestAsync(args);
        };
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        if (busy || importing) return;
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            foreach (var extension in MediaConverter.Extensions) picker.FileTypeFilter.Add(extension);
            InitializeWithWindow.Initialize(picker, Handle);
            var files = await picker.PickMultipleFilesAsync();
            await ImportPathsAsync(files.Select(f => f.Path));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await ShowErrorAsync("无法添加文件", ex.Message); }
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        if (busy || importing) return;
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, Handle);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null) await ImportPathsAsync([folder.Path]);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await ShowErrorAsync("无法添加文件夹", ex.Message); }
    }

    private async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        if (busy || importing) return;
        importing = true;
        UpdateControls();
        SummaryText.Text = "正在读取文件列表…";
        try
        {
            var inputs = paths.ToArray();
            var output = Path.GetFullPath(OutputBox.Text);
            bool recursive = preferences.RecursiveFolders;
            var discovered = await Task.Run(() =>
            {
                var found = new List<(string Source, string? Root)>();
                var warnings = new List<string>();
                void Visit(string path, string? importRoot)
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    try
                    {
                        path = Path.GetFullPath(path);
                        if (File.Exists(path))
                        {
                            if (MediaConverter.Extensions.Contains(Path.GetExtension(path))) found.Add((path, importRoot));
                            return;
                        }
                        if (!Directory.Exists(path)) return;
                        if (string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), output.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return;
                        if ((File.GetAttributes(path) & System.IO.FileAttributes.ReparsePoint) != 0) return;
                        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
                            if (recursive || File.Exists(entry)) Visit(entry, importRoot);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warnings.Add(Path.GetFileName(path)); }
                }
                foreach (var path in inputs) Visit(path, Directory.Exists(path) ? Path.GetFullPath(path) : null);
                return (found, warnings);
            }, lifetime.Token);
            var known = items.Select(i => i.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (var found in discovered.found)
            {
                var path = found.Source;
                if (!known.Add(path)) continue;
                try
                {
                    var item = new QueueItem(path, found.Root);
                    items.Add(item);
                    added++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { discovered.warnings.Add(Path.GetFileName(path)); }
            }
            UpdateQueue();
            if (FileList.SelectedItem is null && items.Count > 0) FileList.SelectedIndex = 0;
            SummaryText.Text = $"已添加 {added} 个文件" + (discovered.warnings.Count > 0 ? $" · {discovered.warnings.Count} 项无法读取" : added == 0 ? " · 已跳过重复或不支持的文件" : " · 可以开始转换");
        }
        finally { importing = false; UpdateControls(); }
    }

    private async Task LoadThumbnailAsync(QueueItem item)
    {
        if (item.Thumbnail is not null || !loadingThumbnails.Add(item)) return;
        bool acquired = false;
        try
        {
            await thumbnailSlots.WaitAsync(lifetime.Token);
            acquired = true;
            if (!items.Contains(item) || !preferences.ShowThumbnails || (!smokeMode && !visibleThumbnails.Contains(item))) return;
            var bytes = await converter.CreateThumbnailAsync(item.Source, lifetime.Token, 128);
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            { writer.WriteBytes(bytes); await writer.StoreAsync(); await writer.FlushAsync(); }
            stream.Seek(0);
            var image = new BitmapImage { DecodePixelWidth = 128 };
            await image.SetSourceAsync(stream);
            if (!items.Contains(item) || lifetime.IsCancellationRequested || !preferences.ShowThumbnails) return;
            item.ThumbnailGenerated = true;
            if (visibleThumbnails.Contains(item)) item.Thumbnail = image;
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (items.Contains(item) && !lifetime.IsCancellationRequested)
            {
                item.PreviewUnavailable = true;
                if (ReferenceEquals(FileList.SelectedItem, item)) UpdatePreview(item);
            }
        }
        finally { loadingThumbnails.Remove(item); if (acquired) thumbnailSlots.Release(); }
    }

    private void FileList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not QueueItem item) return;
        if (args.InRecycleQueue)
        {
            visibleThumbnails.Remove(item);
            item.Thumbnail = null;
        }
        else
        {
            visibleThumbnails.Add(item);
            if (preferences.ShowThumbnails) _ = LoadThumbnailAsync(item);
        }
    }

    private async Task LoadSelectedPreviewAsync(QueueItem item, CancellationToken token)
    {
        try
        {
            var bytes = await converter.CreateThumbnailAsync(item.Source, token);
            token.ThrowIfCancellationRequested();
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            { writer.WriteBytes(bytes); await writer.StoreAsync(); await writer.FlushAsync(); }
            stream.Seek(0);
            var image = new BitmapImage { DecodePixelWidth = 640 };
            await image.SetSourceAsync(stream);
            if (token.IsCancellationRequested || !ReferenceEquals(FileList.SelectedItem, item)) return;
            SelectedPreview.Source = image;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!token.IsCancellationRequested) PreviewHint.Text = "预览不可用"; }
    }

    private void UpdateQueue()
    {
        CountText.Text = $"转换队列 · {items.Count} 个文件 · {MediaConverter.FormatSize(items.Sum(i => i.InputBytes))}";
        FileList.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        if (items.Count == 0) UpdatePreview(null);
        UpdateControls();
    }
    private void UpdateControls()
    {
        bool editable = !busy && !importing;
        AddFilesButton.IsEnabled = AddFolderButton.IsEnabled = QueueActionsButton.IsEnabled = editable;
        MenuAddFiles.IsEnabled = MenuAddFolder.IsEnabled = MenuClear.IsEnabled = MenuRemove.IsEnabled = MenuRemoveCompleted.IsEnabled = MenuRetry.IsEnabled = editable;
        OutputBox.IsEnabled = ChooseFolderButton.IsEnabled = editable;
        SessionPresetBox.IsEnabled = DefaultPresetBox.IsEnabled = RecursiveToggle.IsEnabled = ThumbnailsToggle.IsEnabled = AutoOpenToggle.IsEnabled = ConfirmCloseToggle.IsEnabled = editable;
        SettingsBody.IsHitTestVisible = editable;
        StartButton.IsEnabled = editable && items.Any(i => !i.Completed);
        CancelButton.IsEnabled = busy && conversionCancellation?.IsCancellationRequested == false;
    }
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (busy || importing) return;
        if (FileList.SelectedItem is QueueItem item) items.Remove(item);
        UpdateQueue();
        if (items.Count > 0) FileList.SelectedIndex = 0;
    }
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (busy || importing) return;
        items.Clear(); UpdateQueue();
        TotalProgress.Value = 0; TotalPercentText.Text = "0%";
        SummaryText.Text = "准备好，就开始吧";
    }
    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview(FileList.SelectedItem as QueueItem);
    private void UpdatePreview(QueueItem? item)
    {
        CompareButton.IsEnabled = item?.OutputPath is not null;
        CompareSummary.Text = item?.Result ?? "";
        UpdateOutputStructure();
        if (!ReferenceEquals(previewItem, item) || !preferences.ShowThumbnails)
        {
            previewCancellation?.Cancel(); previewCancellation?.Dispose();
            previewCancellation = null;
            previewItem = item;
            if (item is not null && preferences.ShowThumbnails && !busy)
            {
                previewCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                _ = LoadSelectedPreviewAsync(item, previewCancellation.Token);
            }
        }
        SelectedPreview.Source = item?.Thumbnail;
        PreviewPlaceholder.Visibility = item?.Thumbnail is null ? Visibility.Visible : Visibility.Collapsed;
        PreviewHint.Text = item?.PreviewUnavailable == true ? "预览不可用，仍可尝试转换" : item is not null ? "正在生成预览…" : "选择文件查看预览";
        PreviewName.Text = item?.Name ?? "照片与视频，只留在你的电脑里。";
        PreviewKind.Text = item is null ? "本地处理" : MediaConverter.IsVideo(item.Source) ? "视频首帧" : "图片预览";
        ToolTipService.SetToolTip(PreviewName, item?.Error ?? item?.Source);
        SelectedErrorBar.IsOpen = item?.Error is not null;
        SelectedErrorBar.Message = item?.Error ?? "";
        if (!preferences.ShowThumbnails) PreviewHint.Text = "缩略图已在设置中关闭";
    }
    private void SessionPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        presetIndex = Math.Max(0, SessionPresetBox.SelectedIndex);
        UpdatePresetHint();
    }

    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, Handle);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null) OutputBox.Text = folder.Path;
        }
        catch (Exception ex) { await ShowErrorAsync("无法选择目录", ex.Message); }
    }
    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Path.GetFullPath(OutputBox.Text);
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = true });
        }
        catch (Exception ex) { await ShowErrorAsync("无法打开目录", ex.Message); }
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        try { await StartBatchAsync(); }
        catch (Exception ex) { await ShowErrorAsync("无法开始转换", ex.Message); }
    }
    private async Task StartBatchAsync(bool failedOnly = false)
    {
        if (busy || importing) return;
        NavigateTo(false);
        string folder = Path.GetFullPath(OutputBox.Text.Trim());
        var pending = items.Where(i => !i.Completed && (!failedOnly || i.Error is not null)).ToArray();
        if (pending.Length == 0) return;
        Directory.CreateDirectory(folder);
        // Probe write access before disabling controls or starting expensive decodes.
        string probe = Path.Combine(folder, ".lightconvert-write-" + Guid.NewGuid().ToString("N"));
        using (File.Create(probe)) { } File.Delete(probe);
        busy = true;
        conversionCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = conversionCancellation.Token;
        var preset = CompressionPreset.All[presetIndex];
        UpdateControls(); SaveSettings();
        int succeeded = 0, failed = 0;
        long before = 0, after = 0;
        try
        {
            for (int index = 0; index < pending.Length; index++)
            {
                if (token.IsCancellationRequested) break;
                var item = pending[index];
                item.Percent = 0; item.Result = ""; item.Error = null;
                if (ConversionPage.Visibility == Visibility.Visible)
                {
                    FileList.SelectedItem = item;
                    FileList.ScrollIntoView(item);
                }
                int currentIndex = index;
                SummaryText.Text = $"正在转换 {index + 1} / {pending.Length} · {item.Name}";
                var progress = new DispatchProgress(value =>
                {
                    if (!busy || token.IsCancellationRequested || item.Completed || item.Error is not null) return;
                    item.Percent = value.Percent;
                    item.Status = $"{value.Stage} · {value.Percent:0}%";
                    SetTotal((currentIndex + value.Percent / 100) / pending.Length * 100);
                }, DispatcherQueue);
                try
                {
                    var destination = OutputLayout.Folder(folder, item.Source, item.ImportedFolder, preferences.PreserveFolderStructure);
                    var result = await converter.ConvertAsync(item.Source, destination, preset, progress, token, preferences.PreserveCaptureTime);
                    item.OutputPath = result.OutputPath; item.Percent = 100; item.Status = "转换完成";
                    if (ReferenceEquals(FileList.SelectedItem, item)) UpdatePreview(item);
                    double delta = result.InputBytes == 0 ? 0 : (1 - (double)result.OutputBytes / result.InputBytes) * 100;
                    item.Result = $"{MediaConverter.FormatSize(result.OutputBytes)} · {(delta >= 0 ? "减小" : "增大")} {Math.Abs(delta):0}%";
                    succeeded++; before += result.InputBytes; after += result.OutputBytes;
                }
                catch (OperationCanceledException) { item.Status = "已取消，可重新开始"; item.Percent = 0; break; }
                catch (Exception ex)
                {
                    failed++; item.Status = "转换失败 · 选中查看原因"; item.Result = "未输出"; item.Error = ex.Message;
                    ToolTipService.SetToolTip(PreviewName, ex.Message);
                    ShowSelectedError(item);
                }
                SetTotal((index + 1.0) / pending.Length * 100);
            }
            string summary = $"{(token.IsCancellationRequested ? "已取消" : "处理结束")} · 成功 {succeeded} 个" + (failed > 0 ? $"，失败 {failed} 个" : "");
            if (before > 0) summary += $" · {MediaConverter.FormatSize(before)} → {MediaConverter.FormatSize(after)}";
            SummaryText.Text = summary;
            if (!smokeMode && !token.IsCancellationRequested && failed == 0 && succeeded > 0 && preferences.OpenFolderOnComplete)
                OpenFolder_Click(this, new RoutedEventArgs());
        }
        finally
        {
            busy = false;
            previewItem = null;
            UpdatePreview(FileList.SelectedItem as QueueItem);
            conversionCancellation.Dispose(); conversionCancellation = null;
            UpdateControls();
            if (closing) { allowClose = true; Close(); }
        }
    }

    private void SetTotal(double value) { TotalProgress.Value = value; TotalPercentText.Text = $"{value:0}%"; }
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        conversionCancellation?.Cancel(); CancelButton.IsEnabled = false; SummaryText.Text = "正在停止，清理当前临时文件…";
    }
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = !busy && !importing && e.DataView.Contains(StandardDataFormats.StorageItems) ? DataPackageOperation.Copy : DataPackageOperation.None;
        e.DragUIOverride.Caption = "添加到轻转队列";
        e.DragUIOverride.IsCaptionVisible = true;
        e.Handled = true;
    }
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (busy || importing || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var deferral = e.GetDeferral();
        try { await ImportDataAsync(e.DataView); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await ShowErrorAsync("无法读取拖入的文件", ex.Message); }
        finally { deferral.Complete(); }
    }
    private async Task ImportDataAsync(DataPackageView view) => await ImportPathsAsync((await view.GetStorageItemsAsync()).Select(i => i.Path));
    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (allowClose || !busy) return;
        args.Cancel = true;
        if (closing) return;
        if (!preferences.ConfirmClose) { closing = true; conversionCancellation?.Cancel(); return; }
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "转换仍在进行", Content = "关闭窗口会取消当前转换，已完成的文件会保留。", PrimaryButtonText = "停止并关闭", CloseButtonText = "继续转换", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            closing = true;
            if (busy) conversionCancellation?.Cancel();
            else { allowClose = true; Close(); }
        }
    }
    private async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = title, Content = message, CloseButtonText = "知道了" };
        await dialog.ShowAsync();
    }
    private void ShowSelectedError(QueueItem item)
    {
        if (ReferenceEquals(FileList.SelectedItem, item) && item.Error is not null)
        {
            SelectedErrorBar.Message = item.Error;
            SelectedErrorBar.IsOpen = true;
            ToolTipService.SetToolTip(PreviewName, item.Error);
        }
    }
    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(settingsPath)) return;
            preferences = AppSettings.Load(settingsPath);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
    }
    private void SaveSettings()
    {
        if (smokeMode) return;
        try
        {
            preferences.Save(settingsPath);
            SettingsNotice.IsOpen = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { SettingsNotice.Message = "设置暂未保存：" + ex.Message; SettingsNotice.IsOpen = true; }
    }
    private sealed class DispatchProgress(Action<ConversionProgress> action, Microsoft.UI.Dispatching.DispatcherQueue queue) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) => queue.TryEnqueue(() => action(value));
    }

    private async Task RunSmokeTestAsync(string[] args)
    {
        string Value(string key) => args[Array.IndexOf(args, key) + 1];
        string reportPath = Value("--report");
        try
        {
            preferences = preferences with { OutputFolder = Value("--output") };
            OutputBox.Text = Value("--output");
            var package = new DataPackage();
            package.SetStorageItems([await StorageFolder.GetFolderFromPathAsync(Value("--fixtures"))]);
            await ImportDataAsync(package.GetView());
            int firstImport = items.Count;
            await ImportDataAsync(package.GetView());
            foreach (var item in items) await LoadThumbnailAsync(item);
            var timeout = DateTime.UtcNow.AddSeconds(30);
            while (items.Any(i => !i.ThumbnailGenerated && !i.PreviewUnavailable) && DateTime.UtcNow < timeout) await Task.Delay(100);
            int thumbnails = items.Count(i => i.ThumbnailGenerated);
            await StartBatchAsync();
            string completedSummary = SummaryText.Text;
            var originalPreferences = preferences;
            string settingsProbePath = Path.Combine(Path.GetDirectoryName(reportPath)!, "settings-roundtrip.json");
            var probeSettings = preferences with { Preset = 2, RecursiveFolders = false, ShowThumbnails = false, OpenFolderOnComplete = true, ConfirmClose = false };
            probeSettings.Save(settingsProbePath);
            bool settingsPersisted = AppSettings.Load(settingsProbePath) == probeSettings;
            NavigateTo(true);
            await Task.Delay(80);
            bool navigationWorks = SettingsPage.Visibility == Visibility.Visible && ConversionPage.Visibility == Visibility.Collapsed;
            string changedOutput = Path.Combine(Path.GetDirectoryName(reportPath)!, "settings-output-probe");
            OutputBox.Text = changedOutput;
            await Task.Delay(80);
            bool outputSettingWorks = preferences.OutputFolder == changedOutput;
            DefaultPresetBox.SelectedIndex = 2;
            bool presetWorks = presetIndex == 2 && preferences.Preset == 2;
            preferences = originalPreferences;
            ApplySettings();
            NavigateTo(false);
            navigationWorks &= ConversionPage.Visibility == Visibility.Visible && TaskFooter.Visibility == Visibility.Visible;
            bool recursiveWorks = await VerifyFolderSettingsAsync(reportPath);
            SummaryText.Text = completedSummary;
            var report = new SmokeReport { engine = "C# + WinUI 3", count = items.Count, thumbnails = thumbnails,
                dpi = GetDpiForWindow(Handle), dpiAwareness = GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(Handle)),
                perMonitorV2 = AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(Handle), new nint(-4)),
                rasterizationScale = Root.XamlRoot.RasterizationScale,
                settingsPersisted = settingsPersisted, navigationWorks = navigationWorks, presetWorks = presetWorks, recursiveWorks = recursiveWorks, outputSettingWorks = outputSettingWorks,
                winrtDropPayloadImported = firstImport > 0, duplicateDropIgnored = firstImport == items.Count,
                completed = items.Count(i => i.Completed), progress100 = items.Count(i => i.Percent == 100),
                totalPercent = TotalProgress.Value, status = SummaryText.Text,
                errors = items.Where(i => i.Error is not null).Select(i => new FileError(i.Name, i.Error)).ToArray() };
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, AppJsonContext.Default.SmokeReport));
            if (args.Contains("--show-settings")) NavigateTo(true);
            if (args.Contains("--wheel-test")) StartInteractionProbe(reportPath);
            if (!args.Contains("--keep-open")) { allowClose = true; Close(); }
        }
        catch (Exception ex) { await File.WriteAllTextAsync(reportPath, ex.ToString()); allowClose = true; Close(); }
    }
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")]
    private static extern nint GetWindowDpiAwarenessContext(nint hwnd);
    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(nint context);
    [DllImport("user32.dll")]
    private static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);
}
