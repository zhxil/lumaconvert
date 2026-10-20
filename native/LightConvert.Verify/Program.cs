using System.Text.Json;
using ImageMagick;
using LightConvert.Core;

// Integration verification exercises actual HEIC decode, JPEG encode, FFmpeg, collision and cancellation.
var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".tmp/native-verification");
Directory.CreateDirectory(root);
try
{
string tools = Path.GetFullPath(args.Length > 1 ? args[1] : "bin");
var converter = new MediaConverter(tools, args.Length > 3 ? Path.GetFullPath(args[3]) : null);
var checks = new List<string>();
void Require(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
async Task<string> FF(params string[] arguments)
{
    var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(tools, "ffmpeg.exe")) { RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true, UseShellExecute = false };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var proc = System.Diagnostics.Process.Start(start)!;
    var error = proc.StandardError.ReadToEndAsync();
    var output = proc.StandardOutput.ReadToEndAsync();
    await proc.WaitForExitAsync();
    if (proc.ExitCode != 0) throw new Exception(await error);
    return await output;
}
var fixture = Path.Combine(root, "fixtures");
Directory.CreateDirectory(fixture);
var heic = Path.Combine(fixture, "原片 中文.heic");
File.Copy(args.Length > 2 ? args[2] : Path.GetFullPath("native/fixtures/sample.heic"), heic, true);
uint fixtureWidth, fixtureHeight;
using (var image = new MagickImage(heic)) { fixtureWidth = image.Width; fixtureHeight = image.Height; }
var original = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(heic));
var photos = new List<long>();
foreach (var preset in CompressionPreset.All)
{
    var result = await converter.ConvertAsync(heic, Path.Combine(root, preset.Name), preset);
    using var check = new MagickImage(result.OutputPath);
    Require(check.Format == MagickFormat.Jpeg && check.Width == fixtureWidth && check.Height == fixtureHeight, "HEIC → JPG / " + preset.Name);
    photos.Add(result.OutputBytes);
}
var collision = await converter.ConvertAsync(heic, Path.Combine(root, "均衡压缩"), CompressionPreset.All[1]);
Require(Path.GetFileName(collision.OutputPath).Contains('_'), "Duplicate output preserved");
Require(original.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(heic))), "Source hash unchanged");
var jpg = Path.Combine(fixture, "large.jpg");
using (var image = new MagickImage(MagickColors.SeaGreen, 4200, 2800)) { image.Quality = 95; image.Write(jpg); }
var resized = await converter.ConvertAsync(jpg, Path.Combine(root, "resize"), CompressionPreset.All[2]);
using (var image = new MagickImage(resized.OutputPath)) Require(image.Width == 2560 && image.Height <= 2560, "Photo long edge limit");
var sameFolder = await converter.ConvertAsync(jpg, fixture, CompressionPreset.All[1]);
Require(sameFolder.OutputPath != jpg && File.Exists(jpg), "JPEG input cannot overwrite itself");
File.Delete(sameFolder.OutputPath); File.Delete(jpg);
var mov = Path.Combine(fixture, "视频 portrait.mov");
await FF("-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=240x320:rate=24", "-f", "lavfi", "-i", "sine=frequency=440", "-t", "3", "-c:v", "libx264", "-c:a", "aac", mov);
var values = new List<double>();
var mp4 = await converter.ConvertAsync(mov, Path.Combine(root, "video"), CompressionPreset.All[1], new InlineProgress(p => values.Add(p.Percent)));
await FF("-v", "error", "-i", mp4.OutputPath, "-f", "null", "-");
var info = await converter.ProbeAsync(mp4.OutputPath);
Require(info.Width == 240 && info.Height == 320 && info.Duration > 2.9, "MOV → MP4 fully decoded, portrait and duration preserved");
Require(values.Any(v => v > 0 && v < 100) && values.Last() == 100, "Real FFmpeg per-file progress");
Require((await converter.CreateThumbnailAsync(heic)).Length > 100, "HEIC thumbnail");
Require((await converter.CreateThumbnailAsync(mov)).Length > 100, "MOV first frame thumbnail");
var hdr = Path.Combine(root, "hdr-synthetic.mov");
await FF("-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=24", "-t", "1", "-vf", "format=yuv420p10le",
    "-c:v", "libx265", "-x265-params", "log-level=error:colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc", "-color_primaries", "bt2020", "-color_trc", "smpte2084", "-colorspace", "bt2020nc", "-tag:v", "hvc1", hdr);
Require((await converter.ProbeAsync(hdr)).Transfer == "smpte2084", "HDR fixture identifies as PQ before conversion");
var sdr = await converter.ConvertAsync(hdr, Path.Combine(root, "hdr-output"), CompressionPreset.All[1]);
await FF("-v", "error", "-i", sdr.OutputPath, "-f", "null", "-");
Require((await converter.ProbeAsync(sdr.OutputPath)).Transfer == "bt709", "Synthetic 10-bit PQ HEVC → SDR H.264 decodes with BT.709 transfer");
var cancel = new CancellationTokenSource();
cancel.Cancel();
try { await converter.ConvertAsync(mov, Path.Combine(root, "cancel-before"), CompressionPreset.All[1], cancellationToken: cancel.Token); throw new Exception("Pre-cancel ignored"); }
catch (OperationCanceledException) { checks.Add("Cancel before start"); }
using var during = new CancellationTokenSource();
try
{
    await converter.ConvertAsync(mov, Path.Combine(root, "cancel-during"), CompressionPreset.All[1],
        new InlineProgress(p => { if (p.Percent > 0) during.Cancel(); }), during.Token);
    throw new Exception("Cancellation ignored");
}
catch (OperationCanceledException) { checks.Add("Cancel during actual FFmpeg conversion"); }
var broken = Path.Combine(root, "broken.heic");
var datedPhoto = Path.Combine(root, "dated-photo.jpg");
using (var photo = new MagickImage(MagickColors.Coral, 180, 120))
{
    var exif = new ExifProfile();
    exif.SetValue(ExifTag.DateTimeOriginal, "2021:05:06 12:34:56");
    exif.SetValue(ExifTag.OffsetTimeOriginal, "+08:00");
    exif.SetValue(ExifTag.SubsecTimeOriginal, "123");
    exif.SetValue(ExifTag.Artist, "private metadata");
    photo.SetProfile(exif); photo.Write(datedPhoto);
}
var datedOutput = await converter.ConvertAsync(datedPhoto, Path.Combine(root, "dates"), CompressionPreset.All[1]);
using (var photo = new MagickImage(datedOutput.OutputPath))
{
    var exif = photo.GetExifProfile();
    Require(exif?.GetValue(ExifTag.DateTimeOriginal)?.Value == "2021:05:06 12:34:56" &&
        exif.GetValue(ExifTag.OffsetTimeOriginal)?.Value == "+08:00" && exif.GetValue(ExifTag.SubsecTimeOriginal)?.Value == "123", "Photo capture date, timezone and subsecond EXIF retained");
    Require(exif?.GetValue(ExifTag.Artist) is null, "Unrelated private metadata removed");
}
Require(Math.Abs((File.GetCreationTimeUtc(datedOutput.OutputPath) - new DateTime(2021, 5, 6, 4, 34, 56, DateTimeKind.Utc)).TotalSeconds) < 1,
    "Photo filesystem date respects EXIF timezone");
var noDate = await converter.ConvertAsync(datedPhoto, Path.Combine(root, "dates-disabled"), CompressionPreset.All[1], preserveCaptureTime: false);
using (var photo = new MagickImage(noDate.OutputPath)) Require(photo.GetExifProfile()?.GetValue(ExifTag.DateTimeOriginal) is null, "Capture date preservation can be disabled");
var fallbackDate = new DateTime(2020, 2, 3, 4, 5, 6, DateTimeKind.Utc);
File.SetLastWriteTimeUtc(heic, fallbackDate);
var fallback = await converter.ConvertAsync(heic, Path.Combine(root, "date-fallback"), CompressionPreset.All[1]);
Require(File.GetLastWriteTimeUtc(fallback.OutputPath) == fallbackDate, "Missing capture metadata falls back to source file time");
var datedVideo = Path.Combine(root, "dated-video.mov");
await FF("-v", "error", "-y", "-i", mov, "-c", "copy", "-metadata", "creation_time=2020-02-03T04:05:06Z", datedVideo);
var datedMp4 = await converter.ConvertAsync(datedVideo, Path.Combine(root, "video-date"), CompressionPreset.All[1]);
var stamp = (await converter.ProbeAsync(datedMp4.OutputPath)).CaptureTime;
Require(DateTimeOffset.Parse(stamp).UtcDateTime == fallbackDate && File.GetLastWriteTimeUtc(datedMp4.OutputPath) == fallbackDate,
    "Video capture metadata and file time retained");
var undatedMp4 = await converter.ConvertAsync(datedVideo, Path.Combine(root, "video-date-disabled"), CompressionPreset.All[1], preserveCaptureTime: false);
Require(string.IsNullOrEmpty((await converter.ProbeAsync(undatedMp4.OutputPath)).CaptureTime), "Disabling capture dates clears video global and stream timestamps");
var outputRoot = Path.Combine(root, "structured");
var album = Path.Combine(root, "相册");
var nestedPhoto = Path.Combine(album, "旅行", "一.jpg");
Require(OutputLayout.Folder(outputRoot, nestedPhoto, album, true) == Path.Combine(outputRoot, "相册", "旅行"), "Unicode imported root and nested folders preserved");
Require(OutputLayout.Folder(outputRoot, nestedPhoto, album, false) == outputRoot && OutputLayout.Folder(outputRoot, nestedPhoto, null, true) == outputRoot,
    "Flat output and individually added files supported");
try { OutputLayout.Folder(outputRoot, Path.Combine(root, "outside.jpg"), album, true); throw new Exception("Path escaped"); }
catch (InvalidDataException) { checks.Add("Source outside imported root rejected"); }
var album2 = Path.Combine(root, "另一个相册");
Require(OutputLayout.Folder(outputRoot, Path.Combine(album2, "旅行", "一.jpg"), album2, true) != OutputLayout.Folder(outputRoot, nestedPhoto, album, true), "Multiple imported roots remain separate");
Directory.CreateDirectory(Path.GetDirectoryName(nestedPhoto)!);
File.Copy(datedPhoto, nestedPhoto);
var nestedOutput = await converter.ConvertAsync(nestedPhoto, OutputLayout.Folder(outputRoot, nestedPhoto, album, true), CompressionPreset.All[1]);
Require(File.Exists(Path.Combine(outputRoot, "相册", "旅行", "一.jpg")) && nestedOutput.OutputPath != nestedPhoto, "Structured conversion writes a real nested output and retains source");
var preview = await converter.CreateThumbnailAsync(datedPhoto, default, 1536);
using (var photo = new MagickImage(preview)) Require(photo.Format == MagickFormat.Png, "Comparison image preview avoids lossy re-encoding");
Require((await converter.CreateThumbnailAsync(datedMp4.OutputPath, default, 1536, 1.5)).Length > 100, "Comparison video supports selecting a later frame");
File.WriteAllText(broken, "bad media");
try { await converter.ConvertAsync(broken, Path.Combine(root, "broken-output"), CompressionPreset.All[1]); throw new Exception("Invalid input accepted"); }
catch (Exception error) when (error is MagickException or InvalidDataException) { checks.Add("Corrupt input fails"); }
Require(!Directory.EnumerateFiles(root, ".lightconvert-*", SearchOption.AllDirectories).Any(), "No conversion temp files after failure / cancellation");
var report = new { utc = DateTimeOffset.UtcNow, checks, jpegBytes = photos, fixtures = fixture,
    limitations = new[] { "Synthetic fixtures only; real iPhone HEIC / Dolby Vision color not accepted", "UI drag gesture requires separate desktop test" } };
File.WriteAllText(Path.Combine(root, "core-verification.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
}
catch (Exception exception)
{
    File.WriteAllText(Path.Combine(root, "verification-failure.txt"), exception.ToString());
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}

sealed class InlineProgress(Action<ConversionProgress> action) : IProgress<ConversionProgress>
{ public void Report(ConversionProgress value) => action(value); }
