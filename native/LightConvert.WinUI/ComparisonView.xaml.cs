using LightConvert.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace LightConvert.WinUI;

public sealed partial class ComparisonView : UserControl, IDisposable
{
    private readonly MediaConverter converter;
    private readonly QueueItem item;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? frames;
    private readonly DispatcherTimer seekTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool ready;

    public ComparisonView(MediaConverter converter, QueueItem item, double width, double height = 380)
    {
        this.converter = converter; this.item = item;
        InitializeComponent();
        Stage.Width = Math.Max(320, width);
        Stage.Height = ZoomViewer.Height = Math.Clamp(height, 180, 420);
        FileNameText.Text = item.Name;
        BeforeText.Text = "原片 · " + MediaConverter.FormatSize(item.InputBytes);
        AfterText.Text = "转换后 · " + MediaConverter.FormatSize(new FileInfo(item.OutputPath!).Length);
        ToolTipService.SetToolTip(AfterText, item.Result);
        seekTimer.Tick += async (_, _) => { seekTimer.Stop(); await LoadFramesAsync(); };
    }

    public async Task InitializeAsync()
    {
        try
        {
            if (MediaConverter.IsVideo(item.Source))
            {
                var before = await converter.ProbeAsync(item.Source, lifetime.Token);
                var after = await converter.ProbeAsync(item.OutputPath!, lifetime.Token);
                TimeSlider.Maximum = Math.Max(0, Math.Min(before.Duration, after.Duration) - .1);
                VideoControls.Visibility = Visibility.Visible;
                ToolTipService.SetToolTip(VideoControls, "拖动查看同一时间点的前后帧");
            }
            ready = true;
            await LoadFramesAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { ErrorBar.Message = error.Message; ErrorBar.IsOpen = true; LoadingRing.IsActive = false; }
    }

    private async Task LoadFramesAsync()
    {
        frames?.Cancel(); frames?.Dispose();
        frames = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = frames.Token;
        double seconds = TimeSlider.Value;
        TimeText.Text = TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss") + " / " + TimeSpan.FromSeconds(TimeSlider.Maximum).ToString(@"mm\:ss");
        LoadingRing.IsActive = true; ErrorBar.IsOpen = false;
        BeforeImage.Opacity = AfterImage.Opacity = .35;
        try
        {
            var before = await converter.CreateThumbnailAsync(item.Source, token, 1536, seconds);
            var after = await converter.CreateThumbnailAsync(item.OutputPath!, token, 1536, seconds);
            var beforeBitmap = await BitmapAsync(before);
            var afterBitmap = await BitmapAsync(after);
            token.ThrowIfCancellationRequested();
            BeforeImage.Source = beforeBitmap; AfterImage.Source = afterBitmap;
            BeforeImage.Opacity = AfterImage.Opacity = 1;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!token.IsCancellationRequested) { ErrorBar.Message = "无法读取对比画面：" + error.Message; ErrorBar.IsOpen = true; } }
        finally { if (!token.IsCancellationRequested) LoadingRing.IsActive = false; }
    }

    private static async Task<BitmapImage> BitmapAsync(byte[] bytes)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        { writer.WriteBytes(bytes); await writer.StoreAsync(); await writer.FlushAsync(); }
        stream.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream); return bitmap;
    }
    private void Stage_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSplit();
    private void Split_Changed(object sender, RangeBaseValueChangedEventArgs e) => UpdateSplit();
    private void UpdateSplit()
    {
        if (BeforeClip is null || Stage is null || SplitSlider is null) return;
        double position = Stage.ActualWidth * SplitSlider.Value / 100;
        BeforeClip.Rect = new Rect(0, 0, position, Stage.ActualHeight);
        Divider.Margin = new Thickness(position, 0, 0, 0);
    }
    private void Time_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!ready) return;
        frames?.Cancel(); seekTimer.Stop(); seekTimer.Start();
    }
    private void Zoom_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ZoomViewer is null) return;
        float factor = ZoomBox.SelectedIndex switch { 1 => 2, 2 => 4, _ => 1 };
        ZoomViewer.ChangeView(0, 0, factor, true);
    }
    public void Dispose()
    {
        ready = false; seekTimer.Stop(); lifetime.Cancel(); frames?.Cancel();
        BeforeImage.Source = AfterImage.Source = null;
        frames?.Dispose(); lifetime.Dispose();
    }
}
