using System.Text.Json.Serialization;

namespace LightConvert.WinUI;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(SmokeReport))]
[JsonSerializable(typeof(InteractionReport))]
[JsonSerializable(typeof(CacheReport))]
internal partial class AppJsonContext : JsonSerializerContext { }

internal sealed class SmokeReport
{
    public string engine { get; set; } = "";
    public int count { get; set; }
    public int thumbnails { get; set; }
    public uint dpi { get; set; }
    public int dpiAwareness { get; set; }
    public bool perMonitorV2 { get; set; }
    public double rasterizationScale { get; set; }
    public bool settingsPersisted { get; set; }
    public bool navigationWorks { get; set; }
    public bool presetWorks { get; set; }
    public bool recursiveWorks { get; set; }
    public bool outputSettingWorks { get; set; }
    public bool winrtDropPayloadImported { get; set; }
    public bool duplicateDropIgnored { get; set; }
    public int completed { get; set; }
    public int progress100 { get; set; }
    public double totalPercent { get; set; }
    public string status { get; set; } = "";
    public FileError[] errors { get; set; } = [];
}
internal sealed record FileError(string Name, string? Error);
internal sealed record CacheReport(int files, int visible, int retainedThumbnails, bool offscreenReleased, bool visibleReady, bool passed);
internal sealed class InteractionReport
{
    public double settingsOffset { get; set; }
    public double settingsScrollable { get; set; }
    public double previewOffset { get; set; }
    public double previewScrollable { get; set; }
    public double? queueOffset { get; set; }
    public double? queueScrollable { get; set; }
    public bool settingsVisible { get; set; }
    public uint dpi { get; set; }
    public double scale { get; set; }
}
