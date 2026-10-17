using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LightConvert.WinUI;

public sealed partial class MainWindow
{
    private async Task VerifyThumbnailCacheAsync(string[] args)
    {
        string Value(string key) => args[Array.IndexOf(args, key) + 1];
        string report = Value("--report");
        try
        {
            await ImportPathsAsync([Value("--fixtures")]);
            async Task WaitVisible()
            {
                var limit = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < limit)
                {
                    await Task.Delay(200);
                    if (visibleThumbnails.Count > 0 && visibleThumbnails.All(i => i.Thumbnail is not null || i.PreviewUnavailable)) return;
                }
            }
            await WaitVisible();
            FileList.SelectedItem = items.Last();
            FileList.ScrollIntoView(items.Last());
            await WaitVisible();
            bool released = items.Where(i => !visibleThumbnails.Contains(i)).All(i => i.Thumbnail is null);
            bool loaded = visibleThumbnails.Count > 0 && visibleThumbnails.All(i => i.Thumbnail is not null);
            int cached = items.Count(i => i.Thumbnail is not null);
            var result = new CacheReport(items.Count, visibleThumbnails.Count, cached, released, loaded,
                items.Count > visibleThumbnails.Count && released && loaded && cached <= visibleThumbnails.Count);
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(result, AppJsonContext.Default.CacheReport));
        }
        catch (Exception error) { await File.WriteAllTextAsync(report, error.ToString()); }
        finally { allowClose = true; Close(); }
    }
    private DispatcherTimer? interactionTimer;
    private void StartInteractionProbe(string reportPath)
    {
        interactionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        interactionTimer.Tick += (_, _) =>
        {
            try
            {
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(reportPath)!, "interaction.json"), JsonSerializer.Serialize(new InteractionReport
                { settingsOffset = SettingsPage.VerticalOffset, settingsScrollable = SettingsPage.ScrollableHeight, previewOffset = PreviewScroll.VerticalOffset, previewScrollable = PreviewScroll.ScrollableHeight,
                    queueOffset = QueueScroller(FileList)?.VerticalOffset, queueScrollable = QueueScroller(FileList)?.ScrollableHeight,
                    settingsVisible = SettingsPage.Visibility == Visibility.Visible, dpi = GetDpiForWindow(Handle), scale = Root.XamlRoot.RasterizationScale }, AppJsonContext.Default.InteractionReport));
            }
            catch (IOException) { }
        };
        interactionTimer.Start();
        Closed += (_, _) => interactionTimer.Stop();
    }
    private static ScrollViewer? QueueScroller(DependencyObject parent)
    {
        if (parent is ScrollViewer viewer) return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (QueueScroller(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }
    private async Task<bool> VerifyFolderSettingsAsync(string reportPath)
    {
        var saved = items.ToArray();
        var settings = preferences;
        var source = saved.FirstOrDefault(i => !LightConvert.Core.MediaConverter.IsVideo(i.Source));
        if (source is null) return false;
        string folder = Path.Combine(Path.GetDirectoryName(reportPath)!, "recursive-probe");
        Directory.CreateDirectory(Path.Combine(folder, "child"));
        File.Copy(source.Source, Path.Combine(folder, "root" + Path.GetExtension(source.Source)), true);
        File.Copy(source.Source, Path.Combine(folder, "child", "nested" + Path.GetExtension(source.Source)), true);
        try
        {
            items.Clear();
            preferences = settings with { RecursiveFolders = false, ShowThumbnails = false };
            await ImportPathsAsync([folder]);
            bool topOnly = items.Count == 1;
            items.Clear();
            preferences = settings with { RecursiveFolders = true, ShowThumbnails = false };
            await ImportPathsAsync([folder]);
            return topOnly && items.Count == 2;
        }
        finally
        {
            items.Clear();
            foreach (var item in saved) items.Add(item);
            preferences = settings;
            ApplySettings();
            UpdateQueue();
            if (items.Count > 0) FileList.SelectedIndex = 0;
        }
    }
}
