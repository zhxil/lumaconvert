using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ImageMagick;

namespace LightConvert.Core;

public sealed record CompressionPreset(string Name, int Quality, int Crf, uint PhotoEdge, int VideoEdge)
{
    public static readonly CompressionPreset[] All = [
        new("清晰优先", 92, 20, 0, 0), new("均衡压缩", 82, 25, 3840, 1920), new("体积优先", 65, 30, 2560, 1280)];
}

public sealed record ConversionProgress(double Percent, string Stage);
public sealed record ConversionResult(string OutputPath, long InputBytes, long OutputBytes);
public sealed record VideoInfo(int Width, int Height, double Duration, string Transfer, string CaptureTime = "");

public sealed class MediaConverter(string toolsDirectory, string? imageWorker = null)
{
    // Serialize expensive decoders so a background preview cannot overlap conversion.
    private static readonly SemaphoreSlim decoding = new(1);
    private static void ConfigureImageResources()
    {
        ResourceLimits.Memory = 128UL * 1024 * 1024;
        ResourceLimits.Thread = 2;
    }
    private static readonly string[] ToneMapFilters = ["zscale=t=linear:npl=100", "format=gbrpf32le", "zscale=p=bt709",
        "tonemap=tonemap=hable:desat=0", "zscale=t=bt709:m=bt709:r=tv"];
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".heic", ".heif", ".jpg", ".jpeg", ".png", ".mov", ".mp4" };
    public static bool IsVideo(string path) => new[] { ".mov", ".mp4" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public static string FormatSize(long bytes) => bytes >= 1048576 ? $"{bytes / 1048576.0:0.0} MB" : $"{bytes / 1024.0:0.#} KB";
    private string Tool(string name)
    {
        var path = Path.Combine(toolsDirectory, name + ".exe");
        return File.Exists(path) ? path : throw new FileNotFoundException("转换组件缺失，请重新解压完整便携版。", path);
    }

    public async Task<ConversionResult> ConvertAsync(string source, string folder, CompressionPreset preset,
        IProgress<ConversionProgress>? progress = null, CancellationToken cancellationToken = default, bool preserveCaptureTime = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        source = Path.GetFullPath(source);
        folder = Path.GetFullPath(folder);
        if (!Extensions.Contains(Path.GetExtension(source))) throw new NotSupportedException("不支持的文件格式");
        var inputBytes = new FileInfo(source).Length;
        Directory.CreateDirectory(folder);
        string suffix = IsVideo(source) ? ".mp4" : ".jpg";
        var temporary = Path.Combine(folder, ".lightconvert-" + Guid.NewGuid().ToString("N") + suffix);
        await decoding.WaitAsync(cancellationToken);
        try
        {
            DateTime? captured = null;
            if (IsVideo(source)) captured = await ConvertVideoAsync(source, temporary, preset, progress, cancellationToken, preserveCaptureTime);
            else if (imageWorker is not null)
            {
                progress?.Report(new(0, "处理图片"));
                var response = await RunCaptureAsync(imageWorker, ["convert", source, temporary, Array.IndexOf(CompressionPreset.All, preset).ToString(CultureInfo.InvariantCulture), preserveCaptureTime.ToString()], cancellationToken);
                if (DateTime.TryParse(response.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)) captured = date;
            }
            else captured = await Task.Run(() => ConvertPhoto(source, temporary, preset, progress, cancellationToken, preserveCaptureTime), cancellationToken);
            if (preserveCaptureTime)
            {
                File.SetCreationTimeUtc(temporary, captured?.ToUniversalTime() ?? File.GetCreationTimeUtc(source));
                File.SetLastWriteTimeUtc(temporary, captured?.ToUniversalTime() ?? File.GetLastWriteTimeUtc(source));
            }
            cancellationToken.ThrowIfCancellationRequested();
            var output = CommitOutput(temporary, folder, Path.GetFileNameWithoutExtension(source), suffix);
            var result = new ConversionResult(output, inputBytes, new FileInfo(output).Length);
            progress?.Report(new(100, "完成"));
            return result;
        }
        finally { decoding.Release(); if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // File.Move without overwrite is the final authority, including concurrent output name races.
    private static string CommitOutput(string temporary, string folder, string stem, string suffix)
    {
        for (int index = 0; ; index++)
        {
            string target = Path.Combine(folder, stem + (index == 0 ? "" : $"_{index}") + suffix);
            try { File.Move(temporary, target, false); return target; }
            catch (IOException) when (File.Exists(target) || Directory.Exists(target)) { }
        }
    }

    public static DateTime? ConvertPhoto(string source, string target, CompressionPreset preset,
        IProgress<ConversionProgress>? progress, CancellationToken token, bool preserveCaptureTime = true)
    {
        progress?.Report(new(0, "解码图片"));
        ConfigureImageResources();
        using var image = new MagickImage(source);
        var originalExif = image.GetExifProfile();
        var timeExif = new ExifProfile();
        ExifTag<string>[] dateTags = [ExifTag.DateTimeOriginal, ExifTag.DateTimeDigitized, ExifTag.DateTime,
            ExifTag.OffsetTimeOriginal, ExifTag.OffsetTimeDigitized, ExifTag.OffsetTime,
            ExifTag.SubsecTimeOriginal, ExifTag.SubsecTimeDigitized, ExifTag.SubsecTime];
        if (preserveCaptureTime && originalExif is not null)
            foreach (var tag in dateTags)
                if (originalExif.GetValue(tag) is { } value) timeExif.SetValue(tag, value.Value);
        DateTime? captured = null;
        foreach (var tag in dateTags.Take(3))
            if (timeExif.GetValue(tag) is { } value && DateTime.TryParseExact(value.Value, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) && parsed.Year >= 1601)
            {
                captured = parsed;
                var offsetTag = tag == ExifTag.DateTimeOriginal ? ExifTag.OffsetTimeOriginal : tag == ExifTag.DateTimeDigitized ? ExifTag.OffsetTimeDigitized : ExifTag.OffsetTime;
                if (timeExif.GetValue(offsetTag) is { } offset && DateTimeOffset.TryParse(parsed.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + offset.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var zoned)) captured = zoned.UtcDateTime;
                break;
            }
        token.ThrowIfCancellationRequested();
        progress?.Report(new(40, "校正方向"));
        image.AutoOrient();
        // Convert profile-bearing photos to sRGB before removing metadata.
        if (image.GetColorProfile() is not null) image.TransformColorSpace(ColorProfiles.SRGB);
        else image.ColorSpace = ColorSpace.sRGB;
        if (preset.PhotoEdge > 0 && Math.Max(image.Width, image.Height) > preset.PhotoEdge)
            image.Resize(new MagickGeometry(preset.PhotoEdge, preset.PhotoEdge) { Greater = true });
        token.ThrowIfCancellationRequested();
        progress?.Report(new(75, "压缩并写入"));
        image.BackgroundColor = MagickColors.White;
        image.Alpha(AlphaAction.Remove);
        image.Strip();
        if (preserveCaptureTime && timeExif.Values.Any()) image.SetProfile(timeExif);
        image.Quality = (uint)preset.Quality;
        image.Format = MagickFormat.Jpeg;
        image.Write(target);
        token.ThrowIfCancellationRequested();
        return captured;
    }

    public async Task<VideoInfo> ProbeAsync(string source, CancellationToken token = default)
    {
        var json = await RunCaptureAsync(Tool("ffprobe"), ["-v", "error", "-show_streams", "-show_format", "-of", "json", source], token);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var videos = root.GetProperty("streams").EnumerateArray().Where(s => s.GetProperty("codec_type").GetString() == "video").ToList();
        if (videos.Count == 0) throw new InvalidDataException("文件中没有视频轨道。");
        var video = videos[0];
        double duration = 0;
        if (root.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var time))
            double.TryParse(time.GetString(), CultureInfo.InvariantCulture, out duration);
        string capture = "";
        foreach (var container in new[] { root.TryGetProperty("format", out var f) ? f : default, video })
            if (container.ValueKind == JsonValueKind.Object && container.TryGetProperty("tags", out var tags) &&
                (tags.TryGetProperty("com.apple.quicktime.creationdate", out var stamp) || tags.TryGetProperty("creation_time", out stamp)) &&
                DateTimeOffset.TryParse(stamp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var stampDate) && stampDate.Year >= 1601)
            { capture = stampDate.ToString("O", CultureInfo.InvariantCulture); break; }
        return new(video.GetProperty("width").GetInt32(), video.GetProperty("height").GetInt32(), duration,
            video.TryGetProperty("color_transfer", out var transfer) ? transfer.GetString() ?? "" : "", capture);
    }

    private async Task<DateTime?> ConvertVideoAsync(string source, string output, CompressionPreset preset,
        IProgress<ConversionProgress>? progress, CancellationToken token, bool preserveCaptureTime)
    {
        progress?.Report(new(0, "读取视频"));
        var info = await ProbeAsync(source, token);
        int edge = preset.VideoEdge == 0 ? Math.Max(info.Width, info.Height) : preset.VideoEdge;
        var filters = new List<string>();
        if (info.Transfer is "smpte2084" or "arib-std-b67")
            filters.AddRange(ToneMapFilters);
        filters.Add($"scale=w='min(iw,{edge})':h='min(ih,{edge})':force_original_aspect_ratio=decrease:force_divisible_by=2");
        filters.Add("format=yuv420p");
        List<string> args = ["-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-threads", "4", "-filter_threads", "2", "-i", source,
            "-map", "0:v:0", "-map", "0:a:0?", "-map_metadata", "-1", "-map_metadata:s", "-1", "-vf", string.Join(',', filters),
            "-c:v", "libx264", "-threads", "4", "-preset", "medium", "-crf", preset.Crf.ToString(CultureInfo.InvariantCulture),
            "-x264-params", "rc-lookahead=16:sync-lookahead=0",
            "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart", "-progress", "pipe:1"];
        if (info.Transfer is "smpte2084" or "arib-std-b67")
            args.AddRange(["-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709"]);
        DateTime? captured = null;
        if (preserveCaptureTime && DateTimeOffset.TryParse(info.CaptureTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp))
        {
            var date = stamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
            args.AddRange(["-metadata", "creation_time=" + date, "-metadata:s:v:0", "creation_time=" + date]);
            captured = stamp.UtcDateTime;
        }
        args.Add(output);
        using var process = CreateProcess(Tool("ffmpeg"), args);
        process.Start();
        using var registration = token.Register(() => Kill(process));
        var errorTask = ReadErrorTailAsync(process.StandardError);
        string? line;
        try
        {
            while ((line = await process.StandardOutput.ReadLineAsync(token)) is not null)
            {
                if (line.StartsWith("out_time_us=", StringComparison.Ordinal) && info.Duration > 0 &&
                    double.TryParse(line.AsSpan(12), CultureInfo.InvariantCulture, out var microseconds))
                    progress?.Report(new(Math.Clamp(microseconds / 1e6 / info.Duration * 100, 0, 99), "转换视频"));
            }
            await process.WaitForExitAsync(token);
            var error = await errorTask;
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new InvalidDataException("视频转换失败：\n" + error);
        }
        finally
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await errorTask;
        }
        return captured;
    }

    public async Task<byte[]> CreateThumbnailAsync(string source, CancellationToken token = default, int edge = 640, double seconds = 0)
    {
        await decoding.WaitAsync(token);
        try { return await CreateThumbnailCoreAsync(source, token, Math.Clamp(edge, 64, 1536), Math.Max(0, seconds)); }
        finally { decoding.Release(); }
    }
    private async Task<byte[]> CreateThumbnailCoreAsync(string source, CancellationToken token, int edge, double seconds)
    {
        if (!IsVideo(source) && imageWorker is not null)
        {
            var path = Path.Combine(Path.GetTempPath(), "lightconvert-image-" + Guid.NewGuid().ToString("N") + ".jpg");
            try
            {
                await RunCaptureAsync(imageWorker, ["thumbnail", source, path, edge.ToString(CultureInfo.InvariantCulture)], token);
                return await File.ReadAllBytesAsync(path, token);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        if (!IsVideo(source))
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                ConfigureImageResources();
                using var image = new MagickImage(source);
                image.AutoOrient();
                if (image.GetColorProfile() is not null) image.TransformColorSpace(ColorProfiles.SRGB);
                image.Thumbnail((uint)edge, (uint)(edge * 3 / 4));
                token.ThrowIfCancellationRequested();
                return image.ToByteArray(edge > 640 ? MagickFormat.Png : MagickFormat.Jpeg);
            }, token);
        var preview = Path.Combine(Path.GetTempPath(), "lightconvert-preview-" + Guid.NewGuid().ToString("N") + ".jpg");
        try
        {
            var info = await ProbeAsync(source, token);
            var filters = new List<string>();
            if (info.Transfer is "smpte2084" or "arib-std-b67") filters.AddRange(ToneMapFilters);
            filters.Add($"scale={edge}:{edge * 3 / 4}:force_original_aspect_ratio=decrease");
            await RunCaptureAsync(Tool("ffmpeg"), ["-v", "error", "-nostdin", "-n", "-threads", "2", "-filter_threads", "1", "-ss", seconds.ToString("0.###", CultureInfo.InvariantCulture), "-i", source,
                "-frames:v", "1", "-vf", string.Join(',', filters), "-q:v", "2", "-update", "1", preview], token);
            return await File.ReadAllBytesAsync(preview, token);
        }
        finally { if (File.Exists(preview)) File.Delete(preview); }
    }

    private static Process CreateProcess(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return new Process { StartInfo = start };
    }
    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
    private static async Task<string> ReadErrorTailAsync(StreamReader reader)
    {
        var tail = new StringBuilder();
        var buffer = new char[2048];
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            tail.Append(buffer, 0, read);
            if (tail.Length > 8192) tail.Remove(0, tail.Length - 8192);
        }
        return tail.ToString();
    }
    private static async Task<string> RunCaptureAsync(string executable, IEnumerable<string> args, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = CreateProcess(executable, args);
        process.Start();
        using var registration = token.Register(() => Kill(process));
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = ReadErrorTailAsync(process.StandardError);
        try
        {
            await process.WaitForExitAsync(token);
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new InvalidDataException(await stderr);
            return await stdout;
        }
        finally
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            try { await stdout; } catch (OperationCanceledException) { }
            await stderr;
        }
    }
}
