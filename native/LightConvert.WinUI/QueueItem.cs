using System.ComponentModel;
using System.Runtime.CompilerServices;
using LightConvert.Core;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LightConvert.WinUI;

public sealed class QueueItem(string path, string? importedFolder = null) : INotifyPropertyChanged
{
    public string? ImportedFolder { get; } = importedFolder;
    public string FolderLabel => ImportedFolder is null ? Description : Path.Combine(Path.GetFileName(Path.TrimEndingDirectorySeparator(ImportedFolder)), Path.GetRelativePath(ImportedFolder, Source));
    public string Source { get; } = path;
    public string Name => Path.GetFileName(Source);
    public long InputBytes { get; } = new FileInfo(path).Length;
    public string Description => $"{Path.GetExtension(Source).TrimStart('.').ToUpperInvariant()}  →  {(MediaConverter.IsVideo(Source) ? "MP4" : "JPG")}     ·     {MediaConverter.FormatSize(InputBytes)}";
    public string KindGlyph => MediaConverter.IsVideo(Source) ? "\uE714" : "\uEB9F";
    private BitmapImage? thumbnail;
    public BitmapImage? Thumbnail { get => thumbnail; set => Set(ref thumbnail, value); }
    private double percent;
    public double Percent { get => percent; set => Set(ref percent, value); }
    private string status = "等待转换";
    public string Status { get => status; set => Set(ref status, value); }
    private string result = "";
    public string Result { get => result; set => Set(ref result, value); }
    public string? OutputPath { get; set; }
    public string? Error { get; set; }
    public bool PreviewUnavailable { get; set; }
    public bool ThumbnailGenerated { get; set; }
    public bool Completed => OutputPath is not null;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
    }
}
