using LightConvert.Core;

// Short-lived decoder: native image allocations are returned to Windows on exit.
try
{
    if (args.Length != 4) throw new ArgumentException("Invalid worker arguments");
    var converter = new MediaConverter(Path.Combine(AppContext.BaseDirectory, "tools"));
    if (args[0] == "thumbnail")
        await File.WriteAllBytesAsync(args[2], await converter.CreateThumbnailAsync(args[1], default, int.Parse(args[3])));
    else if (args[0] == "convert")
    {
        MediaConverter.ConvertPhoto(args[1], args[2], CompressionPreset.All[int.Parse(args[3])], null, default);
    }
    else throw new ArgumentException("Invalid worker command");
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    Environment.ExitCode = 1;
}
