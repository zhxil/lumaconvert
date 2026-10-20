using System.Text.Json;

namespace LightConvert.WinUI;

public sealed record AppSettings
{
    public string OutputFolder { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "轻转输出");
    public int Preset { get; init; } = 1;
    public bool RecursiveFolders { get; init; } = true;
    public bool ShowThumbnails { get; init; } = true;
    public bool OpenFolderOnComplete { get; init; }
    public bool ConfirmClose { get; init; } = true;
    public bool PreserveCaptureTime { get; init; } = true;
    public bool PreserveFolderStructure { get; init; } = true;
    public static AppSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        var settings = JsonSerializer.Deserialize(File.ReadAllText(path), AppJsonContext.Default.AppSettings) ?? new();
        if (string.IsNullOrWhiteSpace(settings.OutputFolder) || !Path.IsPathFullyQualified(settings.OutputFolder))
            settings = settings with { OutputFolder = new AppSettings().OutputFolder };
        return settings with { Preset = Math.Clamp(settings.Preset, 0, 2) };
    }
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(this, AppJsonContext.Default.AppSettings)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
